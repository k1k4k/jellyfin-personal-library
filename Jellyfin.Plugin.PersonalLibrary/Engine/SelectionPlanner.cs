using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.PersonalLibrary.Selection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PersonalLibrary.Engine;

/// <summary>Éléments à étiqueter pour un utilisateur.</summary>
public sealed class Plan
{
    public static Plan Empty => new();

    /// <summary>Éléments à masquer (étiquette « hide », étiquettes bloquées du profil).</summary>
    public HashSet<Guid> Hide { get; } = [];

    /// <summary>Éléments à garder (étiquette « show ») ; non vide seulement si une bibliothèque est en « Uniquement ».</summary>
    public HashSet<Guid> Show { get; } = [];

    /// <summary>Au moins une bibliothèque en « Uniquement » : les étiquettes autorisées du profil sont actives.</summary>
    public bool UsesShow { get; set; }

    /// <summary>Types en « Uniquement » ramenés à « Tout » (restriction de l'admin par étiquettes autorisées).</summary>
    public HashSet<string> RefusedOnly { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Traduit les choix d'un utilisateur en éléments à étiqueter.
/// <para>
/// Jellyfin 10.11 ne regarde que les étiquettes de l'élément lui-même (et de sa série) : l'étiquette est donc posée sur
/// chaque élément concerné, conteneurs compris (dossier, album, série, saison), jamais sur un parent seulement.
/// En 12.x, les étiquettes des parents et de la bibliothèque comptent aussi : une bibliothèque entièrement gardée reçoit
/// une seule étiquette, sur la bibliothèque, au lieu d'une par élément.
/// </para>
/// <para>
/// Dès qu'une bibliothèque est en « Uniquement », Jellyfin cache tout ce qui n'a pas l'étiquette autorisée, sur tout le
/// serveur : les autres bibliothèques, les genres, les collections et les playlists de l'utilisateur sont donc gardés
/// explicitement.
/// </para>
/// </summary>
public sealed class SelectionPlanner
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SelectionPlanner> _logger;

    public SelectionPlanner(ILibraryManager libraryManager, ILogger<SelectionPlanner> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>Bibliothèques auxquelles l'utilisateur a accès.</summary>
    public IReadOnlyList<CollectionFolder> AccessibleLibraries(User user)
        => _libraryManager.GetUserRootFolder().GetChildren(user, true).OfType<CollectionFolder>().ToList();

    /// <summary>
    /// Tous les éléments d'une bibliothèque (récursif, conteneurs compris). Par ParentId, Jellyfin cherche par dossier
    /// racine (TopParentId), toujours renseigné ; la table des ancêtres, elle, peut manquer pour une partie des éléments.
    /// Les dossiers racines eux-mêmes sont exclus : leur étiquette vaudrait pour toute la bibliothèque.
    /// </summary>
    public IReadOnlyList<BaseItem> Members(CollectionFolder library)
    {
        var roots = library.PhysicalFolderIds.ToHashSet();
        return _libraryManager.GetItemList(new InternalItemsQuery { ParentId = library.Id, Recursive = true })
            .Where(i => !roots.Contains(i.Id))
            .ToList();
    }

    /// <param name="onlyAllowed">Faux si l'admin restreint déjà l'utilisateur par étiquettes autorisées : le mode « Uniquement » l'élargirait.</param>
    public Plan Build(User user, UserSelection selection, bool onlyAllowed)
    {
        var plan = new Plan();
        var libraries = AccessibleLibraries(user)
            .Select(lib =>
            {
                var type = ContentTypes.Of(lib);
                var s = selection.For(type);
                var mode = s.Mode;
                if (mode == ContentMode.Only && !onlyAllowed)
                {
                    plan.RefusedOnly.Add(type);
                    mode = ContentMode.All;
                }

                return (Library: lib, Mode: mode, s.Rules);
            })
            .ToList();
        plan.UsesShow = libraries.Any(l => l.Mode == ContentMode.Only);
        var onlyFolders = libraries.Where(l => l.Mode == ContentMode.Only).SelectMany(l => l.Library.PhysicalFolderIds).ToHashSet();

        var visible = new HashSet<Guid>();
        var musicGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var otherGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boxSetLibraries = new List<(CollectionFolder? Library, ContentMode Mode, IReadOnlyList<BaseItem> Members, HashSet<Guid> Picked)>();
        foreach (var (library, mode, rules) in libraries)
        {
            var members = Members(library);
            var memberIds = members.Select(m => m.Id).ToHashSet();
            var picked = Expand(rules);
            picked.IntersectWith(memberIds);

            if (library.CollectionType == CollectionType.boxsets)
            {
                // Collections : décidées après les autres bibliothèques (selon ce qu'elles contiennent de visible).
                boxSetLibraries.Add((library, mode, members, picked));
                continue;
            }

            HashSet<Guid> libVisible;
            switch (mode)
            {
                case ContentMode.Except:
                    var hidden = CloseUp(members, picked);
                    plan.Hide.UnionWith(hidden);
                    libVisible = memberIds;
                    libVisible.ExceptWith(hidden);
                    break;
                case ContentMode.Only:
#if JF10
                    // 10.11 : sans étiquette, un dossier parent est invisible et ne transmet rien ; on l'étiquette pour le parcourir.
                    libVisible = WithAncestors(members, picked);
#else
                    // 12.x : l'étiquette d'un dossier vaut pour tout son contenu ; étiqueter le dossier d'un album gardé
                    // montrerait tout l'artiste. Seuls les éléments choisis sont étiquetés (vues Albums / Artistes complètes).
                    libVisible = picked;
#endif
                    plan.Show.UnionWith(libVisible);
                    break;
                default:
                    libVisible = memberIds;
                    break;
            }

            if (plan.UsesShow && mode != ContentMode.Only)
            {
#if JF10
                plan.Show.UnionWith(libVisible);
#else
                // 12.x : l'étiquette de la bibliothèque vaut pour tout son contenu ; « hide » reste prioritaire. Jellyfin la
                // lit dans la table des ancêtres, parfois incomplète : on étiquette aussi ses dossiers racines (TopParentId,
                // toujours renseigné), sauf ceux partagés avec une bibliothèque en « Uniquement ».
                plan.Show.Add(library.Id);
                plan.Show.UnionWith(library.PhysicalFolderIds.Where(id => !onlyFolders.Contains(id)));
#endif
            }

            visible.UnionWith(libVisible);
            if (plan.UsesShow)
            {
                foreach (var m in members.Where(m => m.Genres.Length > 0 && libVisible.Contains(m.Id)))
                {
                    (m is Audio or MusicAlbum or MusicArtist ? musicGenres : otherGenres).UnionWith(m.Genres);
                }
            }
        }

        // Collections hors d'une bibliothèque Collections accessible (bibliothèque désactivée ou absente) : Jellyfin les montre
        // quand même, regroupées dans les films ; elles suivent le mode « Tout ».
        var planned = boxSetLibraries.SelectMany(l => l.Members).Select(m => m.Id).ToHashSet();
        var orphans = _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.BoxSet], Recursive = true })
            .Where(b => !planned.Contains(b.Id))
            .ToList();
        if (orphans.Count > 0)
        {
            boxSetLibraries.Add((null, ContentMode.All, orphans, []));
        }

