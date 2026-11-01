using System.Text.Json;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.PersonalLibrary.Selection;

/// <summary>
/// Fichiers JSON du plugin, hors du dossier versionné (rien n'est perdu à une mise à jour) :
/// <list type="bullet">
/// <item><c>&lt;plugins/configurations&gt;/PersonalLibrary/users/&lt;id&gt;.json</c> : choix de l'utilisateur ;</item>
/// <item><c>…/applied/&lt;id&gt;.json</c> : éléments étiquetés par le plugin pour cet utilisateur.</item>
/// </list>
/// </summary>
public sealed class SelectionStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _lock = new();

    public SelectionStore(IApplicationPaths applicationPaths)
    {
        DataDirectory = Path.Combine(applicationPaths.PluginConfigurationsPath, "PersonalLibrary");
    }

    public string DataDirectory { get; }

    private string UsersDirectory => Path.Combine(DataDirectory, "users");

    private string AppliedDirectory => Path.Combine(DataDirectory, "applied");

    public UserSelection Load(Guid userId)
        => Read<UserSelection>(Path.Combine(UsersDirectory, userId.ToString("N") + ".json")) ?? new UserSelection { UserId = userId };

    public void Save(UserSelection selection)
    {
        selection.UpdatedUtc = DateTime.UtcNow;
        Write(Path.Combine(UsersDirectory, selection.UserId.ToString("N") + ".json"), selection);
    }

    public void Delete(Guid userId)
    {
        lock (_lock)
        {
            DeleteIfExists(Path.Combine(UsersDirectory, userId.ToString("N") + ".json"));
        }
    }

    public AppliedState LoadApplied(Guid userId)
        => Read<AppliedState>(Path.Combine(AppliedDirectory, userId.ToString("N") + ".json")) ?? new AppliedState { UserId = userId };

    public void SaveApplied(AppliedState state)
    {
        var path = Path.Combine(AppliedDirectory, state.UserId.ToString("N") + ".json");
        if (state.Hidden.Count == 0 && state.Shown.Count == 0)
        {
            lock (_lock)
            {
                DeleteIfExists(path);
            }

            return;
        }

        Write(path, state);
    }

    /// <summary>Utilisateurs qui ont des choix ou des étiquettes posées.</summary>
    public IReadOnlyCollection<Guid> KnownUsers()
    {
        lock (_lock)
        {
            return new[] { UsersDirectory, AppliedDirectory }
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.json"))
                .Select(f => Guid.TryParse(Path.GetFileNameWithoutExtension(f), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private T? Read<T>(string path)
        where T : class
    {
        lock (_lock)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
    }

    private void Write<T>(string path, T value)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, true);
        }
    }
}
