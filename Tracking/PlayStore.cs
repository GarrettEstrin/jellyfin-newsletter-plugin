using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SlackNewsletter.Tracking;

/// <summary>Append-only JSON-lines log of finished playbacks, kept in the Jellyfin data folder.</summary>
public sealed class PlayStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<PlayStore> _logger;

    public PlayStore(IApplicationPaths paths, ILogger<PlayStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(paths.DataPath, "slack-newsletter");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "plays.jsonl");
    }

    public void Append(PlayRecord record)
    {
        var line = JsonSerializer.Serialize(record);
        lock (_lock)
        {
            File.AppendAllText(_path, line + "\n");
        }
    }

    public IReadOnlyList<PlayRecord> GetSince(DateTime sinceUtc)
    {
        var result = new List<PlayRecord>();
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return result;
            }

            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var record = JsonSerializer.Deserialize<PlayRecord>(line);
                    if (record is not null && record.EndedUtc >= sinceUtc)
                    {
                        result.Add(record);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Skipping unreadable line in play log");
                }
            }
        }

        return result;
    }

    /// <summary>Drops records older than <paramref name="keep"/> so the file does not grow forever.</summary>
    public void Prune(TimeSpan keep)
    {
        var cutoff = DateTime.UtcNow - keep;
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var kept = new List<string>();
            foreach (var line in File.ReadLines(_path))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<PlayRecord>(line);
                    if (record is not null && record.EndedUtc >= cutoff)
                    {
                        kept.Add(line);
                    }
                }
                catch (JsonException)
                {
                    // drop corrupt lines
                }
            }

            File.WriteAllLines(_path, kept);
        }
    }
}
