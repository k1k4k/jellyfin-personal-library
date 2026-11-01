namespace Jellyfin.Plugin.PersonalLibrary.Engine;

/// <summary>
/// Étiquettes techniques du plugin, une paire par utilisateur. Elles n'ont pas de sens pour un humain
/// (elles apparaissent dans l'éditeur de métadonnées) et ne contiennent que des minuscules ASCII :
/// Jellyfin les compare après nettoyage (minuscules, sans accents).
/// </summary>
public static class TagNames
{
    private const string Prefix = "pl-";

    /// <summary>Posée sur ce que l'utilisateur masque (« Tout sauf ») ; ajoutée à ses étiquettes bloquées.</summary>
    public static string Hide(Guid userId) => $"{Prefix}{Short(userId)}-hide";

    /// <summary>Posée sur ce que l'utilisateur garde ; ajoutée à ses étiquettes autorisées dès qu'une bibliothèque est en « Uniquement ».</summary>
    public static string Show(Guid userId) => $"{Prefix}{Short(userId)}-show";

    /// <summary>Étiquette posée par ce plugin (pour n'importe quel utilisateur).</summary>
    public static bool IsOurs(string? tag)
        => tag is not null
            && tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && (tag.EndsWith("-hide", StringComparison.OrdinalIgnoreCase) || tag.EndsWith("-show", StringComparison.OrdinalIgnoreCase));

    private static string Short(Guid userId) => userId.ToString("N")[..12];
}
