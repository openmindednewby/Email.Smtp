namespace Email.Smtp.Configuration;

/// <summary>
/// Configuration options for the Resend HTTP API email provider.
/// Bound from the <c>Email:Resend</c> configuration section.
/// </summary>
public class ResendOptions
{
  /// <summary>
  /// Configuration section name in appsettings.json.
  /// </summary>
  public const string SectionName = "Email:Resend";

  /// <summary>
  /// Base URL of the Resend API. Overridable for testing/self-hosted proxies;
  /// defaults to the public Resend endpoint. Must end with a trailing slash.
  /// </summary>
  public string BaseUrl { get; set; } = "https://api.resend.com/";

  /// <summary>
  /// Resend API key (secret, format <c>re_...</c>). Supply via a secret store /
  /// environment variable, never hard-code. Required when the provider is Resend.
  /// </summary>
  public string ApiKey { get; set; } = string.Empty;

  /// <summary>
  /// Default sender email address. Must be on a domain verified in the Resend
  /// dashboard (e.g. <c>noreply@dloizides.com</c>).
  /// </summary>
  public string FromAddress { get; set; } = "noreply@localhost";

  /// <summary>
  /// Default sender display name.
  /// </summary>
  public string FromName { get; set; } = "SaaS Platform";
}
