using System.Globalization;
using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SlackNewsletter.Tracking;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SlackNewsletter.Newsletter;

public sealed record NewsletterResult(string FallbackText, IReadOnlyList<object> Blocks, bool IsEmpty);

public sealed class NewsletterBuilder
{
    private static readonly string[] Medals = { "🥇", "🥈", "🥉" };

    private readonly ILibraryManager _library;
    private readonly PlayStore _store;
    private readonly ILogger<NewsletterBuilder> _logger;

    public NewsletterBuilder(ILibraryManager library, PlayStore store, ILogger<NewsletterBuilder> logger)
    {
        _library = library;
        _store = store;
        _logger = logger;
    }

    public NewsletterResult Build(DateTime nowUtc)
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var days = Math.Max(1, config.LookbackDays);
        var since = nowUtc.AddDays(-days);
        var top = Math.Clamp(config.LeaderboardSize, 1, 20);

        var newLines = BuildNewLines(config, since, out var newTotal);
        var plays = _store.GetSince(since);

        var blocks = new List<object>
        {
            new { type = "header", text = new { type = "plain_text", text = $"🎬 Jellyfin weekly · {since.ToLocalTime():MMM d} – {nowUtc.ToLocalTime():MMM d}", emoji = true } }
        };

        // --- New this week ---
        blocks.Add(Section("*🆕 New this week*"));
        if (newLines.Count == 0)
        {
            blocks.Add(Section("_Nothing new was added._"));
        }
        else
        {
            blocks.AddRange(Sections(newLines));
        }

        blocks.Add(new { type = "divider" });

        // --- Leaderboards ---
        if (plays.Count == 0)
        {
            blocks.Add(Section("*🏆 Leaderboard*\n_No viewing recorded this week._"));
        }
        else
        {
            var viewers = plays
                .GroupBy(p => p.UserId)
                .Select(g => new
                {
                    Name = g.OrderByDescending(p => p.EndedUtc).First().UserName,
                    Seconds = g.Sum(p => p.WatchedSeconds),
                    Plays = g.Count()
                })
                .OrderByDescending(v => v.Seconds)
                .Take(top)
                .Select((v, i) => $"{Rank(i)} *{Escape(v.Name)}* — {FormatDuration(v.Seconds)} · {v.Plays} {(v.Plays == 1 ? "play" : "plays")}")
                .ToList();

            var titles = plays
                .GroupBy(p => p.SeriesId ?? p.ItemId)
                .Select(g =>
                {
                    var first = g.First();
                    return new
                    {
                        Id = g.Key,
                        Name = first.SeriesName ?? first.Title,
                        Seconds = g.Sum(p => p.WatchedSeconds),
                        Viewers = g.Select(p => p.UserId).Distinct().Count()
                    };
                })
                .OrderByDescending(t => t.Seconds)
                .Take(top)
                .Select((t, i) => $"{Rank(i)} {Link(config, t.Id, t.Name)} — {FormatDuration(t.Seconds)} · {t.Viewers} {(t.Viewers == 1 ? "viewer" : "viewers")}")
                .ToList();

            blocks.Add(Section("*🏆 Top viewers*\n" + string.Join("\n", viewers)));
            blocks.Add(Section("*📺 Most watched*\n" + string.Join("\n", titles)));
        }

        var fallback = $"Jellyfin weekly: {newTotal} new item(s), {plays.Count} play(s) this week.";
        return new NewsletterResult(fallback, blocks, newTotal == 0 && plays.Count == 0);
    }

    private List<string> BuildNewLines(Configuration.PluginConfiguration config, DateTime since, out int total)
    {
        var items = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
            MinDateCreated = since,
            IsVirtualItem = false,
            Recursive = true
        });

        var entries = new List<(DateTime Created, string Line)>();

        foreach (var movie in items.OfType<Movie>())
        {
            var year = movie.ProductionYear is int y ? $" ({y})" : string.Empty;
            entries.Add((movie.DateCreated, $"🎞️ {Link(config, movie.Id, movie.Name + year)}"));
        }

        foreach (var group in items.OfType<Episode>().GroupBy(e => e.SeriesId))
        {
            var episodes = group.ToList();
            var name = episodes[0].SeriesName ?? "Unknown series";
            var isNewSeries = false;
            try
            {
                isNewSeries = group.Key != Guid.Empty
                    && _library.GetItemById(group.Key) is { } series
                    && series.DateCreated >= since;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not look up series {SeriesId}", group.Key);
            }

            var target = group.Key == Guid.Empty ? episodes[0].Id : group.Key;
            var count = episodes.Count;
            var detail = isNewSeries
                ? $"new series · {count} {(count == 1 ? "episode" : "episodes")}"
                : $"{count} new {(count == 1 ? "episode" : "episodes")}";
            entries.Add((episodes.Max(e => e.DateCreated), $"📺 {Link(config, target, name)} — {detail}"));
        }

        total = entries.Count;
        var max = Math.Max(1, config.MaxNewItemsListed);
        var lines = entries.OrderByDescending(e => e.Created).Take(max).Select(e => e.Line).ToList();
        if (entries.Count > max)
        {
            lines.Add($"_…and {entries.Count - max} more_");
        }

        return lines;
    }

    private static string Rank(int index) => index < Medals.Length ? Medals[index] : $"{index + 1}.";

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{span.TotalHours:0.#} h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)span.TotalMinutes)} min");
    }

    private static string Link(Configuration.PluginConfiguration config, Guid id, string text)
    {
        var safe = Escape(text);
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            return $"*{safe}*";
        }

        return $"<{config.ServerUrl.TrimEnd('/')}/web/index.html#!/details?id={id:N}|{safe}>";
    }

    // Slack mrkdwn requires these three characters to be escaped.
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static object Section(string mrkdwn) =>
        new { type = "section", text = new { type = "mrkdwn", text = mrkdwn } };

    /// <summary>Slack caps a section's text at 3000 chars, so long lists are split across sections.</summary>
    private static IEnumerable<object> Sections(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length + line.Length + 1 > 2800 && sb.Length > 0)
            {
                yield return Section(sb.ToString());
                sb.Clear();
            }

            sb.AppendLine(line);
        }

        if (sb.Length > 0)
        {
            yield return Section(sb.ToString().TrimEnd());
        }
    }
}
