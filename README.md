# Email.Smtp

Transactional email for .NET behind one `IEmailService` abstraction, with a
**config-driven provider switch**: send via **SMTP** (MailKit) or the **Resend**
HTTP API by flipping a single config value — **zero code change**. Compatible with
Mailpit (dev), self-hosted Maddy/Postal, any standard SMTP server, or Resend.

## Features

- One `IEmailService` abstraction, multiple transports (`Email:Provider = Smtp | Resend | Postmark`)
- MailKit-based SMTP sending (replaces deprecated System.Net.Mail.SmtpClient)
- Resend HTTP API provider for high-deliverability managed sending
- Code-based HTML email templates (embedded resources)
- `{{Placeholder}}` template rendering with caching
- SSL/TLS support for production SMTP servers
- Optional authentication (skip for Mailpit in dev)
- Shared `email_sent` / `email_failed` log tokens across every provider (Loki-greppable)

## Provider selection (config-driven, zero code change)

Register once with `AddTransactionalEmail(IConfiguration)` and switch transports via config:

```csharp
// In Program.cs — reads Email:Provider, defaults to Smtp when absent
builder.Services.AddTransactionalEmail(builder.Configuration);
```

```jsonc
// SMTP (default) — options read from the existing "Smtp" section
{
  "Email": { "Provider": "Smtp" },
  "Smtp": { "Host": "mailpit", "Port": 1025, "UseSsl": false,
            "FromAddress": "noreply@localhost", "FromName": "SaaS Platform" }
}

// Resend — flip Provider, add the Email:Resend section, no code change
{
  "Email": { "Provider": "Resend" },
  "Email:Resend": {
    "ApiKey": "re_xxx",                       // secret; supply via env/secret store
    "FromAddress": "noreply@dloizides.com",   // must be a Resend-verified domain
    "FromName": "SaaS Platform"
  }
}
```

`Postmark` is a reserved provider value (recognised by the switch) but not yet
implemented — selecting it throws a clear `NotSupportedException`. You can also register a
single provider directly with `AddSmtpEmail(...)` or `AddResendEmail(...)`.

## Templates Included

| Template | Placeholders |
|----------|-------------|
| `otp-code` | `AppName`, `Code`, `ExpiryMinutes` |
| `welcome` | `AppName`, `UserName`, `TenantName`, `DashboardUrl` |
| `password-reset` | `AppName`, `ResetUrl`, `ExpiryMinutes` |
| `payment-receipt` | `AppName`, `PlanName`, `Amount`, `Date`, `InvoiceNumber` |
| `payment-failed` | `AppName`, `PlanName`, `Amount`, `BillingUrl` |
| `account-deletion` | `AppName` |

## Usage

```csharp
// In Program.cs or DI setup
builder.Services.AddSmtpEmail(options =>
    builder.Configuration.GetSection("Smtp").Bind(options));

// In appsettings.json
{
  "Smtp": {
    "Host": "mailpit",     // or "postal.yourdomain.com"
    "Port": 1025,          // or 587 for TLS
    "UseSsl": false,
    "FromAddress": "noreply@localhost",
    "FromName": "SaaS Platform"
  }
}

// Send a templated email
var result = await emailService.SendTemplatedAsync(
    EmailTemplateNames.OtpCode,
    new EmailRecipient { Address = "user@example.com" },
    "Your Verification Code",
    new Dictionary<string, string>
    {
        ["AppName"] = "My SaaS",
        ["Code"] = "123456",
        ["ExpiryMinutes"] = "5"
    });
```
