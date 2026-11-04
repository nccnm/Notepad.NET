using System.Text.Json;
using System.Text.Json.Serialization;
using NotepadNet.Core.IO;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Autosave;

/// <summary>
/// What the original writes into the registry value <c>Autosave\&lt;GUID&gt;</c>,
/// in the port's own versioned JSON file instead of
/// <c>"%i,%s,%s"</c> (deliberate difference, parity.md: paths with commas no longer break it).
/// </summary>
/// <param name="Version">Format version; a reader refuses versions it does not know.</param>
/// <param name="Encoding">Encoding of the document, restored so Save
/// writes the file the way it was.</param>
/// <param name="DocumentPath">Full path of the document, or null while untitled
/// (<c>*Untitled*</c> in the original).</param>
/// <param name="HasText">The document was dirty: its text is in the session's text file.</param>
/// <param name="CurrentDirectory">Process current directory, set again before the restore.</param>
/// <param name="SelectionStart">Selection of dirty text, set again after the restore. A manifest
/// written before this field existed reads as 0.</param>
/// <param name="SelectionEnd">End of that selection.</param>
/// <param name="FileStamp">The document's file as it knew it, for dirty text with a path, so that a
/// change made while the app is closed is noticed. Null in a manifest written before this field existed.</param>
public sealed record AutosaveManifest(
    int Version, TextEncodingKind Encoding, string? DocumentPath, bool HasText, string? CurrentDirectory, int SelectionStart = 0, int SelectionEnd = 0,
    FileStamp? FileStamp = null)
{
    public const int CurrentVersion = 1;

    public string ToJson() => JsonSerializer.Serialize(this, AutosaveJsonContext.Default.AutosaveManifest);

    /// <summary>The manifest, or null when <paramref name="json"/> is not one this version can
    /// restore (bad JSON, unknown version or encoding, empty path).</summary>
    public static AutosaveManifest? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        AutosaveManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, AutosaveJsonContext.Default.AutosaveManifest);
        }
        catch (JsonException)
        {
            return null;
        }

        return manifest is not null
            && manifest.Version == CurrentVersion
            && Enum.IsDefined(manifest.Encoding)
            && (manifest.DocumentPath is null || manifest.DocumentPath.Length > 0)
            ? manifest
            : null;
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AutosaveManifest))]
internal sealed partial class AutosaveJsonContext : JsonSerializerContext;
