using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SlackNewsletter.Tracking;

/// <summary>
/// Listens for playback start/stop events and records how long each user actually watched.
/// This replaces log scraping: the events carry the user, item and position directly.
/// </summary>
public sealed class PlaybackTracker : IHostedService
{
    private readonly ISessionManager _sessions;
    private readonly PlayStore _store;
    private readonly ILogger<PlaybackTracker> _logger;
    private readonly ConcurrentDictionary<string, ActiveStream> _active = new();

    public PlaybackTracker(ISessionManager sessions, PlayStore store, ILogger<PlaybackTracker> logger)
    {
        _sessions = sessions;
        _store = store;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _store.Prune(TimeSpan.FromDays(120));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not prune play log");
        }

        _sessions.PlaybackStart += OnPlaybackStart;
        _sessions.PlaybackStopped += OnPlaybackStopped;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackStart -= OnPlaybackStart;
        _sessions.PlaybackStopped -= OnPlaybackStopped;
        return Task.CompletedTask;
    }

    private static string Key(PlaybackProgressEventArgs e) => $"{e.DeviceId}|{e.Item?.Id}";

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Item is null)
        {
            return;
        }

        _active[Key(e)] = new ActiveStream(e.PlaybackPositionTicks ?? 0, DateTime.UtcNow);
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        try
        {
            if (e.Item is null || e.Users is null || e.Users.Count == 0)
            {
                return;
            }

            _active.TryRemove(Key(e), out var started);

            var runtimeTicks = e.Item.RunTimeTicks ?? 0;
            double watchedSeconds;

            if (started is not null && e.PlaybackPositionTicks.HasValue && e.PlaybackPositionTicks.Value >= started.StartTicks)
            {
                // Position delta ignores time spent paused.
                watchedSeconds = TimeSpan.FromTicks(e.PlaybackPositionTicks.Value - started.StartTicks).TotalSeconds;
            }
            else if (started is not null)
            {
                watchedSeconds = (DateTime.UtcNow - started.StartedUtc).TotalSeconds;
            }
            else
            {
                return; // never saw the start (e.g. server restarted mid-stream)
            }

            if (runtimeTicks > 0)
            {
                watchedSeconds = Math.Min(watchedSeconds, TimeSpan.FromTicks(runtimeTicks).TotalSeconds);
            }

            var config = Plugin.Instance?.Configuration;
            var minSeconds = config?.MinPlaySeconds ?? 120;
            if (watchedSeconds < minSeconds)
            {
                return;
            }

            var excluded = ParseList(config?.ExcludedUsers);
            var episode = e.Item as Episode;

            foreach (var user in e.Users)
            {
                if (excluded.Contains(user.Username))
                {
                    continue;
                }

                _store.Append(new PlayRecord(
                    user.Id,
                    user.Username,
                    e.Item.Id,
                    e.Item.Name,
                    episode?.SeriesId is { } sid && sid != Guid.Empty ? sid : null,
                    episode?.SeriesName,
                    e.Item.GetType().Name,
                    watchedSeconds,
                    DateTime.UtcNow));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record playback");
        }
    }

    internal static HashSet<string> ParseList(string? csv) =>
        new(
            (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    private sealed record ActiveStream(long StartTicks, DateTime StartedUtc);
}
