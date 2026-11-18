using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SlackNewsletter.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Slack incoming-webhook URL. The send day/time is set under Scheduled Tasks.</summary>
    public string SlackWebhookUrl { get; set; } = string.Empty;

    /// <summary>Public base URL of this server (e.g. https://jellyfin.example.com). Adds links to titles when set.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    public int LookbackDays { get; set; } = 7;

    public int LeaderboardSize { get; set; } = 5;

    public int MaxNewItemsListed { get; set; } = 15;

    /// <summary>Playbacks shorter than this are not counted.</summary>
    public int MinPlaySeconds { get; set; } = 120;

    /// <summary>Comma-separated usernames to leave off the leaderboard.</summary>
    public string ExcludedUsers { get; set; } = string.Empty;

    /// <summary>Skip the scheduled post when nothing was added and nothing was watched.</summary>
    public bool SkipIfEmpty { get; set; } = true;
}
