using Jellyfin.Plugin.PersonalLibrary.Engine;
using Jellyfin.Plugin.PersonalLibrary.Selection;
using Jellyfin.Plugin.PersonalLibrary.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PersonalLibrary;

/// <summary>
/// Enregistre les services du plugin dans le conteneur de Jellyfin (10.11 et 12).
/// La tâche d'après scan est découverte toute seule par Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SelectionStore>();
        serviceCollection.AddSingleton<SelectionPlanner>();
        serviceCollection.AddSingleton<TagApplier>();
        serviceCollection.AddSingleton<ApplyService>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<ApplyService>());
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
    }
}
