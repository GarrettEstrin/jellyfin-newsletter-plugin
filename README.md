# Jellyfin Slack Newsletter plugin

Posts a weekly summary to a Slack channel:

- **New this week**: movies and episodes added in the last N days (episodes grouped per show, new series flagged).
- **Top viewers**: users ranked by time actually watched.
- **Most watched**: shows and movies ranked by total watch time, with unique viewer counts.

Targets Jellyfin 12.x (.NET 10).

## How the leaderboard gets its data

The plugin listens for Jellyfin's playback start/stop events and writes one record per finished playback to
`<jellyfin data dir>/slack-newsletter/plays.jsonl`. It does not read server logs. The events already carry the
user, the item and the playback position, so there is nothing fragile to parse, and watch time excludes time spent
paused. Only playback that happens **after the plugin is installed** is counted, so the first leaderboard will be thin.
Records older than 120 days are pruned on startup.

## Build

Requires the .NET 10 SDK.

    dotnet publish -c Release -o out

## Install

Copy `out/Jellyfin.Plugin.SlackNewsletter.dll` into a new folder under the Jellyfin plugins directory, for example:

- Docker: `/config/plugins/SlackNewsletter_0.1.0.0/`
- Linux package: `/var/lib/jellyfin/plugins/SlackNewsletter_0.1.0.0/`

Restart Jellyfin.

## Set up

1. In Slack, create an app with **Incoming Webhooks** enabled and add a webhook for the target channel.
2. In Jellyfin: Dashboard → Plugins → Slack Newsletter. Paste the webhook URL and save.
3. Click **Save and send now** to post a test.
4. The weekly send is a scheduled task (Dashboard → Scheduled Tasks → Slack Newsletter). It defaults to Monday 09:00
   server time; edit the trigger there.

`GET /SlackNewsletter/Preview` (admin token) returns the Slack payload without sending it.

## Notes

- Playback shorter than the configured minimum (default 120 s) is ignored.
- A stream that was already running when the server restarted is not counted, because its start was never seen.
