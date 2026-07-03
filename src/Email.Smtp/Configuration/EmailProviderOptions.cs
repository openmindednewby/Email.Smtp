namespace Email.Smtp.Configuration;

/// <summary>
/// Top-level email options that select the transport provider. Bound from the
/// <c>Email</c> configuration section, so <c>Email:Provider</c> maps to
/// <see cref="Provider"/>.
/// </summary>
public class EmailProviderOptions
{
  /// <summary>
  /// Configuration section name in appsettings.json.
  /// </summary>
  public const string SectionName = "Email";

  /// <summary>
  /// Which transport to register. Defaults to <see cref="EmailProvider.Smtp"/>
  /// so existing services keep their current behaviour when the key is absent.
  /// Values (case-insensitive): <c>Smtp</c>, <c>Resend</c>, <c>Postmark</c>.
  /// </summary>
  public EmailProvider Provider { get; set; } = EmailProvider.Smtp;
}
