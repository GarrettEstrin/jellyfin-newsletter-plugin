using Jellyfin.Plugin.SlackNewsletter.Newsletter;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SlackNewsletter.ScheduledTasks;

/// <summary>
/// Shows up under Dashboard → Scheduled Tasks. Defaults to Mondays at 09:00 server time;
/// the trigger can be edited there.
/// </summary>
public sealed class WeeklyNewsletterTask : IScheduledTask
{
    private readonly NewsletterBuilder _builder;
    private readonly SlackClient _slack;
    private readonly ILogger<WeeklyNewsletterTask> _logger;

    public WeeklyNewsletterTask(NewsletterBuilder builder, SlackClient slack, ILogger<WeeklyNewsletterTask> logger)
    {
        _builder = builder;
        _slack = slack;
        _logger = logger;
    }

    public string Name => "Send Slack newsletter";

    public string Key => "SlackNewsletterWeekly";

    public string Description => "Posts new media and the viewing leaderboard to Slack.";

    public string Category => "Slack Newsletter";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Slack Newsletter plugin is not initialised.");

        progress.Report(10);
        var newsletter = _builder.Build(DateTime.UtcNow);

        if (newsletter.IsEmpty && config.SkipIfEmpty)
        {
            _logger.LogInformation("Nothing new and nothing watched; skipping Slack post");
            progress.Report(100);
            return;
        }

        progress.Report(60);
        await _slack.PostAsync(config.SlackWebhookUrl, newsletter, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Posted weekly newsletter to Slack");
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Monday,
            TimeOfDayTicks = TimeSpan.FromHours(9).Ticks
        };
    }
}
