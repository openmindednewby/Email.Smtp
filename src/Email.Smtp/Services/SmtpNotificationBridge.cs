using Email.Abstractions;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Identity.Abstractions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Email.Smtp.Services;

/// <summary>
/// Bridges the INotificationService.SendEmailAsync interface to the IEmailService.
/// Registers as INotificationService, delegating email to SMTP and logging SMS as unsupported.
/// Use alongside AddTwilioNotifications when you need both SMS and email.
/// </summary>
public class SmtpNotificationBridge : INotificationService
{
  private readonly IEmailService _emailService;
  private readonly SmtpOptions _options;
  private readonly ILogger<SmtpNotificationBridge> _logger;

  public SmtpNotificationBridge(
    IEmailService emailService,
    IOptions<SmtpOptions> options,
    ILogger<SmtpNotificationBridge> logger)
  {
    _emailService = emailService;
    _options = options.Value;
    _logger = logger;
  }

  /// <inheritdoc />
  public Task<bool> SendSmsAsync(
    string phoneNumber,
    string message,
    CancellationToken cancellationToken = default)
  {
    _logger.LogWarning(
      "SMS sending is not available via SMTP bridge. Configure Twilio for SMS support.");
    return Task.FromResult(false);
  }

  /// <inheritdoc />
  public async Task<bool> SendEmailAsync(
    string email,
    string subject,
    string body,
    CancellationToken cancellationToken = default)
  {
    var message = new EmailMessage
    {
      To = new EmailRecipient { Address = email },
      FromAddress = _options.FromAddress,
      FromName = _options.FromName,
      Subject = subject,
      HtmlBody = body,
      PlainTextBody = body,
    };

    var result = await _emailService.SendAsync(message, cancellationToken);
    return result.IsSuccess;
  }
}
