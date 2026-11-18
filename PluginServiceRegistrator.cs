using Jellyfin.Plugin.SlackNewsletter.Newsletter;
using Jellyfin.Plugin.SlackNewsletter.Tracking;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SlackNewsletter;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PlayStore>();
        serviceCollection.AddSingleton<NewsletterBuilder>();
        serviceCollection.AddSingleton<SlackClient>();
        serviceCollection.AddHostedService<PlaybackTracker>();
    }
}
