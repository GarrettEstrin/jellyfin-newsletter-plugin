namespace Jellyfin.Plugin.SlackNewsletter.Tracking;

/// <summary>One finished playback by one user.</summary>
public sealed record PlayRecord(
    Guid UserId,
    string UserName,
    Guid ItemId,
    string Title,
    Guid? SeriesId,
    string? SeriesName,
    string ItemType,
    double WatchedSeconds,
    DateTime EndedUtc);
