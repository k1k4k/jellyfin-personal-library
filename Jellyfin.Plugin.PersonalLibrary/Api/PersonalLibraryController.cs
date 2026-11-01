using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.PersonalLibrary.Engine;
using Jellyfin.Plugin.PersonalLibrary.Selection;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PersonalLibrary.Api;

/// <summary>Corps de <c>PUT /PersonalLibrary/Me/Types/{type}</c>.</summary>
public sealed class ModeRequest
{
    [Required]
    public ContentMode Mode { get; set; }
}

/// <summary>Corps de <c>POST /PersonalLibrary/Me/Types/{type}/Rules</c>.</summary>
public sealed class RuleRequest
{
    [Required]
    public Guid ItemId { get; set; }
}

/// <summary>
/// API du plugin. Les routes <c>Me</c> agissent sur l'utilisateur connecté ; un administrateur peut viser un autre
/// utilisateur avec <c>?userId=</c>. Chaque modification est enregistrée puis appliquée en arrière-plan.
/// </summary>
[ApiController]
[Route(Plugin.RoutePrefix)]
[Authorize]
public class PersonalLibraryController : ControllerBase
{
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly SelectionStore _store;
    private readonly SelectionPlanner _planner;
    private readonly TagApplier _applier;
    private readonly ApplyService _service;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;

    public PersonalLibraryController(
        SelectionStore store,
        SelectionPlanner planner,
        TagApplier applier,
        ApplyService service,
        IUserManager userManager,
        ILibraryManager libraryManager)
    {
        _store = store;
        _planner = planner;
        _applier = applier;
        _service = service;
        _userManager = userManager;
        _libraryManager = libraryManager;
    }

