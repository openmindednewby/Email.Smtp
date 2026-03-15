using System.Collections.Concurrent;
using System.Reflection;
using Email.Abstractions.Abstractions;
using Microsoft.Extensions.Logging;

namespace Email.Smtp.Rendering;

/// <summary>
/// Renders email templates from embedded resources with {{Placeholder}} replacement.
/// Templates are cached after first load for performance.
/// </summary>
public class EmbeddedResourceTemplateRenderer : IEmailTemplateRenderer
{
  private static readonly ConcurrentDictionary<string, string> TemplateCache = new();
  private readonly ILogger<EmbeddedResourceTemplateRenderer> _logger;

  public EmbeddedResourceTemplateRenderer(
    ILogger<EmbeddedResourceTemplateRenderer> logger)
  {
    _logger = logger;
  }

  /// <inheritdoc />
  public Task<string> RenderAsync(
    string templateName,
    Dictionary<string, string> data,
    CancellationToken cancellationToken = default)
  {
    var template = TemplateCache.GetOrAdd(templateName, LoadTemplate);

    var rendered = data.Aggregate(
      template,
      (current, kvp) => current.Replace("{{" + kvp.Key + "}}", kvp.Value));

    return Task.FromResult(rendered);
  }

  private string LoadTemplate(string templateName)
  {
    var assembly = Assembly.GetExecutingAssembly();
    var resourceName = $"Email.Smtp.Templates.{templateName}.html";

    using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream is null)
    {
      _logger.LogWarning(
        "Email template '{TemplateName}' not found as embedded resource '{ResourceName}'. Using fallback.",
        templateName,
        resourceName);

      return BuildFallbackTemplate(templateName);
    }

    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
  }

  private static string BuildFallbackTemplate(string templateName) =>
    "<!DOCTYPE html><html><body style=\"font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;\">" +
    "<p>Template '" + templateName + "' is not configured.</p>" +
    "<p>{{Content}}</p></body></html>";
}
