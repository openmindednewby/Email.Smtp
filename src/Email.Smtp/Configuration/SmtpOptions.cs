namespace Email.Smtp.Configuration;

/// <summary>
/// Configuration options for the SMTP email service.
/// </summary>
public class SmtpOptions
{
  /// <summary>
  /// Configuration section name in appsettings.json.
  /// </summary>
  public const string SectionName = "Smtp";

  /// <summary>
  /// SMTP server hostname (e.g., "mailpit" for dev, "postal.yourdomain.com" for prod).
  /// </summary>
  public string Host { get; set; } = "localhost";

  /// <summary>
  /// SMTP server port (e.g., 1025 for Mailpit, 587 for TLS, 465 for SSL).
  /// </summary>
  public int Port { get; set; } = 1025;

  /// <summary>
  /// SMTP username for authentication. Leave empty for servers that don't require auth (e.g., Mailpit).
  /// </summary>
  public string? Username { get; set; }

  /// <summary>
  /// SMTP password for authentication.
  /// </summary>
  public string? Password { get; set; }

  /// <summary>
  /// Whether to use SSL/TLS when connecting to the SMTP server.
  /// </summary>
  public bool UseSsl { get; set; }

  /// <summary>
  /// Default sender email address.
  /// </summary>
  public string FromAddress { get; set; } = "noreply@localhost";

  /// <summary>
  /// Default sender display name.
  /// </summary>
  public string FromName { get; set; } = "SaaS Platform";
}
