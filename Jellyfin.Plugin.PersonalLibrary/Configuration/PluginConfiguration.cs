using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PersonalLibrary.Configuration;

/// <summary>Configuration du plugin (Tableau de bord → Plugins → Personal Library).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Les utilisateurs peuvent régler eux-mêmes leur bibliothèque personnelle.</summary>
    public bool EnableSelfService { get; set; } = true;

    /// <summary>Utilisateurs privés du libre-service (leurs choix existants restent appliqués).</summary>
    public Guid[] DisabledUserIds { get; set; } = [];
}
