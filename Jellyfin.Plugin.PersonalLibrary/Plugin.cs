using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Jellyfin.Plugin.PersonalLibrary.Configuration;
using Jellyfin.Plugin.PersonalLibrary.Engine;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.PersonalLibrary;

/// <summary>
/// Jellyfin Personal Library : chaque utilisateur choisit ce qu'il voit dans les bibliothèques partagées,
/// type de contenu par type de contenu (« Tout », « Tout sauf… », « Uniquement… »).
/// Le masquage repose sur le filtre natif par étiquettes du profil utilisateur : il vaut pour tous les clients.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Segment de route de l'API du plugin.</summary>
    public const string RoutePrefix = "PersonalLibrary";

    /// <summary>Script de l'interface web, injecté dans jellyfin-web.</summary>
    public const string ScriptFileName = "jellyfin-personal-library.js";

    /// <summary>Nom de la ressource embarquée du script.</summary>
    public const string ScriptResource = "Jellyfin.Plugin.PersonalLibrary.jellyfin-personal-library.js";

    /// <summary>Empreinte du script, pour l'URL (?v=) : un nouveau script n'est jamais servi depuis le cache.</summary>
    public static string ScriptVersion { get; } = ComputeScriptVersion();

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Jellyfin Personal Library";

    public override Guid Id => Guid.Parse("87c7944b-ed92-4cdf-9e97-02b45c8b1057");

    public override string Description =>
        "Chaque utilisateur choisit ce qu'il voit dans les bibliothèques partagées. "
        + "Each user chooses what they see in shared libraries.";

    /// <summary>
    /// Désinstallation : sans nettoyage, les étiquettes resteraient dans les profils et un utilisateur en « Uniquement… »
    /// ne verrait plus rien. On retire tout avant que Jellyfin supprime le plugin.
    /// </summary>
    public override void OnUninstalling()
    {
        try
        {
            ApplyService.Current?.ResetAllAsync(CancellationToken.None).Wait(TimeSpan.FromMinutes(10));
        }
        catch (Exception)
        {
            // La désinstallation continue ; l'admin peut encore retirer les étiquettes à la main.
        }

        base.OnUninstalling();
    }

    private static string ComputeScriptVersion()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResource);
        return stream is null ? "0" : Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant();
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
