using Email.Abstractions;
using MailKit.Net.Smtp;

namespace Email.Smtp.Services;

/// <summary>
/// Decides whether a failed email send is worth retrying, for both providers
/// this package ships (SMTP via MailKit, and the Resend HTTP API).
/// </summary>
/// <remarks>
/// <para>
/// <b>The two providers signal permanence with OPPOSITE digits.</b> Read this
/// before editing either method:
/// </para>
/// <list type="bullet">
///   <item><description><b>SMTP</b> (RFC 5321 §4.2.1): <b>5yz = PERMANENT</b>
///   ("SHOULD NOT repeat the exact request"), 4yz = transient. So <c>550</c>
///   user-unknown is permanent.</description></item>
///   <item><description><b>HTTP</b> (Resend): <b>4xx = PERMANENT</b> (the
///   request is wrong), 5xx = transient (their outage). So <c>422</c> invalid
///   recipient is permanent while <c>503</c> is not.</description></item>
/// </list>
/// <para>
/// Anything not positively identified as permanent is
/// <see cref="EmailFailureKind.Transient"/> by construction — transport
/// exceptions, TLS failures, timeouts, cancellations, unparseable replies.
/// That asymmetry is deliberate and load-bearing: calling a transient failure
/// permanent DROPS a real email, whereas calling a permanent one transient
/// costs one wasted retry. Never widen the permanent set on a hunch.
/// </para>
/// <para>
/// Deliberately NOT special-cased: SMTP <c>552</c> (storage allocation
/// exceeded) stays permanent per its reply class, even though a full mailbox
/// may later drain — servers that mean "try again" are expected to send
/// <c>452</c>. If a specific server needs an exception, add it HERE with a
/// comment naming that server, never at a call-site where the other consumers
/// won't inherit it.
/// </para>
/// </remarks>
public static class EmailFailureClassifier
{
  /// <summary>Lowest reply code in RFC 5321's permanent (5yz) SMTP class.</summary>
  private const int SmtpPermanentClassFloor = 500;

  /// <summary>First SMTP code above the permanent class.</summary>
  private const int SmtpPermanentClassCeiling = 600;

  /// <summary>Lowest HTTP status treated as a caller-side (permanent) error.</summary>
  private const int HttpClientErrorFloor = 400;

  /// <summary>First HTTP status above the client-error class.</summary>
  private const int HttpClientErrorCeiling = 500;

  /// <summary>HTTP 408 Request Timeout — retryable despite being 4xx.</summary>
  private const int HttpRequestTimeout = 408;

  /// <summary>HTTP 429 Too Many Requests — retryable despite being 4xx.</summary>
  private const int HttpTooManyRequests = 429;

  /// <summary>
  /// Classifies an exception thrown by a send attempt (the SMTP path).
  /// </summary>
  /// <param name="exception">The exception the send threw.</param>
  /// <returns>
  /// <see cref="EmailFailureKind.Permanent"/> only for an SMTP 5yz reply;
  /// <see cref="EmailFailureKind.Transient"/> for everything else.
  /// </returns>
  public static EmailFailureKind Classify(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);

    // Only SmtpCommandException carries a server reply code. SmtpProtocolException,
    // SocketException, IOException, TimeoutException and OperationCanceledException
    // all mean "we never got a verdict" -> retryable.
    if (exception is not SmtpCommandException smtp)
    {
      return EmailFailureKind.Transient;
    }

    var code = (int)smtp.StatusCode;
    var isPermanentReply = code >= SmtpPermanentClassFloor && code < SmtpPermanentClassCeiling;

    return isPermanentReply ? EmailFailureKind.Permanent : EmailFailureKind.Transient;
  }

  /// <summary>
  /// Classifies a non-2xx HTTP status from an email provider API (the Resend path).
  /// </summary>
  /// <param name="statusCode">The provider's HTTP status code.</param>
  /// <returns>
  /// <see cref="EmailFailureKind.Permanent"/> for a 4xx that reflects a bad
  /// request, excluding the retryable 408/429; otherwise
  /// <see cref="EmailFailureKind.Transient"/>.
  /// </returns>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>This does NOT give you SMTP-equivalent bad-recipient detection.</b>
  /// Resend accepts a syntactically valid but non-existent recipient with
  /// <c>200</c> and reports the bounce LATER, out of band (webhook) — so a
  /// permanent recipient failure never reaches this method. What a 4xx here
  /// actually means is an error on OUR side: bad API key (401), unverified
  /// sending domain (403/422), malformed payload (400).
  /// </para>
  /// <para>
  /// Consequence for anyone switching <c>Email:Provider</c> from Smtp to
  /// Resend: a mistyped recipient that today yields SMTP <c>5.1.1</c> →
  /// <see cref="EmailFailureKind.Permanent"/> → HTTP 422 at the call-site will
  /// instead look like a SUCCESS. Handling Resend bounces needs a webhook
  /// receiver, which this package does not provide.
  /// </para>
  /// </remarks>
  public static EmailFailureKind ClassifyProviderHttpStatus(int statusCode)
  {
    // 429 rate-limit and 408 timeout are 4xx but explicitly retryable — treating
    // a rate-limited send as permanent would silently drop mail during a burst.
    if (statusCode is HttpTooManyRequests or HttpRequestTimeout)
    {
      return EmailFailureKind.Transient;
    }

    var isClientError = statusCode >= HttpClientErrorFloor && statusCode < HttpClientErrorCeiling;

    return isClientError ? EmailFailureKind.Permanent : EmailFailureKind.Transient;
  }
}