    /// <summary>GET /PersonalLibrary/jellyfin-personal-library.js. Anonyme : chargé par index.html avant toute connexion.</summary>
    [HttpGet(Plugin.ScriptFileName)]
    [AllowAnonymous]
    public ActionResult GetScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Plugin.ScriptResource);
        if (stream is null)
        {
            return NotFound();
        }

        // L'URL porte l'empreinte du script (?v=) : on peut cacher longtemps.
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return new FileStreamResult(stream, "text/javascript; charset=utf-8");
    }

    /// <summary>
    /// GET /PersonalLibrary/Me/Items/{itemId} : pour le menu d'un élément, son type de contenu, le mode de ce type
    /// et si l'élément est lui-même une règle.
    /// </summary>
    [HttpGet("Me/Items/{itemId}")]
    public ActionResult<object> GetItemState([FromRoute] Guid itemId)
    {
        if (CurrentUser() is not { } user)
        {
            return Forbid();
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var libraries = _planner.AccessibleLibraries(user);
        var folders = _libraryManager.GetCollectionFolders(item).Select(f => f.Id).ToHashSet();
        var type = libraries.Where(l => folders.Contains(l.Id)).Select(ContentTypes.Of).FirstOrDefault()
            ?? (item is MusicArtist && libraries.Any(l => l.CollectionType == CollectionType.music) ? nameof(CollectionType.music) : null);
        if (type is null)
        {
            return new { type = (string?)null, mode = ContentMode.All, selected = false, canEdit = false };
        }

        var s = _store.Load(user.Id).For(type);
        return new { type, mode = s.Mode, selected = s.Rules.Any(r => r.ItemId.Equals(item.Id)), canEdit = CanEdit(user) };
    }

    /// <summary>
    /// GET /PersonalLibrary/Me : types de contenu accessibles (avec leurs bibliothèques), mode et règles de chacun,
    /// état de l'application.
    /// </summary>
    [HttpGet("Me")]
    public ActionResult<object> GetMe([FromQuery] Guid? userId)
    {
        if (Target(userId) is not { } user)
        {
            return Forbid();
        }

        var selection = _store.Load(user.Id);
        var status = _service.Status(user.Id);
        return new
        {
            userId = user.Id,
            userName = user.Username,
            canEdit = CanEdit(user),
            onlyAllowed = _applier.ForeignAllowedTags(user.Id).Count == 0,
            pending = status.Pending,
            last = status.Last,
            types = _planner.AccessibleLibraries(user)
                .GroupBy(ContentTypes.Of, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var s = selection.For(g.Key);
                    return new
                    {
                        type = g.Key,
                        libraries = g.Select(lib => new { id = lib.Id, name = lib.Name }),
                        mode = s.Mode,
                        rules = s.Rules.Select(r => new
                        {
                            r.Kind,
                            r.ItemId,
                            name = _libraryManager.GetItemById(r.ItemId)?.Name ?? r.Name,
                            r.Type,
                            exists = _libraryManager.GetItemById(r.ItemId) is not null
                        })
                    };
                })
        };
    }

    /// <summary>PUT /PersonalLibrary/Me/Types/{type} : change le mode d'un type de contenu (les règles sont gardées).</summary>
    [HttpPut("Me/Types/{type}")]
    public ActionResult SetMode([FromRoute] string type, [FromBody] ModeRequest request, [FromQuery] Guid? userId)
    {
        if (Editable(userId) is not { } user)
        {
            return Forbid();
        }

        if (TypeKey(user, type) is not { } key)
        {
            return NotFound();
        }

        if (request.Mode == ContentMode.Only && _applier.ForeignAllowedTags(user.Id).Count > 0)
        {
            return Conflict(new { error = "OnlyRefused", message = "Le profil est déjà restreint par des étiquettes autorisées : le mode « Uniquement » l'élargirait." });
        }

        var selection = _store.Load(user.Id);
        selection.GetOrAdd(key).Mode = request.Mode;
        Save(selection);
        return NoContent();
    }

    /// <summary>POST /PersonalLibrary/Me/Types/{type}/Rules : ajoute un élément (album, artiste, dossier, film, série, collection…).</summary>
    [HttpPost("Me/Types/{type}/Rules")]
    public ActionResult AddRule([FromRoute] string type, [FromBody] RuleRequest request, [FromQuery] Guid? userId)
    {
        if (Editable(userId) is not { } user)
        {
            return Forbid();
        }

        var key = TypeKey(user, type);
        var item = _libraryManager.GetItemById(request.ItemId);
        if (key is null || item is null)
        {
            return NotFound();
        }

        var libraries = _planner.AccessibleLibraries(user).Where(l => ContentTypes.Of(l).Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        var inType = (!libraries.Any(l => l.Id.Equals(item.Id))
                && _libraryManager.GetCollectionFolders(item).Any(f => libraries.Any(l => l.Id.Equals(f.Id))))
            || (item is MusicArtist && key == nameof(CollectionType.music));
        if (!inType)
        {
            return BadRequest(new { error = "NotInType" });
        }

        var selection = _store.Load(user.Id);
        var s = selection.GetOrAdd(key);
        if (!s.Rules.Any(r => r.ItemId.Equals(item.Id)))
        {
            s.Rules.Add(new Rule
            {
                Kind = item is MusicArtist ? RuleKind.Artist : RuleKind.Item,
                ItemId = item.Id,
                Name = item.Name,
                Type = item.GetBaseItemKind().ToString()
            });
            Save(selection);
        }

        return NoContent();
    }

    /// <summary>DELETE /PersonalLibrary/Me/Types/{type}/Rules/{itemId} : retire un élément.</summary>
    [HttpDelete("Me/Types/{type}/Rules/{itemId}")]
    public ActionResult RemoveRule([FromRoute] string type, [FromRoute] Guid itemId, [FromQuery] Guid? userId)
    {
        if (Editable(userId) is not { } user)
        {
            return Forbid();
        }

        var selection = _store.Load(user.Id);
        if (selection.Types.TryGetValue(TypeKey(user, type) ?? type, out var s) && s.Rules.RemoveAll(r => r.ItemId.Equals(itemId)) > 0)
        {
            Save(selection);
        }

        return NoContent();
    }

    /// <summary>DELETE /PersonalLibrary/Me : retour à « tout voir » (choix effacés, étiquettes retirées).</summary>
    [HttpDelete("Me")]
    public ActionResult Reset([FromQuery] Guid? userId)
    {
        if (Editable(userId) is not { } user)
        {
            return Forbid();
        }

        _store.Delete(user.Id);
        _service.Request(user.Id);
        return NoContent();
    }

    /// <summary>POST /PersonalLibrary/Me/Apply : applique tout de suite et renvoie le bilan.</summary>
    [HttpPost("Me/Apply")]
    public async Task<ActionResult<ApplyResult>> ApplyMe([FromQuery] Guid? userId, CancellationToken cancellationToken)
    {
        if (Target(userId) is not { } user)
        {
            return Forbid();
        }

        return await _service.ApplyNowAsync(user.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GET /PersonalLibrary/Me/Browse : contenu d'une bibliothèque tel que l'utilisateur le verrait sans ses propres choix
    /// (les restrictions de l'admin restent appliquées), avec l'état de chaque élément, pour choisir quoi garder ou masquer.
    /// </summary>
    [HttpGet("Me/Browse")]
    public ActionResult<object> Browse(
        [FromQuery, Required] Guid libraryId,
        [FromQuery] Guid? parentId,
        [FromQuery] string? searchTerm,
        [FromQuery] string? includeItemTypes,
        [FromQuery] int startIndex = 0,
        [FromQuery] int limit = 100,
        [FromQuery] Guid? userId = null)
    {
        if (Target(userId) is not { } user)
        {
            return Forbid();
        }

        var library = Library(user, libraryId);
        if (library is null)
        {
            return NotFound();
        }

        var types = (includeItemTypes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => Enum.TryParse<BaseItemKind>(t, true, out var k) ? k : (BaseItemKind?)null)
            .OfType<BaseItemKind>()
            .ToArray();
        var flat = types.Length > 0 || !string.IsNullOrWhiteSpace(searchTerm);
        var query = new InternalItemsQuery(user)
        {
            SearchTerm = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm,
            IncludeItemTypes = types,
            StartIndex = Math.Max(0, startIndex),
            Limit = Math.Clamp(limit, 1, 500),
            OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)]
        };
        if (flat)
        {
            query.AncestorIds = [library.Id];
            query.Recursive = true;
        }
        else
        {
            // Enfants directs, filtrés en base avec les restrictions de l'admin (et pas avec les étiquettes du plugin) :
            // la liste native d'un dossier applique la visibilité complète de l'utilisateur.
            if (_libraryManager.GetItemById(parentId ?? library.Id) is not Folder parent
                || (!parent.Id.Equals(library.Id) && !_libraryManager.GetCollectionFolders(parent).Any(f => f.Id.Equals(library.Id))))
            {
                return NotFound();
            }

            var children = parent is BoxSet boxSet ? boxSet.GetLinkedChildren() : parent.Children.ToList();
            query.ItemIds = children.Select(c => c.Id).ToArray();
            if (query.ItemIds.Length == 0)
            {
                return new { mode = _store.Load(user.Id).For(ContentTypes.Of(library)).Mode, totalRecordCount = 0, items = Array.Empty<object>() };
            }
        }

        // Ses propres choix ne doivent pas cacher ce qu'il peut choisir ; les étiquettes de l'admin, si.
        query.ExcludeInheritedTags = query.ExcludeInheritedTags.Where(t => !TagNames.IsOurs(t)).ToArray();
        query.IncludeInheritedTags = query.IncludeInheritedTags.Where(t => !TagNames.IsOurs(t)).ToArray();

        var result = _libraryManager.GetItemsResult(query);
        var selection = _store.Load(user.Id).For(ContentTypes.Of(library));
        var ruleIds = selection.Rules.Select(r => r.ItemId).ToHashSet();
        var hideTag = TagNames.Hide(user.Id);
        var showTag = TagNames.Show(user.Id);
        return new
        {
            mode = selection.Mode,
            totalRecordCount = result.TotalRecordCount,
            items = result.Items.Select(i => new
            {
                id = i.Id,
                name = i.Name,
                type = i.GetBaseItemKind().ToString(),
                isFolder = i.IsFolder,
                productionYear = i.ProductionYear,
                albumArtist = (i as MusicAlbum)?.AlbumArtists.FirstOrDefault(),
                selected = ruleIds.Contains(i.Id),
                hidden = i.Tags.Contains(hideTag, StringComparer.OrdinalIgnoreCase),
                shown = i.Tags.Contains(showTag, StringComparer.OrdinalIgnoreCase)
            })
        };
    }

    /// <summary>GET /PersonalLibrary/Users : tous les utilisateurs qui ont une bibliothèque personnelle (admin).</summary>
    [HttpGet("Users")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<object> GetUsers()
    {
        return _store.KnownUsers().Select(id =>
        {
            var user = _userManager.GetUserById(id);
            var selection = _store.Load(id);
            var status = _service.Status(id);
            return new
            {
                userId = id,
                userName = user?.Username,
                types = selection.Types
                    .Where(t => t.Value.Mode != ContentMode.All)
                    .Select(t => new { type = t.Key, mode = t.Value.Mode, rules = t.Value.Rules.Count }),
                pending = status.Pending,
                last = status.Last
            };
        }).ToList();
    }

    /// <summary>DELETE /PersonalLibrary/Users : efface les choix de tout le monde et retire toutes les étiquettes (admin).</summary>
    [HttpDelete("Users")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult> ResetAll(CancellationToken cancellationToken)
    {
        await _service.ResetAllAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>POST /PersonalLibrary/Apply : réapplique tout, pour tous (admin).</summary>
    [HttpPost("Apply")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult> ApplyAll(CancellationToken cancellationToken)
    {
        await _service.ApplyAllNowAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>GET /PersonalLibrary/Status : version et cible de compilation (admin).</summary>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<object> GetStatus()
    {
        return new
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
#if JF10
            target = "jf10",
#else
            target = "jf12",
#endif
            configuration = Plugin.Instance?.Configuration
        };
    }

    private void Save(UserSelection selection)
    {
        _store.Save(selection);
        _service.Request(selection.UserId);
    }

    private User? CurrentUser()
    {
        var value = User.FindFirstValue(UserIdClaim);
        return Guid.TryParse(value, out var id) ? _userManager.GetUserById(id) : null;
    }

    private bool IsAdmin() => User.IsInRole("Administrator");

    /// <summary>Utilisateur visé : soi-même, ou un autre pour un administrateur.</summary>
    private User? Target(Guid? userId)
    {
        var me = CurrentUser();
        if (me is null || userId is null || userId.Value.Equals(Guid.Empty) || userId.Value.Equals(me.Id))
        {
            return me;
        }

        return IsAdmin() ? _userManager.GetUserById(userId.Value) : null;
    }

    /// <summary>Utilisateur visé, s'il peut être modifié par l'appelant.</summary>
    private User? Editable(Guid? userId)
    {
        var user = Target(userId);
        return user is not null && (IsAdmin() || CanEdit(user)) ? user : null;
    }

    private static bool CanEdit(User user)
    {
        var config = Plugin.Instance?.Configuration;
        return config is not null && config.EnableSelfService && !config.DisabledUserIds.Contains(user.Id);
    }

    /// <summary>Type de contenu tel qu'enregistré (casse des bibliothèques), s'il correspond à une bibliothèque accessible.</summary>
    private string? TypeKey(User user, string type)
        => _planner.AccessibleLibraries(user).Select(ContentTypes.Of).FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));

    private CollectionFolder? Library(User user, Guid libraryId)
        => _planner.AccessibleLibraries(user).FirstOrDefault(l => l.Id.Equals(libraryId));
}