        PlanBoxSets(plan, boxSetLibraries, visible);
        if (plan.UsesShow)
        {
            PlanPlaylists(plan, user);
            PlanGenres(plan, musicGenres, otherGenres);
        }

        return plan;
    }

    /// <summary>Éléments couverts par les règles : l'élément, tout ce qu'il contient, et pour un artiste ses albums.</summary>
    public HashSet<Guid> Expand(IEnumerable<Rule> rules)
    {
        var ids = new HashSet<Guid>();
        foreach (var rule in rules)
        {
            var item = _libraryManager.GetItemById(rule.ItemId);
            if (item is null)
            {
                continue;
            }

            AddWithChildren(ids, item);
            if (item is MusicArtist || rule.Kind == RuleKind.Artist)
            {
                // Ses albums rangés ailleurs (compilations, autre dossier), avec leurs pistes : 10.11 veut l'étiquette sur chacune.
                foreach (var album in _libraryManager.GetItemList(new InternalItemsQuery
                {
                    AlbumArtistIds = [item.Id],
                    IncludeItemTypes = [BaseItemKind.MusicAlbum, BaseItemKind.Audio]
                }))
                {
                    AddWithChildren(ids, album);
                }
            }
        }

        return ids;
    }

    private static void AddWithChildren(HashSet<Guid> ids, BaseItem item)
    {
        if (!ids.Add(item.Id))
        {
            return;
        }

        if (item is BoxSet boxSet)
        {
            foreach (var child in boxSet.GetLinkedChildren())
            {
                AddWithChildren(ids, child);
            }
        }
        else if (item is Folder folder)
        {
            foreach (var child in folder.GetRecursiveChildren())
            {
                ids.Add(child.Id);
            }
        }
    }

    /// <summary>« Tout sauf » : les éléments choisis, plus chaque conteneur dont tout le contenu est masqué (album, dossier, série vidés).</summary>
    private static HashSet<Guid> CloseUp(IReadOnlyList<BaseItem> members, HashSet<Guid> picked)
    {
        var children = members.GroupBy(m => m.ParentId).ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());
        var memo = new Dictionary<Guid, bool>();
        bool IsHidden(Guid id)
        {
            if (memo.TryGetValue(id, out var known))
            {
                return known;
            }

            memo[id] = false; // garde-fou contre un cycle
            var result = picked.Contains(id)
                || (children.TryGetValue(id, out var kids) && kids.Count > 0 && kids.All(IsHidden));
            memo[id] = result;
            return result;
        }

        return members.Select(m => m.Id).Where(IsHidden).ToHashSet();
    }

    /// <summary>« Uniquement » (10.11) : les éléments choisis et leurs conteneurs dans la bibliothèque (pour les parcourir).</summary>
    private static HashSet<Guid> WithAncestors(IReadOnlyList<BaseItem> members, HashSet<Guid> picked)
    {
        var parent = members.ToDictionary(m => m.Id, m => m.ParentId);
        var result = new HashSet<Guid>();
        foreach (var id in picked)
        {
            var current = id;
            while (parent.ContainsKey(current) && result.Add(current))
            {
                current = parent[current];
            }
        }

        return result;
    }

    /// <summary>
    /// Collections : une collection dont aucun élément n'est visible est masquée (« Tout sauf ») ou non gardée
    /// (« Uniquement ») ; sinon elle suit le mode de la bibliothèque Collections et ses règles.
    /// </summary>
    private void PlanBoxSets(Plan plan, List<(CollectionFolder? Library, ContentMode Mode, IReadOnlyList<BaseItem> Members, HashSet<Guid> Picked)> libraries, HashSet<Guid> visible)
    {
        foreach (var (_, mode, members, picked) in libraries)
        {
            foreach (var boxSet in members.OfType<BoxSet>())
            {
                var linked = boxSet.GetLinkedChildren();
                var empty = linked.Count > 0 && !linked.Any(c => visible.Contains(c.Id));
                var keep = mode switch
                {
                    ContentMode.Except => !picked.Contains(boxSet.Id) && !empty,
                    ContentMode.Only => picked.Contains(boxSet.Id) && !empty,
                    _ => !empty
                };
                if (keep)
                {
                    visible.Add(boxSet.Id);
                    if (plan.UsesShow)
                    {
                        plan.Show.Add(boxSet.Id);
                    }
                }
                else if (!plan.UsesShow || mode == ContentMode.Except)
                {
                    plan.Hide.Add(boxSet.Id);
                }
            }
        }
    }

    /// <summary>Playlists dont l'utilisateur est propriétaire, qui lui sont partagées ou ouvertes : gardées (leur contenu reste filtré).</summary>
    private void PlanPlaylists(Plan plan, User user)
    {
        foreach (var playlist in _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Playlist] }).OfType<Playlist>())
        {
            if (playlist.OwnerUserId.Equals(user.Id) || playlist.OpenAccess || playlist.Shares.Any(s => s.UserId.Equals(user.Id)))
            {
                plan.Show.Add(playlist.Id);
            }
        }
    }

    /// <summary>Genres qui ont au moins un élément visible : gardés, sinon la liste des genres serait vide.</summary>
    private void PlanGenres(Plan plan, HashSet<string> music, HashSet<string> other)
    {
        foreach (var name in music)
        {
            TryAdd(plan, () => _libraryManager.GetMusicGenre(name), name);
        }

        foreach (var name in other)
        {
            TryAdd(plan, () => _libraryManager.GetGenre(name), name);
        }
    }

    private void TryAdd(Plan plan, Func<BaseItem> lookup, string name)
    {
        try
        {
            plan.Show.Add(lookup().Id);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Personal Library : genre {Name} introuvable", name);
        }
    }
}
