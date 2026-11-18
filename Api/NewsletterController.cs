using Jellyfin.Plugin.SlackNewsletter.Newsletter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SlackNewsletter.Api;

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("SlackNewsletter")]
public class NewsletterController : ControllerBase
{
    private readonly NewsletterBuilder _builder;
    private readonly SlackClient _slack;

    public NewsletterController(NewsletterBuilder builder, SlackClient slack)
    {
        _builder = builder;
        _slack = slack;
    }

    /// <summary>Builds the newsletter and posts it now, even if it would be empty.</summary>
    [HttpPost("Send")]
    public async Task<ActionResult> Send(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.SlackWebhookUrl))
        {
            return BadRequest("Set the Slack webhook URL first.");
        }

        try
        {
            await _slack.PostAsync(config.SlackWebhookUrl, _builder.Build(DateTime.UtcNow), cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return StatusCode(502, ex.Message);
        }
    }

    /// <summary>Returns the Slack payload without sending it.</summary>
    [HttpGet("Preview")]
    public ActionResult Preview()
    {
        var result = _builder.Build(DateTime.UtcNow);
        return Ok(new { text = result.FallbackText, blocks = result.Blocks });
    }
}
