using Email.Abstractions.Abstractions;
using Email.Smtp.Extensions;
using Email.Smtp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Email.Smtp.Tests;

/// <summary>
/// Verifies the config-driven provider switch (<c>AddTransactionalEmail</c>)
/// registers the correct <see cref="IEmailService"/> implementation for each
/// <c>Email:Provider</c> value, defaults to SMTP, and rejects unimplemented ones —
/// the mechanism that lets a service change relay with ZERO code change.
/// </summary>
public class EmailProviderSelectionTests
{
  private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
    new ConfigurationBuilder().AddInMemoryCollection(values).Build();

  private static ServiceProvider BuildProvider(IConfiguration configuration)
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddTransactionalEmail(configuration);
    return services.BuildServiceProvider();
  }

  [Fact]
  public void DefaultProvider_WhenKeyAbsent_IsSmtp()
  {
    var config = BuildConfig(new Dictionary<string, string?>
    {
      ["Smtp:Host"] = "mailpit",
      ["Smtp:Port"] = "1025",
    });

    using var provider = BuildProvider(config);
    var service = provider.GetRequiredService<IEmailService>();

    service.Should().BeOfType<SmtpEmailService>();
  }

  [Fact]
  public void Provider_Smtp_RegistersSmtpEmailService()
  {
    var config = BuildConfig(new Dictionary<string, string?>
    {
      ["Email:Provider"] = "Smtp",
      ["Smtp:Host"] = "mailpit",
    });

    using var provider = BuildProvider(config);
    var service = provider.GetRequiredService<IEmailService>();

    service.Should().BeOfType<SmtpEmailService>();
  }

  [Fact]
  public void Provider_Resend_RegistersResendEmailService()
  {
    var config = BuildConfig(new Dictionary<string, string?>
    {
      ["Email:Provider"] = "Resend",
      ["Email:Resend:ApiKey"] = "re_test",
      ["Email:Resend:FromAddress"] = "noreply@dloizides.com",
    });

    using var provider = BuildProvider(config);
    var service = provider.GetRequiredService<IEmailService>();

    service.Should().BeOfType<ResendEmailService>();
  }

  [Fact]
  public void Provider_IsCaseInsensitive()
  {
    var config = BuildConfig(new Dictionary<string, string?>
    {
      ["Email:Provider"] = "resend",
      ["Email:Resend:ApiKey"] = "re_test",
    });

    using var provider = BuildProvider(config);
    var service = provider.GetRequiredService<IEmailService>();

    service.Should().BeOfType<ResendEmailService>();
  }

  [Fact]
  public void Provider_Postmark_ThrowsNotSupported()
  {
    var config = BuildConfig(new Dictionary<string, string?>
    {
      ["Email:Provider"] = "Postmark",
    });

    var services = new ServiceCollection();
    services.AddLogging();

    var act = () => services.AddTransactionalEmail(config);

    act.Should().Throw<NotSupportedException>().WithMessage("*Postmark*");
  }
}
