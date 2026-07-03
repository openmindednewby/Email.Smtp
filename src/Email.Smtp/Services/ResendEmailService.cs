using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Email.Abstractions;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Email.Smtp.Services;

/// <summary>
/// Email service that sends via the Resend HTTP API (<c>POST /emails</c>).
/// A drop-in <see cref="IEmailService"/> alternative to <see cref="SmtpEmailService"/>
/// for high-deliverability transactional mail through a reputable managed relay.
/// The <see cref="HttpClient"/> is expected to be pre-configured (base address +
/// <c>Authorization: Bearer</c> header) by the typed-client registration.
/// </summary>
public class ResendEmailService : IEmailService
{
  // Relative to the client's BaseAddress (https://api.resend.com/).
  private const string SendPath = "emails";

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  private readonly HttpClient _httpClient;
  private readonly ResendOptions _options;
  private readonly IEmailTemplateRenderer _templateRenderer;
  private readonly ILogger<ResendEmailService> _logger;

  public ResendEmailService(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    IEmailTemplateRenderer templateRenderer,
    ILogger<ResendEmailService> logger)
  {
    _httpClient = httpClient;
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
      var payload = BuildPayload(message);

      using var response = await _httpClient.PostAsJsonAsync(
        SendPath, payload, SerializerOptions, cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = $"Resend API returned {(int)response.StatusCode}: {body}";

        // "email_failed" is the cross-producer observability contract shared with
        // SmtpEmailService — a stable, greppable token queryable in Loki/Grafana
        // (LogQL: |= "email_failed"). Do not reword the leading token.
        _logger.LogError(
          "email_failed to={Recipient} subject={Subject} error={Error}",
          message.To.Address,
          message.Subject,
          error);

        return EmailResult.Failure(error);
      }

      var messageId = await ReadMessageIdAsync(response, cancellationToken);

      // "email_sent" — same observability contract as SmtpEmailService.
      _logger.LogInformation(
        "email_sent to={Recipient} subject={Subject} messageId={MessageId}",
        message.To.Address,
        message.Subject,
        messageId);

      return EmailResult.Success();
    }
    catch (Exception ex)
    {
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

  private ResendSendRequest BuildPayload(EmailMessage message)
  {
    var fromAddress = message.FromAddress ?? _options.FromAddress;
    var fromName = message.FromName ?? _options.FromName;

    var from = string.IsNullOrEmpty(fromName)
      ? fromAddress
      : $"{fromName} <{fromAddress}>";

    var to = string.IsNullOrEmpty(message.To.Name)
      ? message.To.Address
      : $"{message.To.Name} <{message.To.Address}>";

    Dictionary<string, string>? headers = null;
    if (message.CustomHeaders is { Count: > 0 })
    {
      headers = new Dictionary<string, string>(message.CustomHeaders);
    }

    return new ResendSendRequest
    {
      From = from,
      To = [to],
      Subject = message.Subject,
      Html = string.IsNullOrEmpty(message.HtmlBody) ? null : message.HtmlBody,
      Text = string.IsNullOrEmpty(message.PlainTextBody) ? null : message.PlainTextBody,
      ReplyTo = string.IsNullOrEmpty(message.ReplyTo) ? null : message.ReplyTo,
      Headers = headers,
    };
  }

  private static async Task<string?> ReadMessageIdAsync(
    HttpResponseMessage response,
    CancellationToken cancellationToken)
  {
    try
    {
      var result = await response.Content.ReadFromJsonAsync<ResendSendResponse>(
        cancellationToken);
      return result?.Id;
    }
    catch (Exception)
    {
      // A missing/malformed id in an otherwise-2xx response must not turn a
      // successful send into a failure — messageId is diagnostic only.
      return null;
    }
  }

  /// <summary>Request body for <c>POST https://api.resend.com/emails</c>.</summary>
  private sealed record ResendSendRequest
  {
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string[] To { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("html")]
    public string? Html { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("reply_to")]
    public string? ReplyTo { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
  }

  /// <summary>Success response body from Resend — carries the message id.</summary>
  private sealed record ResendSendResponse
  {
    [JsonPropertyName("id")]
    public string? Id { get; init; }
  }
}
