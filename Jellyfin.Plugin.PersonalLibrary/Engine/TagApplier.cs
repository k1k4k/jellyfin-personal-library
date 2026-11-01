using System.Collections.Concurrent;
using Jellyfin.Plugin.PersonalLibrary.Selection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PersonalLibrary.Engine;

/// <summary>Bilan d'une application pour un utilisateur.</summary>
public sealed class ApplyResult
{
    public int Hidden { get; set; }

    public int Shown { get; set; }

    public int Written { get; set; }

    public List<string> RefusedOnly { get; set; } = [];

    public DateTime AppliedUtc { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// Pose et retire les étiquettes d'un utilisateur, et met à jour ses étiquettes bloquées / autorisées.
/// Ordre choisi pour ne jamais montrer trop : ajouts d'étiquettes, puis profil, puis retraits.
/// Les éléments sont enregistrés sans enregistreur de métadonnées (<see cref="ItemUpdateType.None"/>) : pas d'écriture de NFO.
/// </summary>
public sealed class TagApplier
{
    private const int BatchSize = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly SelectionPlanner _planner;
    private readonly SelectionStore _store;
    private readonly ILogger<TagApplier> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _writing = new();

    public TagApplier(ILibraryManager libraryManager, IUserManager userManager, SelectionPlanner planner, SelectionStore store, ILogger<TagApplier> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _planner = planner;
        _store = store;
        _logger = logger;
    }

    /// <summary>Élément en cours d'écriture par le plugin (ses événements de mise à jour sont ignorés).</summary>
    public bool IsWriting(Guid itemId) => _writing.ContainsKey(itemId);

    /// <summary>Étiquettes autorisées posées par l'admin (hors plugin) : si non vide, le mode « Uniquement » est refusé.</summary>
    public IReadOnlyList<string> ForeignAllowedTags(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        return user is null ? [] : _userManager.GetUserDto(user).Policy.AllowedTags.Where(t => !TagNames.IsOurs(t)).ToList();
    }

    public async Task<ApplyResult> ApplyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var hideTag = TagNames.Hide(userId);
        var showTag = TagNames.Show(userId);
        var previous = _store.LoadApplied(userId);
        var user = _userManager.GetUserById(userId);

        Plan plan;
        UserPolicy? policy = null;
        if (user is null)
        {
            // Utilisateur supprimé : on retire tout ce qui a été posé pour lui.
            plan = Plan.Empty;
            _store.Delete(userId);
        }
        else
        {
            var selection = _store.Load(userId);
            policy = _userManager.GetUserDto(user).Policy;
            var onlyAllowed = !policy.AllowedTags.Any(t => !TagNames.IsOurs(t));
            plan = selection.IsEmpty ? Plan.Empty : _planner.Build(user, selection, onlyAllowed);
        }

        var currentHide = previous.Hidden.Concat(Holders(hideTag)).ToHashSet();
        var currentShow = previous.Shown.Concat(Holders(showTag)).ToHashSet();

        var written = 0;
        // Ajouts comparés aux étiquettes réelles : une actualisation (« Remplacer toutes les métadonnées ») a pu les effacer.
        written += await EditAsync(plan.Hide, hideTag, add: true, cancellationToken).ConfigureAwait(false);
        written += await EditAsync(plan.Show, showTag, add: true, cancellationToken).ConfigureAwait(false);

        if (user is not null && policy is not null)
        {
            await UpdatePolicyAsync(user.Id, policy, plan.Hide.Count > 0 ? hideTag : null, plan.UsesShow ? showTag : null).ConfigureAwait(false);
        }

        written += await EditAsync(currentHide.Except(plan.Hide), hideTag, add: false, cancellationToken).ConfigureAwait(false);
        written += await EditAsync(currentShow.Except(plan.Show), showTag, add: false, cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        _store.SaveApplied(new AppliedState { UserId = userId, Hidden = [.. plan.Hide], Shown = [.. plan.Show], AppliedUtc = now });
        if (written > 0)
        {
            _logger.LogInformation(
                "Personal Library : {User} — {Hidden} masqués, {Shown} gardés, {Written} éléments modifiés",
                user?.Username ?? userId.ToString("N"),
                plan.Hide.Count,
                plan.Show.Count,
                written);
        }

        return new ApplyResult { Hidden = plan.Hide.Count, Shown = plan.Show.Count, Written = written, RefusedOnly = [.. plan.RefusedOnly], AppliedUtc = now };
    }

    /// <summary>Éléments qui portent l'étiquette en base (rattrape un état enregistré perdu ou incomplet).</summary>
    private IEnumerable<Guid> Holders(string tag)
        => _libraryManager.GetItemList(new InternalItemsQuery { Tags = [tag] }).Select(i => i.Id);

    private async Task UpdatePolicyAsync(Guid userId, UserPolicy policy, string? blocked, string? allowed)
    {
        var newBlocked = policy.BlockedTags.Where(t => !TagNames.IsOurs(t)).ToList();
        var newAllowed = policy.AllowedTags.Where(t => !TagNames.IsOurs(t)).ToList();
        if (blocked is not null)
        {
            newBlocked.Add(blocked);
        }

        if (allowed is not null)
        {
            newAllowed.Add(allowed);
        }

        if (SameSet(newBlocked, policy.BlockedTags) && SameSet(newAllowed, policy.AllowedTags))
        {
            return;
        }

        policy.BlockedTags = [.. newBlocked];
        policy.AllowedTags = [.. newAllowed];
        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);
    }

    private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b)
        => a.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b);

    private async Task<int> EditAsync(IEnumerable<Guid> ids, string tag, bool add, CancellationToken cancellationToken)
    {
        var changed = new List<BaseItem>();
        foreach (var id in ids)
        {
            var item = _libraryManager.GetItemById(id);
            if (item is null)
            {
                continue;
            }

            var has = item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
            if (has == add)
            {
                continue;
            }

            item.Tags = add
                ? [.. item.Tags, tag]
                : item.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToArray();
            changed.Add(item);
        }

        // Par parent : l'API de mise à jour groupée de Jellyfin attend le parent commun.
        foreach (var group in changed.GroupBy(i => i.ParentId))
        {
            // Genres, bibliothèques… n'ont pas de parent.
            var parent = (group.Key.Equals(Guid.Empty) ? null : _libraryManager.GetItemById(group.Key)) ?? _libraryManager.RootFolder;
            foreach (var batch in group.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var i in batch)
                {
                    _writing[i.Id] = 0;
                }

                try
                {
                    await _libraryManager.UpdateItemsAsync(batch, parent, ItemUpdateType.None, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    foreach (var i in batch)
                    {
                        _writing.TryRemove(i.Id, out _);
                    }
                }
            }
        }

        return changed.Count;
    }
}
