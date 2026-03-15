using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Email.Smtp.Rendering;
using Email.Smtp.Services;
using Identity.Abstractions.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Email.Smtp.Extensions;

/// <summary>
/// Extension methods for registering SMTP email services.
/// </summary>
public static class EmailServiceExtensions
{
  /// <summary>
  /// Adds the SMTP email service with MailKit and embedded template rendering.
  /// Registers IEmailService for direct email sending.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configureOptions">Action to configure SMTP options.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddSmtpEmail(
    this IServiceCollection services,
    Action<SmtpOptions> configureOptions)
  {
    services.Configure(configureOptions);
    services.AddSingleton<IEmailTemplateRenderer, EmbeddedResourceTemplateRenderer>();
    services.AddScoped<IEmailService, SmtpEmailService>();

    return services;
  }

  /// <summary>
  /// Adds SMTP email and registers as INotificationService (replacing any prior registration).
  /// Bridges INotificationService.SendEmailAsync to the SMTP IEmailService.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configureOptions">Action to configure SMTP options.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddSmtpEmailNotifications(
    this IServiceCollection services,
    Action<SmtpOptions> configureOptions)
  {
    services.AddSmtpEmail(configureOptions);
    services.AddScoped<INotificationService, SmtpNotificationBridge>();

    return services;
  }
}
