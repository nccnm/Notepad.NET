using NotepadNet.Core.Settings;

namespace NotepadNet.Core.Documents;

/// <summary>Where the list of recent files lives between runs and between windows.</summary>
public interface IRecentFilesStore
{
    /// <summary>The list as stored now, most recent first.</summary>
    IReadOnlyList<string> Load();

    void Save(IReadOnlyList<string> paths);
}

/// <summary>A list that lives only for the process.</summary>
public sealed class InMemoryRecentFilesStore : IRecentFilesStore
{
    private IReadOnlyList<string> _paths = [];

    public IReadOnlyList<string> Load() => _paths;

    public void Save(IReadOnlyList<string> paths) => _paths = paths;
}

/// <summary>
/// The list as one <c>REG_SZ</c> in the user key. One value is written in one call, so a window
/// reading it never sees a half-written list. Written on every change rather than on exit like the
/// other settings: each window is its own process and a save on exit would drop what the others added.
/// </summary>
public sealed class RegistryRecentFilesStore(ISettingsRegistry registry) : IRecentFilesStore
{
    private readonly ISettingsRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public IReadOnlyList<string> Load()
    {
        using var key = _registry.Open(SettingsLocation.User);
        return key is not null && key.TryReadString(SettingsSchema.RecentFiles, RecentFilesPolicy.StoredCapacity, out var raw)
            ? RecentFilesPolicy.Parse(RegistrySettingsStore.TrimRegistryString(raw, RecentFilesPolicy.StoredCapacity))
            : [];
    }

    public void Save(IReadOnlyList<string> paths)
    {
        using var key = _registry.CreateUser();
        key?.WriteString(SettingsSchema.RecentFiles, RecentFilesPolicy.Format(paths));
    }
}

/// <summary>The rules of the list: its order and size, its stored form and its menu text.</summary>
public static class RecentFilesPolicy
{
    public const int MaxCount = 10;

    /// <summary>A longer path is not kept; it would make every read of the list allocate far more.</summary>
    public const int MaxPathLength = 4095;

    /// <summary>Longest menu text of a path, without its number.</summary>
    public const int MaxDisplayLength = 72;

    /// <summary>WCHARs of the stored value: every path, its separator or the NUL.</summary>
    public const int StoredCapacity = MaxCount * (MaxPathLength + 1);

    /// <summary>Between paths in the stored value; Windows does not allow it in a file name.</summary>
    private const char Separator = '|';

    private const string Ellipsis = "...";

    /// <summary><paramref name="path"/> first, without an older entry for it (names on Windows ignore
    /// case), and the oldest entry dropped past <see cref="MaxCount"/>.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> paths, string path)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(path);
        return IsStorable(path) ? [.. Without(paths, path).Prepend(path).Take(MaxCount)] : paths;
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> paths, string path)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(path);
        return [.. Without(paths, path)];
    }

    /// <summary>The stored value, which anything may have edited: entries that could not have been
    /// written are dropped.</summary>
    public static IReadOnlyList<string> Parse(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return [.. stored.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => IsStorable(p) && Path.IsPathFullyQualified(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxCount)];
    }

    public static string Format(IReadOnlyList<string> paths) => string.Join(Separator, paths);

    /// <summary>The access number of an entry: <c>&amp;1</c> to <c>&amp;9</c>, then <c>1&amp;0</c>.</summary>
    /// <param name="index">Zero-based place in the list.</param>
    public static string AccessNumber(int index) => index < 9 ? $"&{index + 1}" : $"{(index + 1) / 10}&{(index + 1) % 10}";

    /// <summary>A path as a menu shows it: shortened, with an <c>&amp;</c> shown as itself.</summary>
    public static string DisplayPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Shorten(path).Replace("&", "&&", StringComparison.Ordinal);
    }

    /// <summary>
    /// The root and as much of the end as fits, as <c>C:\...\folder\name.txt</c>, since the end tells
    /// files apart. A name too long for that loses its start, which keeps its extension.
    /// </summary>
    private static string Shorten(string path)
    {
        if (path.Length <= MaxDisplayLength)
        {
            return path;
        }

        var head = Path.GetPathRoot(path) + Ellipsis;
        var room = MaxDisplayLength - head.Length;
        var start = path.LastIndexOf(Path.DirectorySeparatorChar);
        if (start < 0 || path.Length - start > room)
        {
            return string.Concat(Ellipsis, path.AsSpan(path.Length - (MaxDisplayLength - Ellipsis.Length)));
        }

        // Each step takes one more folder, while the text from that separator on still fits.
        for (var next = start > 0 ? path.LastIndexOf(Path.DirectorySeparatorChar, start - 1) : -1;
            next >= 0 && path.Length - next <= room;
            next = next > 0 ? path.LastIndexOf(Path.DirectorySeparatorChar, next - 1) : -1)
        {
            start = next;
        }

        return string.Concat(head, path.AsSpan(start));
    }

    private static bool IsStorable(string path) =>
        path.Length is > 0 and <= MaxPathLength && !path.Contains(Separator, StringComparison.Ordinal);

    private static IEnumerable<string> Without(IEnumerable<string> paths, string path) =>
        paths.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// File > Recent Files. Every change starts from the stored list, not from <see cref="Items"/>, so
/// that it keeps what other windows added in the meantime. Two windows changing it at the same
/// instant can lose one entry, which is not worth a lock.
/// </summary>
public sealed class RecentFilesService(IRecentFilesStore store)
{
    private readonly IRecentFilesStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The list the menu shows and its commands act on, as of the last
    /// <see cref="Refresh"/> or change. Nothing is read before that, which keeps startup free of it.</summary>
    public IReadOnlyList<string> Items { get; private set; } = [];

    public void Refresh() => Items = _store.Load();

    public void Add(string path) => Change(RecentFilesPolicy.Add(_store.Load(), path));

    public void Remove(string path) => Change(RecentFilesPolicy.Remove(_store.Load(), path));

    public void Clear() => Change([]);

    private void Change(IReadOnlyList<string> paths)
    {
        _store.Save(paths);
        Items = paths;
    }
}
