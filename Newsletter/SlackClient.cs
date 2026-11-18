using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.SlackNewsletter.Newsletter;

public sealed class SlackClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SlackClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task PostAsync(string webhookUrl, NewsletterResult newsletter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)
            || !Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Slack webhook URL is missing or is not an https:// URL.");
        }

        var json = JsonSerializer.Serialize(new { text = newsletter.FallbackText, blocks = newsletter.Blocks });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Slack returned {(int)response.StatusCode}: {body}");
        }
    }
}
