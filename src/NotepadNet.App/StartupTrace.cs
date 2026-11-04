using System.Diagnostics;
using System.Globalization;

namespace NotepadNet.App;

/// <summary>
/// Optional startup timeline (docs/performance.md, "Khởi động"): when
/// <see cref="Variable"/> names a file, every <see cref="Mark"/> is recorded and, at the first idle
/// of the message loop, appended to that file as one line per mark with the milliseconds since the
/// process was created. Without the variable the whole thing costs one environment read.
/// </summary>
internal static class StartupTrace
{
    public const string Variable = "NOTEPADNET_STARTUP_TRACE";

    private static readonly string? s_path = Environment.GetEnvironmentVariable(Variable);
    private static readonly List<(string Label, DateTime At)> s_marks = [];

    public static bool Enabled => !string.IsNullOrEmpty(s_path);

    /// <summary>Records <paramref name="label"/> at the current precise system time.</summary>
    public static void Mark(string label)
    {
        if (Enabled)
        {
            s_marks.Add((label, DateTime.UtcNow));
        }
    }

    /// <summary>Appends the marks to the trace file, relative to the process creation time.</summary>
    public static void Flush()
    {
        if (!Enabled)
        {
            return;
        }

        Mark("Flush");
        DateTime start;
        using (var process = Process.GetCurrentProcess())
        {
            start = process.StartTime.ToUniversalTime();
        }

        var lines = s_marks.Select(m => string.Create(
            CultureInfo.InvariantCulture, $"{(m.At - start).TotalMilliseconds,9:F1}\t{m.Label}"));
        // Methods the JIT compiled so far (ReadyToRun code is not counted) and the time it spent.
        var jit = string.Create(
            CultureInfo.InvariantCulture,
            $"{"",9}\tJIT: {System.Runtime.JitInfo.GetCompiledMethodCount()} methods, {System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds:F1} ms, GCs: {GC.CollectionCount(0)}");
        File.AppendAllLines(s_path!, [.. lines, jit, ""]);
        s_marks.Clear();
    }
}
