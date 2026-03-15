# Email.Smtp

SMTP email service using MailKit with code-based HTML templates. Compatible with Mailpit (dev), Postal (self-hosted), or any standard SMTP server.

## Features

- MailKit-based SMTP sending (replaces deprecated System.Net.Mail.SmtpClient)
- Code-based HTML email templates (embedded resources)
- `{{Placeholder}}` template rendering with caching
- SSL/TLS support for production SMTP servers
- Optional authentication (skip for Mailpit in dev)

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
