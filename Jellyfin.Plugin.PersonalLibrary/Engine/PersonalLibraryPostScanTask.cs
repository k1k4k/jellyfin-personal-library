using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.PersonalLibrary.Engine;

/// <summary>Après chaque scan : étiquette les nouveautés couvertes par les règles, et repose les étiquettes effacées.</summary>
public sealed class PersonalLibraryPostScanTask : ILibraryPostScanTask
{
    private readonly ApplyService _service;

    public PersonalLibraryPostScanTask(ApplyService service)
    {
        _service = service;
    }

    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _service.ApplyAllNowAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}
