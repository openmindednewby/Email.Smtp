using Email.Abstractions;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Email.Smtp.Services;

/// <summary>
/// SMTP-based email service using MailKit.
/// Compatible with Mailpit (dev), Postal (self-hosted), or any standard SMTP server.
/// </summary>
public class SmtpEmailService : IEmailService
{
  private readonly SmtpOptions _options;
  private readonly IEmailTemplateRenderer _templateRenderer;
  private readonly ILogger<SmtpEmailService> _logger;

  public SmtpEmailService(
    IOptions<SmtpOptions> options,
    IEmailTemplateRenderer templateRenderer,
    ILogger<SmtpEmailService> logger)
  {
    _options = options.Value;
    _templateRenderer = templateRenderer;
    _logger = logger;
  }

  /// <inheritdoc />
  public async Task<EmailResult> SendAsync(
    EmailMessage message,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var mimeMessage = BuildMimeMessage(message);
      await SendViaSmtpAsync(mimeMessage, cancellationToken);

      // "email_sent" is the cross-producer observability contract: a stable,
      // greppable token queryable in Loki/Grafana across every service that
      // sends mail (LogQL: |= "email_sent"). The producer comes from the Loki
      // service_name label; messageId enables correlation with the mail
      // server's own delivery log. Do not reword the leading token.
      _logger.LogInformation(
        "email_sent to={Recipient} subject={Subject} messageId={MessageId}",
        message.To.Address,
        message.Subject,
        mimeMessage.MessageId);

      return EmailResult.Success();
    }
    catch (Exception ex)
    {
      // "email_failed" — same observability contract as email_sent above.
      _logger.LogError(
        ex,
        "email_failed to={Recipient} subject={Subject} error={Error}",
        message.To.Address,
        message.Subject,
        ex.Message);

      return EmailResult.Failure(ex.Message);
    }
  }

  /// <inheritdoc />
  public async Task<EmailResult> SendTemplatedAsync(
    string templateName,
    EmailRecipient recipient,
    string subject,
    Dictionary<string, string> templateData,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var htmlBody = await _templateRenderer.RenderAsync(
        templateName, templateData, cancellationToken);

      var message = new EmailMessage
      {
        To = recipient,
        FromAddress = _options.FromAddress,
        FromName = _options.FromName,
        Subject = subject,
        HtmlBody = htmlBody,
      };

      return await SendAsync(message, cancellationToken);
    }
    catch (Exception ex)
    {
      // Template-rendering failures also count as a failed send for
      // observability purposes — same "email_failed" contract, with the
      // template name for diagnosis.
      _logger.LogError(
        ex,
        "email_failed to={Recipient} subject={Subject} template={TemplateName} error={Error}",
        recipient.Address,
        subject,
        templateName,
        ex.Message);

      return EmailResult.Failure(ex.Message);
    }
  }

  private MimeMessage BuildMimeMessage(EmailMessage message)
  {
    var fromAddress = message.FromAddress ?? _options.FromAddress;
    var fromName = message.FromName ?? _options.FromName;

    var mimeMessage = new MimeMessage();
    mimeMessage.From.Add(new MailboxAddress(fromName, fromAddress));
    mimeMessage.To.Add(new MailboxAddress(
      message.To.Name ?? message.To.Address,
      message.To.Address));
    mimeMessage.Subject = message.Subject;

    if (!string.IsNullOrEmpty(message.ReplyTo))
    {
      mimeMessage.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
    }

    var bodyBuilder = new BodyBuilder();

    if (!string.IsNullOrEmpty(message.HtmlBody))
    {
      bodyBuilder.HtmlBody = message.HtmlBody;
    }

    if (!string.IsNullOrEmpty(message.PlainTextBody))
    {
      bodyBuilder.TextBody = message.PlainTextBody;
    }

    mimeMessage.Body = bodyBuilder.ToMessageBody();
    return mimeMessage;
  }

  /// <summary>
  /// Performs the actual SMTP transport. Virtual so tests can substitute the
  /// network call and exercise the send/log pipeline without a real server.
  /// </summary>
  protected virtual async Task SendViaSmtpAsync(
    MimeMessage message,
    CancellationToken cancellationToken)
  {
    using var client = new SmtpClient();

    var secureSocketOptions = _options.UseSsl
      ? MailKit.Security.SecureSocketOptions.StartTls
      : MailKit.Security.SecureSocketOptions.None;

    await client.ConnectAsync(
      _options.Host,
      _options.Port,
      secureSocketOptions,
      cancellationToken);

    if (!string.IsNullOrEmpty(_options.Username))
    {
      await client.AuthenticateAsync(
        _options.Username,
        _options.Password,
        cancellationToken);
    }

    await client.SendAsync(message, cancellationToken);
    await client.DisconnectAsync(quit: true, cancellationToken);
  }
}
