using System.Collections.Concurrent;
using Jellyfin.Plugin.PersonalLibrary.Selection;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PersonalLibrary.Engine;

/// <summary>État d'un utilisateur pour l'API.</summary>
public sealed class UserStatus
{
    public bool Pending { get; set; }

    public ApplyResult? Last { get; set; }
}

/// <summary>
/// File d'application en arrière-plan. Un choix de l'utilisateur est appliqué aussitôt (après 1 s) ;
/// un élément ajouté ou modifié ailleurs (scan, actualisation qui efface les étiquettes) relance une application
/// complète une fois le serveur calme. La tâche d'après scan applique aussi tout, en fin de scan.
/// </summary>
public sealed class ApplyService : IHostedService, IDisposable
{
    private static readonly TimeSpan UserDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QuietDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly TagApplier _applier;
    private readonly SelectionStore _store;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ApplyService> _logger;
    private readonly ConcurrentDictionary<Guid, UserStatus> _status = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _lock = new();
    private readonly HashSet<Guid> _pending = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTime _due = DateTime.MaxValue;
    private DateTime _firstRequest = DateTime.MaxValue;

    public ApplyService(TagApplier applier, SelectionStore store, ILibraryManager libraryManager, ILogger<ApplyService> logger)
    {
        _applier = applier;
        _store = store;
        _libraryManager = libraryManager;
        _logger = logger;
        Current = this;
    }

    /// <summary>Instance en service, pour le nettoyage à la désinstallation (le plugin n'a pas accès au conteneur).</summary>
    public static ApplyService? Current { get; private set; }

    public UserStatus Status(Guid userId) => _status.GetOrAdd(userId, _ => new UserStatus());

    /// <summary>Demande une application pour un utilisateur.</summary>
    public void Request(Guid userId) => Enqueue([userId], UserDelay);

    /// <summary>Applique tout de suite pour tous les utilisateurs concernés (tâche d'après scan, API admin).</summary>
    public async Task ApplyAllNowAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _pending.Clear();
            _due = DateTime.MaxValue;
            _firstRequest = DateTime.MaxValue;
        }

        await RunAsync(_store.KnownUsers(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Efface les choix de tous les utilisateurs, puis retire toutes les étiquettes du plugin (éléments et profils).</summary>
    public async Task ResetAllAsync(CancellationToken cancellationToken)
    {
        var users = _store.KnownUsers();
        foreach (var userId in users)
        {
            _store.Delete(userId);
        }

        await RunAsync(users, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Personal Library : choix de {Count} utilisateur(s) effacés, étiquettes retirées", users.Count);
    }

    public async Task<ApplyResult> ApplyNowAsync(Guid userId, CancellationToken cancellationToken)
    {
        await RunAsync([userId], cancellationToken).ConfigureAwait(false);
        return Status(userId).Last ?? new ApplyResult();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
        // Au démarrage : rattrape ce qui a pu changer pendant l'arrêt.
        Enqueue(_store.KnownUsers(), StartupDelay);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _signal.Dispose();
        _running.Dispose();
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is null || _applier.IsWriting(e.Item.Id))
        {
            return;
        }

        var users = _store.KnownUsers();
        if (users.Count > 0)
        {
            Enqueue(users, QuietDelay);
        }
    }

    private void Enqueue(IEnumerable<Guid> users, TimeSpan delay)
    {
        lock (_lock)
        {
            foreach (var u in users)
            {
                _pending.Add(u);
                Status(u).Pending = true;
            }

            if (_pending.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (_firstRequest == DateTime.MaxValue)
            {
                _firstRequest = now;
            }

            // Repoussé à chaque événement, sans dépasser MaxWait depuis la première demande.
            var due = now + delay;
            var cap = _firstRequest + MaxWait;
            _due = due < cap ? due : cap;
        }

        _signal.Release();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                TimeSpan wait;
                lock (_lock)
                {
                    wait = _due == DateTime.MaxValue ? TimeSpan.Zero : _due - DateTime.UtcNow;
                }

                if (wait <= TimeSpan.Zero)
                {
                    break;
                }

                // Une nouvelle demande peut avancer ou repousser l'échéance.
                await _signal.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }

            Guid[] users;
            lock (_lock)
            {
                users = [.. _pending];
                _pending.Clear();
                _due = DateTime.MaxValue;
                _firstRequest = DateTime.MaxValue;
            }

            if (users.Length > 0)
            {
                await RunAsync(users, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAsync(IEnumerable<Guid> users, CancellationToken cancellationToken)
    {
        await _running.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var userId in users)
            {
                var status = Status(userId);
                try
                {
                    status.Last = await _applier.ApplyAsync(userId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Personal Library : application impossible pour {User}", userId);
                    status.Last = new ApplyResult { AppliedUtc = DateTime.UtcNow, Error = ex.Message };
                }

                lock (_lock)
                {
                    status.Pending = _pending.Contains(userId);
                }
            }
        }
        finally
        {
            _running.Release();
        }
    }
}
