using Email.Abstractions;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Email.Smtp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Email.Smtp.Tests;

/// <summary>
/// Guards the cross-producer email observability contract: every send outcome
/// must emit a structured log event whose message template starts with the
/// stable token <c>email_sent</c> or <c>email_failed</c>. These tokens are
/// queried verbatim in Loki/Grafana and the Daily Report — renaming them is a
/// breaking change for the whole observability pipeline.
/// </summary>
public class SmtpEmailServiceEventContractTests
{
  private const string EmailSentToken = "email_sent";
  private const string EmailFailedToken = "email_failed";

  private static SmtpOptions TestOptions() => new()
  {
    // Port 9 (discard protocol) on localhost — nothing listens there, so the
    // un-overridden transport fails fast in the failure-path tests.
    Host = "127.0.0.1",
    Port = 9,
    UseSsl = false,
    FromAddress = "noreply@test.local",
    FromName = "Test Sender",
  };

  private static EmailMessage TestMessage() => new()
  {
    To = new EmailRecipient { Address = "recipient@example.com", Name = "Recipient" },
    FromAddress = "noreply@test.local",
    Subject = "Test subject",
    HtmlBody = "<p>Hello</p>",
  };

  /// <summary>
  /// Captures structured log invocations so assertions can inspect the
  /// original message template and its named properties.
  /// </summary>
  private sealed class CapturingLogger : ILogger<SmtpEmailService>
  {
    public List<(LogLevel Level, string Template, IReadOnlyList<KeyValuePair<string, object?>> Properties)> Events { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      var properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
      var template = properties.FirstOrDefault(p => p.Key == "{OriginalFormat}").Value?.ToString() ?? string.Empty;
      Events.Add((logLevel, template, properties));
    }
  }

  /// <summary>
  /// Subclass that replaces the network transport with a no-op so the
  /// success path (and its email_sent event) can run without an SMTP server.
  /// </summary>
  private sealed class FakeTransportEmailService(
    IOptions<SmtpOptions> options,
    IEmailTemplateRenderer templateRenderer,
    ILogger<SmtpEmailService> logger)
    : SmtpEmailService(options, templateRenderer, logger)
  {
    protected override Task SendViaSmtpAsync(MimeMessage message, CancellationToken cancellationToken)
      => Task.CompletedTask;
  }

  private static (SmtpEmailService Service, CapturingLogger Logger, Mock<IEmailTemplateRenderer> Renderer) BuildFakeTransportService()
  {
    var logger = new CapturingLogger();
    var renderer = new Mock<IEmailTemplateRenderer>();
    var service = new FakeTransportEmailService(
      Options.Create(TestOptions()), renderer.Object, logger);
    return (service, logger, renderer);
  }

  private static (SmtpEmailService Service, CapturingLogger Logger, Mock<IEmailTemplateRenderer> Renderer) BuildRealTransportService()
  {
    var logger = new CapturingLogger();
    var renderer = new Mock<IEmailTemplateRenderer>();
    var service = new SmtpEmailService(
      Options.Create(TestOptions()), renderer.Object, logger);
    return (service, logger, renderer);
  }

  [Fact]
  public async Task SendAsync_OnSuccess_EmitsEmailSentEvent()
  {
    var (service, logger, _) = BuildFakeTransportService();

    var result = await service.SendAsync(TestMessage());

    result.IsSuccess.Should().BeTrue();
    var sentEvent = logger.Events.Should()
      .ContainSingle(e => e.Template.StartsWith(EmailSentToken)).Subject;
    sentEvent.Level.Should().Be(LogLevel.Information);
    sentEvent.Properties.Should().Contain(p =>
      p.Key == "Recipient" && (string?)p.Value == "recipient@example.com");
    sentEvent.Properties.Should().Contain(p =>
      p.Key == "Subject" && (string?)p.Value == "Test subject");
    sentEvent.Properties.Should().Contain(p => p.Key == "MessageId");
  }

  [Fact]
  public async Task SendAsync_OnSuccess_TemplateCarriesContractFields()
  {
    var (service, logger, _) = BuildFakeTransportService();

    await service.SendAsync(TestMessage());

    var sentEvent = logger.Events.Single(e => e.Template.StartsWith(EmailSentToken));
    sentEvent.Template.Should().Be("email_sent to={Recipient} subject={Subject} messageId={MessageId}");
  }

  [Fact]
  public async Task SendAsync_WhenTransportFails_EmitsEmailFailedEventAndReturnsFailure()
  {
    var (service, logger, _) = BuildRealTransportService();

    var result = await service.SendAsync(TestMessage());

    result.IsSuccess.Should().BeFalse();
    var failedEvent = logger.Events.Should()
      .ContainSingle(e => e.Template.StartsWith(EmailFailedToken)).Subject;
    failedEvent.Level.Should().Be(LogLevel.Error);
    // failureKind was APPENDED in 1.4.0. The cross-producer contract is the
    // leading token (asserted by StartsWith above, and by LogQL |= "email_failed"),
    // so adding a trailing field is additive, not a break.
    failedEvent.Template.Should().Be(
      "email_failed to={Recipient} subject={Subject} error={Error} failureKind={FailureKind}");
    failedEvent.Properties.Should().Contain(p =>
      p.Key == "Recipient" && (string?)p.Value == "recipient@example.com");
    // A dead TCP connection is no verdict from the server, so it must stay
    // retryable -- concluding "bad address" here would drop deliverable mail.
    failedEvent.Properties.Should().Contain(p =>
      p.Key == "FailureKind" && (EmailFailureKind?)p.Value == EmailFailureKind.Transient);
    result.FailureKind.Should().Be(EmailFailureKind.Transient);
  }

  [Fact]
  public async Task SendTemplatedAsync_WhenRendererThrows_EmitsEmailFailedEventWithTemplateName()
  {
    var (service, logger, renderer) = BuildFakeTransportService();
    renderer
      .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("template not found"));

    var result = await service.SendTemplatedAsync(
      "welcome", new EmailRecipient { Address = "recipient@example.com" }, "Welcome!", []);

    result.IsSuccess.Should().BeFalse();
    var failedEvent = logger.Events.Should()
      .ContainSingle(e => e.Template.StartsWith(EmailFailedToken)).Subject;
    failedEvent.Level.Should().Be(LogLevel.Error);
    failedEvent.Template.Should().Be("email_failed to={Recipient} subject={Subject} template={TemplateName} error={Error}");
    failedEvent.Properties.Should().Contain(p =>
      p.Key == "TemplateName" && (string?)p.Value == "welcome");
  }

  [Fact]
  public async Task SendTemplatedAsync_OnSuccess_EmitsEmailSentEvent()
  {
    var (service, logger, renderer) = BuildFakeTransportService();
    renderer
      .Setup(r => r.RenderAsync("welcome", It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("<p>rendered</p>");

    var result = await service.SendTemplatedAsync(
      "welcome", new EmailRecipient { Address = "recipient@example.com" }, "Welcome!", []);

    result.IsSuccess.Should().BeTrue();
    logger.Events.Should().ContainSingle(e => e.Template.StartsWith(EmailSentToken));
    logger.Events.Should().NotContain(e => e.Template.StartsWith(EmailFailedToken));
  }
}
