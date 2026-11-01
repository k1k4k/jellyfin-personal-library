using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PersonalLibrary.Selection;

/// <summary>Ce que l'utilisateur voit d'un type de contenu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContentMode
{
    /// <summary>Tout (comportement natif).</summary>
    All,

    /// <summary>Tout sauf les éléments des règles.</summary>
    Except,

    /// <summary>Uniquement les éléments des règles.</summary>
    Only
}

/// <summary>Portée d'une règle.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleKind
{
    /// <summary>Un élément et tout ce qu'il contient (album, dossier, film, série, collection…).</summary>
    Item,

    /// <summary>Un artiste : les albums dont il est artiste de l'album, et leurs pistes.</summary>
    Artist
}

/// <summary>Une règle : un élément choisi dans une bibliothèque de ce type. Les nouveautés qu'elle couvre suivent toutes seules.</summary>
public sealed class Rule
{
    public RuleKind Kind { get; set; }

    public Guid ItemId { get; set; }

    /// <summary>Nom au moment du choix, pour l'affichage si l'élément a disparu.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type Jellyfin au moment du choix (MusicAlbum, Series…).</summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>Réglage d'un type de contenu (toutes les bibliothèques de ce type).</summary>
public sealed class ContentSelection
{
    public ContentMode Mode { get; set; } = ContentMode.All;

    public List<Rule> Rules { get; set; } = [];
}

/// <summary>Type de contenu d'une bibliothèque : son type Jellyfin, ou « mixed » pour une bibliothèque sans type.</summary>
public static class ContentTypes
{
    public const string Mixed = "mixed";

    public static string Of(MediaBrowser.Controller.Entities.CollectionFolder library)
        => library.CollectionType?.ToString() ?? Mixed;
}

/// <summary>Bibliothèque personnelle d'un utilisateur. Absente ou vide : tout est visible.</summary>
public sealed class UserSelection
{
    public Guid UserId { get; set; }

    /// <summary>Par type de contenu (<see cref="ContentTypes.Of"/> : music, movies, tvshows, boxsets, mixed…).</summary>
    public Dictionary<string, ContentSelection> Types { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime UpdatedUtc { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Types.Values.All(l => l.Mode == ContentMode.All);

    public ContentSelection For(string type)
        => Types.TryGetValue(type, out var s) ? s : new ContentSelection();

    public ContentSelection GetOrAdd(string type)
        => Types.TryGetValue(type, out var s) ? s : Types[type] = new ContentSelection();
}

/// <summary>Ce que le plugin a posé pour un utilisateur, pour retirer exactement ses étiquettes.</summary>
public sealed class AppliedState
{
    public Guid UserId { get; set; }

    public List<Guid> Hidden { get; set; } = [];

    public List<Guid> Shown { get; set; } = [];

    public DateTime AppliedUtc { get; set; }
}
