namespace Email.Smtp.Configuration;

/// <summary>
/// Selects which transport implementation of <c>IEmailService</c> is registered
/// by <c>AddTransactionalEmail</c>. Driven by the <c>Email:Provider</c>
/// configuration value so a service can switch relays with ZERO code change.
/// </summary>
public enum EmailProvider
{
  /// <summary>
  /// Default. Sends via SMTP (MailKit) — self-hosted Maddy in prod, Mailpit in dev.
  /// No behaviour change for existing services.
  /// </summary>
  Smtp = 0,

  /// <summary>
  /// Sends via the Resend HTTP API (<c>https://api.resend.com/emails</c>).
  /// A reputable managed relay for high-deliverability transactional mail.
  /// </summary>
  Resend = 1,

  /// <summary>
  /// Reserved for the Postmark HTTP API. Recognised by the provider switch so the
  /// configuration surface is stable, but not yet implemented — selecting it throws
  /// a clear <see cref="System.NotSupportedException"/>.
  /// </summary>
  Postmark = 2,
}
