using System.Net.Sockets;
using Email.Abstractions;
using Email.Smtp.Services;
using FluentAssertions;
using MailKit.Net.Smtp;
using Xunit;

namespace Email.Smtp.Tests;

/// <summary>
/// Verifies <see cref="EmailFailureClassifier"/>, the Email.Smtp 1.4.0 addition
/// that lets callers tell "this address will never accept mail" apart from
/// "the mail server is briefly unwell".
/// </summary>
/// <remarks>
/// The bias under test is asymmetric on purpose: mislabelling a transient
/// failure as permanent DROPS a real email, while the reverse costs one wasted
/// retry. So the "unknown input stays Transient" cases below are the important
/// ones, not the happy path.
/// </remarks>
public class EmailFailureClassifierTests
{
  // ---------- SMTP: 5yz is permanent, 4yz is not ----------

  // Numeric values below are MailKit's actual SmtpStatusCode members, verified
  // against MailKit 4.17.0 rather than assumed. Note TransactionFailed is 554
  // (permanent), NOT the 451 its name suggests -- 451 is ErrorInProcessing.
  [Theory]
  [InlineData(SmtpStatusCode.MailboxUnavailable)]        // 550 — the prod case: 5.1.1 user does not exist
  [InlineData(SmtpStatusCode.MailboxNameNotAllowed)]     // 553
  [InlineData(SmtpStatusCode.ExceededStorageAllocation)] // 552 — documented as deliberately permanent
  [InlineData(SmtpStatusCode.TransactionFailed)]         // 554
  [InlineData(SmtpStatusCode.CommandNotImplemented)]     // 502 (SMTP-level, unrelated to HTTP 502)
  public void Classify_SmtpPermanentReplyClass_IsPermanent(SmtpStatusCode status)
  {
    var ex = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, status, "rejected");

    EmailFailureClassifier.Classify(ex).Should().Be(EmailFailureKind.Permanent);
  }

  [Theory]
  [InlineData(SmtpStatusCode.ServiceNotAvailable)]       // 421
  [InlineData(SmtpStatusCode.MailboxBusy)]               // 450
  [InlineData(SmtpStatusCode.ErrorInProcessing)]         // 451 — typical greylisting reply
  [InlineData(SmtpStatusCode.InsufficientStorage)]       // 452
  [InlineData(SmtpStatusCode.TemporaryAuthenticationFailure)] // 454
  public void Classify_SmtpTransientReplyClass_IsTransient(SmtpStatusCode status)
  {
    var ex = new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, status, "try later");

    EmailFailureClassifier.Classify(ex).Should().Be(EmailFailureKind.Transient);
  }

  // ---------- Anything without a verdict stays retryable ----------

  public static TheoryData<Exception> NonVerdictExceptions() => new()
  {
    new SmtpProtocolException("protocol desync"),
    new SocketException(),
    new IOException("connection reset"),
    new TimeoutException("timed out"),
    new OperationCanceledException(),
    new InvalidOperationException("something else entirely"),
  };

  [Theory]
  [MemberData(nameof(NonVerdictExceptions))]
  public void Classify_ExceptionWithoutServerReply_IsTransient(Exception ex)
  {
    // We never received a server verdict, so we must not conclude the address
    // is bad — that would drop a deliverable email.
    EmailFailureClassifier.Classify(ex).Should().Be(EmailFailureKind.Transient);
  }

  [Fact]
  public void Classify_NullException_Throws()
  {
    var act = () => EmailFailureClassifier.Classify(null!);

    act.Should().Throw<ArgumentNullException>();
  }

  // ---------- HTTP provider: the INVERSE — 4xx is permanent ----------

  [Theory]
  [InlineData(400)]
  [InlineData(403)]
  [InlineData(422)] // Resend's invalid-recipient shape
  [InlineData(404)]
  public void ClassifyProviderHttpStatus_ClientError_IsPermanent(int status)
  {
    EmailFailureClassifier.ClassifyProviderHttpStatus(status)
      .Should().Be(EmailFailureKind.Permanent);
  }

  [Theory]
  [InlineData(408)] // retryable despite being 4xx
  [InlineData(429)] // rate limit — treating this as permanent would drop mail in a burst
  [InlineData(500)]
  [InlineData(502)]
  [InlineData(503)]
  public void ClassifyProviderHttpStatus_RetryableOrServerError_IsTransient(int status)
  {
    EmailFailureClassifier.ClassifyProviderHttpStatus(status)
      .Should().Be(EmailFailureKind.Transient);
  }

  [Fact]
  public void SmtpAndHttpDisagreeOn502_WhichIsTheWholePoint()
  {
    // Guards against someone "unifying" the two rules: 502 is a permanent SMTP
    // reply class but a transient HTTP status. Same digits, opposite meaning.
    EmailFailureClassifier
      .Classify(new SmtpCommandException(
        SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.CommandNotImplemented, "no"))
      .Should().Be(EmailFailureKind.Permanent);

    EmailFailureClassifier.ClassifyProviderHttpStatus(502)
      .Should().Be(EmailFailureKind.Transient);
  }

  // ---------- EmailResult wiring ----------

  [Fact]
  public void Failure_WithoutKind_StaysTransient_SoPre130CallSitesAreUnchanged()
  {
    var result = EmailResult.Failure("boom");

    result.FailureKind.Should().Be(EmailFailureKind.Transient);
    result.IsPermanentFailure.Should().BeFalse();
  }

  [Fact]
  public void PermanentFailure_SetsKindAndFlag()
  {
    var result = EmailResult.PermanentFailure("5.1.1 User does not exist");

    result.IsSuccess.Should().BeFalse();
    result.FailureKind.Should().Be(EmailFailureKind.Permanent);
    result.IsPermanentFailure.Should().BeTrue();
  }

  [Fact]
  public void Success_HasNoFailureKind()
  {
    var result = EmailResult.Success();

    result.FailureKind.Should().Be(EmailFailureKind.None);
    result.IsPermanentFailure.Should().BeFalse();
  }
}
