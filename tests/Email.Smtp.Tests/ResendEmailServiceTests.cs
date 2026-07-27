using System.Net;
using System.Text;
using System.Text.Json;
using Email.Abstractions;
using Email.Abstractions.Abstractions;
using Email.Smtp.Configuration;
using Email.Smtp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Email.Smtp.Tests;

/// <summary>
/// Verifies the Resend HTTP provider builds the correct request body, maps the
/// API response to an <see cref="EmailResult"/>, and preserves the
/// email_sent / email_failed observability contract. The network call is faked
/// via a capturing <see cref="HttpMessageHandler"/> — no real Resend account needed.
/// </summary>
public class ResendEmailServiceTests
{
  private const string BaseUrl = "https://api.resend.com/";

  /// <summary>Captures the outgoing request and returns a canned response.</summary>
  private sealed class CapturingHandler(HttpStatusCode status, string responseJson)
    : HttpMessageHandler
  {
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Request = request;
      if (request.Content is not null)
      {
        RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
      }

      return new HttpResponseMessage(status)
      {
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
      };
    }
  }

  private static ResendOptions TestOptions() => new()
  {
    BaseUrl = BaseUrl,
    ApiKey = "re_test_key",
    FromAddress = "noreply@dloizides.com",
    FromName = "Dloizides",
  };

  // Typed to HttpMessageHandler, not CapturingHandler, so a handler that THROWS
  // (no HTTP status at all) can exercise the same construction path.
  private static ResendEmailService BuildService(HttpMessageHandler handler)
  {
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
    var renderer = new Mock<IEmailTemplateRenderer>();
    return new ResendEmailService(
      httpClient,
      Options.Create(TestOptions()),
      renderer.Object,
      NullLogger<ResendEmailService>.Instance);
  }

  private static EmailMessage SampleMessage() => new()
  {
    To = new EmailRecipient { Address = "ceo@bigco.com", Name = "Jane CEO" },
    FromAddress = "noreply@dloizides.com",
    FromName = "Dloizides",
    Subject = "Verify your email",
    HtmlBody = "<p>Click to verify</p>",
    PlainTextBody = "Click to verify",
    ReplyTo = "support@dloizides.com",
  };

  [Fact]
  public async Task SendAsync_On2xx_ReturnsSuccess()
  {
    var handler = new CapturingHandler(HttpStatusCode.OK, "{\"id\":\"abc-123\"}");
    var service = BuildService(handler);

    var result = await service.SendAsync(SampleMessage());

    result.IsSuccess.Should().BeTrue();
  }

  [Fact]
  public async Task SendAsync_PostsToEmailsEndpoint()
  {
    var handler = new CapturingHandler(HttpStatusCode.OK, "{\"id\":\"abc-123\"}");
    var service = BuildService(handler);

    await service.SendAsync(SampleMessage());

    handler.Request.Should().NotBeNull();
    handler.Request!.Method.Should().Be(HttpMethod.Post);
    handler.Request.RequestUri!.ToString().Should().Be($"{BaseUrl}emails");
  }

  [Fact]
  public async Task SendAsync_BuildsExpectedPayload()
  {
    var handler = new CapturingHandler(HttpStatusCode.OK, "{\"id\":\"abc-123\"}");
    var service = BuildService(handler);

    var message = SampleMessage() with
    {
      CustomHeaders = new Dictionary<string, string>
      {
        ["List-Unsubscribe"] = "<https://x/u>",
      },
    };

    await service.SendAsync(message);

    using var doc = JsonDocument.Parse(handler.RequestBody!);
    var root = doc.RootElement;

    root.GetProperty("from").GetString().Should().Be("Dloizides <noreply@dloizides.com>");
    root.GetProperty("to")[0].GetString().Should().Be("Jane CEO <ceo@bigco.com>");
    root.GetProperty("subject").GetString().Should().Be("Verify your email");
    root.GetProperty("html").GetString().Should().Be("<p>Click to verify</p>");
    root.GetProperty("text").GetString().Should().Be("Click to verify");
    root.GetProperty("reply_to").GetString().Should().Be("support@dloizides.com");
    root.GetProperty("headers").GetProperty("List-Unsubscribe").GetString()
      .Should().Be("<https://x/u>");
  }

  [Fact]
  public async Task SendAsync_OmitsNullOptionalFields()
  {
    var handler = new CapturingHandler(HttpStatusCode.OK, "{\"id\":\"abc-123\"}");
    var service = BuildService(handler);

    var message = new EmailMessage
    {
      To = new EmailRecipient { Address = "plain@x.com" },
      FromAddress = "noreply@dloizides.com",
      FromName = "Dloizides",
      Subject = "Hi",
      HtmlBody = "<p>Hi</p>",
    };

    await service.SendAsync(message);

    using var doc = JsonDocument.Parse(handler.RequestBody!);
    var root = doc.RootElement;

    // No plain text, no reply-to, no headers were set -> omitted from JSON.
    root.TryGetProperty("text", out _).Should().BeFalse();
    root.TryGetProperty("reply_to", out _).Should().BeFalse();
    root.TryGetProperty("headers", out _).Should().BeFalse();
    // Recipient without a name serialises as the bare address.
    root.GetProperty("to")[0].GetString().Should().Be("plain@x.com");
  }

  [Fact]
  public async Task SendAsync_OnNon2xx_ReturnsFailureWithStatusAndBody()
  {
    var handler = new CapturingHandler(
      HttpStatusCode.UnprocessableEntity, "{\"message\":\"domain not verified\"}");
    var service = BuildService(handler);

    var result = await service.SendAsync(SampleMessage());

    result.IsSuccess.Should().BeFalse();
    result.ErrorMessage.Should().Contain("422");
    result.ErrorMessage.Should().Contain("domain not verified");
    result.FailureKind.Should().Be(EmailFailureKind.Permanent);
  }

  // ---------------------------------------------------------------------------
  // Permanence WIRING, not just the classifier.
  //
  // EmailFailureClassifierTests already proves the rule in isolation. These
  // prove ResendEmailService actually CALLS it, which is the part that can be
  // silently wrong while every pure-function test stays green. Prod runs the
  // SMTP provider (Email:Provider), so this HTTP branch never executes live —
  // these stubbed-transport tests are the only thing standing behind it.
  //
  // Remember the inversion: for HTTP, 4xx is the PERMANENT class. That is the
  // OPPOSITE of SMTP, where 5yz is permanent.
  // ---------------------------------------------------------------------------

  [Theory]
  [InlineData(HttpStatusCode.BadRequest)]          // 400
  [InlineData(HttpStatusCode.Unauthorized)]        // 401 — a bad API key will not fix itself
  [InlineData(HttpStatusCode.Forbidden)]           // 403
  [InlineData(HttpStatusCode.NotFound)]            // 404
  [InlineData(HttpStatusCode.UnprocessableEntity)] // 422
  public async Task SendAsync_OnProviderClientError_IsPermanent(HttpStatusCode status)
  {
    var handler = new CapturingHandler(status, "{\"message\":\"nope\"}");
    var service = BuildService(handler);

    var result = await service.SendAsync(SampleMessage());

    result.IsSuccess.Should().BeFalse();
    result.FailureKind.Should().Be(EmailFailureKind.Permanent);
    result.IsPermanentFailure.Should().BeTrue();
  }

  [Theory]
  [InlineData(HttpStatusCode.RequestTimeout)]      // 408 — 4xx but retryable
  [InlineData((HttpStatusCode)429)]                // rate limit: permanent here would DROP mail in a burst
  [InlineData(HttpStatusCode.InternalServerError)] // 500
  [InlineData(HttpStatusCode.BadGateway)]          // 502 — transient over HTTP, permanent over SMTP
  [InlineData(HttpStatusCode.ServiceUnavailable)]  // 503
  [InlineData(HttpStatusCode.GatewayTimeout)]      // 504
  public async Task SendAsync_OnRetryableOrProviderOutage_IsTransient(HttpStatusCode status)
  {
    var handler = new CapturingHandler(status, "{\"message\":\"later\"}");
    var service = BuildService(handler);

    var result = await service.SendAsync(SampleMessage());

    result.IsSuccess.Should().BeFalse();
    result.FailureKind.Should().Be(EmailFailureKind.Transient);
    result.IsPermanentFailure.Should().BeFalse();
  }

  [Fact]
  public async Task SendAsync_WhenTransportThrows_IsTransient()
  {
    // No HTTP status at all => no verdict from the provider => must stay
    // retryable, or a blip in our own egress would look like a bad address.
    var service = BuildService(new ThrowingHandler());

    var result = await service.SendAsync(SampleMessage());

    result.IsSuccess.Should().BeFalse();
    result.FailureKind.Should().Be(EmailFailureKind.Transient);
  }

  private sealed class ThrowingHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
      => throw new HttpRequestException("connection reset");
  }

  [Fact]
  public async Task SendAsync_On2xxWithoutId_StillSucceeds()
  {
    var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
    var service = BuildService(handler);

    var result = await service.SendAsync(SampleMessage());

    result.IsSuccess.Should().BeTrue();
  }
}
