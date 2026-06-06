using Email.Abstractions;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Email.Smtp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Email.Smtp.Tests;

/// <summary>
/// Verifies that per-message branding (Reply-To) and extra MIME headers
/// (e.g. List-Unsubscribe) flow from <see cref="EmailMessage"/> onto the built
/// <see cref="MimeMessage"/>. This is the Email.Smtp 1.3.0 capability Kefi
/// Phase 13 relies on for tenant-branded, unsubscribable lifecycle mail.
/// </summary>
public class SmtpEmailServiceHeaderTests
{
  private static SmtpOptions TestOptions() => new()
  {
    Host = "127.0.0.1",
    Port = 9,
    UseSsl = false,
    FromAddress = "noreply@test.local",
    FromName = "Test Sender",
  };

  /// <summary>Transport stub that captures the built message instead of sending.</summary>
  private sealed class CapturingTransportEmailService(
    IOptions<SmtpOptions> options,
    IEmailTemplateRenderer templateRenderer,
    ILogger<SmtpEmailService> logger)
    : SmtpEmailService(options, templateRenderer, logger)
  {
    public MimeMessage? Captured { get; private set; }

    protected override Task SendViaSmtpAsync(MimeMessage message, CancellationToken cancellationToken)
    {
      Captured = message;
      return Task.CompletedTask;
    }
  }

  private static CapturingTransportEmailService BuildService()
  {
    var logger = new Mock<ILogger<SmtpEmailService>>();
    var renderer = new Mock<IEmailTemplateRenderer>();
    return new CapturingTransportEmailService(
      Options.Create(TestOptions()), renderer.Object, logger.Object);
  }

  [Fact]
  public async Task SendAsync_WithCustomHeadersAndReplyTo_WritesThemOntoTheMessage()
  {
    var service = BuildService();
    var unsubscribe = "<https://acme.kefi.dloizides.com/api/v1/unsubscribe/abc>, <mailto:u@x>";
    var message = new EmailMessage
    {
      To = new EmailRecipient { Address = "attendee@example.com" },
      FromAddress = "noreply@test.local",
      FromName = "Salsa Nights CY",
      Subject = "Your event is in 7 days",
      HtmlBody = "<p>See you soon</p>",
      ReplyTo = "organizer@example.com",
      CustomHeaders = new Dictionary<string, string>
      {
        ["List-Unsubscribe"] = unsubscribe,
        ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
      },
    };

    var result = await service.SendAsync(message);

    result.IsSuccess.Should().BeTrue();
    service.Captured.Should().NotBeNull();
    service.Captured!.From.ToString().Should().Contain("Salsa Nights CY");
    service.Captured.ReplyTo.ToString().Should().Contain("organizer@example.com");
    service.Captured.Headers["List-Unsubscribe"].Should().Be(unsubscribe);
    service.Captured.Headers["List-Unsubscribe-Post"].Should().Be("List-Unsubscribe=One-Click");
  }

  [Fact]
  public async Task SendAsync_WithoutCustomHeaders_AddsNoUnsubscribeHeader()
  {
    var service = BuildService();
    var message = new EmailMessage
    {
      To = new EmailRecipient { Address = "attendee@example.com" },
      FromAddress = "noreply@test.local",
      Subject = "Registration confirmed",
      HtmlBody = "<p>Thanks</p>",
    };

    await service.SendAsync(message);

    service.Captured.Should().NotBeNull();
    service.Captured!.Headers.Contains("List-Unsubscribe").Should().BeFalse();
  }
}
