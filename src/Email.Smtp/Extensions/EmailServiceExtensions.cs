using System.Net.Http.Headers;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Email.Smtp.Rendering;
using Email.Smtp.Services;
using Identity.Abstractions.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Email.Smtp.Extensions;

/// <summary>
/// Extension methods for registering transactional email services.
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
    services.TryAddSingleton<IEmailTemplateRenderer, EmbeddedResourceTemplateRenderer>();
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

  /// <summary>
  /// Adds the Resend HTTP API email provider. Registers IEmailService backed by
  /// <see cref="ResendEmailService"/> using a typed HttpClient pre-configured with
  /// the Resend base address and bearer token.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configureOptions">Action to configure Resend options.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddResendEmail(
    this IServiceCollection services,
    Action<ResendOptions> configureOptions)
  {
    services.Configure(configureOptions);
    services.TryAddSingleton<IEmailTemplateRenderer, EmbeddedResourceTemplateRenderer>();
    AddResendHttpClient(services);

    return services;
  }

  /// <summary>
  /// Config-driven registration that selects the email transport from the
  /// <c>Email:Provider</c> configuration value (<c>Smtp</c> | <c>Resend</c> |
  /// <c>Postmark</c>). Defaults to <c>Smtp</c> when the key is absent, so this is a
  /// safe drop-in for services currently on SMTP: flipping the config value (and
  /// supplying the provider's options, e.g. <c>Email:Resend:ApiKey</c>) switches the
  /// relay with ZERO code change.
  /// </summary>
  /// <remarks>
  /// SMTP options are read from the existing <c>Smtp</c> section (unchanged);
  /// Resend options from <c>Email:Resend</c>.
  /// </remarks>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">Application configuration.</param>
  /// <returns>The service collection for chaining.</returns>
  /// <exception cref="NotSupportedException">
  /// Thrown when <c>Email:Provider</c> is <c>Postmark</c> (reserved but not yet implemented).
  /// </exception>
  public static IServiceCollection AddTransactionalEmail(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    var providerOptions =
      configuration.GetSection(EmailProviderOptions.SectionName).Get<EmailProviderOptions>()
      ?? new EmailProviderOptions();

    services.TryAddSingleton<IEmailTemplateRenderer, EmbeddedResourceTemplateRenderer>();

    switch (providerOptions.Provider)
    {
      case EmailProvider.Smtp:
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddScoped<IEmailService, SmtpEmailService>();
        break;

      case EmailProvider.Resend:
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        AddResendHttpClient(services);
        break;

      case EmailProvider.Postmark:
        throw new NotSupportedException(
          "Email:Provider 'Postmark' is a reserved value but not yet implemented. " +
          "Use 'Smtp' or 'Resend'.");

      default:
        throw new NotSupportedException(
          $"Email:Provider '{providerOptions.Provider}' is not supported.");
    }

    return services;
  }

  private static void AddResendHttpClient(IServiceCollection services)
  {
    services.AddHttpClient<IEmailService, ResendEmailService>((sp, client) =>
    {
      var options = sp.GetRequiredService<IOptions<ResendOptions>>().Value;
      client.BaseAddress = new Uri(options.BaseUrl);
      client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", options.ApiKey);
    });
  }
}
