using System.Text;
using BenchmarkDotNet.Attributes;
using NotepadNet.Core.Search;
using NotepadNet.Core.Text;
using NotepadNet.Platform.IO;
using NotepadNet.Platform.Text;

namespace NotepadNet.Benchmarks;

/// <summary>
/// The budgets of docs/performance.md on a 50 MB document: detect, decode, Find without a match,
/// Replace All with 100 000 matches, encode and write. Real Win32 services (IsTextUnicode,
/// FindNLSString, MultiByteToWideChar, CreateFileW) as the app uses them.
/// </summary>
[MemoryDiagnoser]
public class HotPathBenchmarks
{
    public const int DocumentLength = 50_000_000;

    private readonly Win32CodePageConverter _converter = new();
    private readonly NlsTextSearcher _searcher = new();
    private readonly Win32FileSystem _files = new();
    private EncodingDetector _detector = null!;
    private TextDecoder _decoder = null!;
    private DocumentLoader _loader = null!;
    private TextEncoder _encoder = null!;
    private string _text = null!;
    private string _ascii = null!;
    private string _needles = null!;
    private byte[] _utf8 = null!;
    private byte[] _utf16 = null!;
    private byte[] _ansi = null!;
    private string _output = null!;

    [GlobalSetup]
    public void Setup()
    {
        _detector = new EncodingDetector(new Win32UnicodeHeuristic());
        _decoder = new TextDecoder(_converter);
        _loader = new DocumentLoader(_detector, _decoder);
        _encoder = new TextEncoder(_converter);
        _text = SampleText.Mixed(DocumentLength);
        _ascii = SampleText.Ascii(DocumentLength);
        _needles = SampleText.Ascii(DocumentLength, needleEvery: DocumentLength / 100_000);
        _utf8 = Encoding.UTF8.GetBytes(_text);
        _utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(_text)];
        _ansi = Encoding.Latin1.GetBytes(_ascii);
        _output = Path.Combine(Path.GetTempPath(), $"npport-bench-{Environment.ProcessId}.txt");
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_output);

    [Benchmark]
    public EncodingDecision DetectUtf8() => _detector.Detect(_utf8);

    [Benchmark]
    public int LoadUtf8() => Load(_utf8);

    [Benchmark]
    public int LoadUtf16() => Load(_utf16);

    [Benchmark]
    public int LoadAnsi() => Load(_ansi);

    /// <summary>Detect, decode into a <c>LocalAlloc</c> buffer as the app does, NUL to space.</summary>
    private int Load(byte[] data)
    {
        var document = _loader.Load(data, TextEncodingKind.Detect, keepNulls: false, LocalTextBufferFactory.Instance);
        document.Buffer.Dispose();
        return document.Length;
    }

    /// <summary>ASCII only: <c>FindNLSString</c> stays on its fast path.</summary>
    [Benchmark]
    public int FindWithoutMatchAscii() => _searcher.Find(_ascii, "zqxj", matchCase: false, fromEnd: false, out _);

    [Benchmark]
    public int FindWithoutMatchAsciiMatchCase() => _searcher.Find(_ascii, "zqxj", matchCase: true, fromEnd: false, out _);

    /// <summary>One Vietnamese word every 400 characters: the linguistic comparison is much slower.</summary>
    [Benchmark]
    public int FindWithoutMatchMixed() => _searcher.Find(_text, "zqxj", matchCase: false, fromEnd: false, out _);

    [Benchmark]
    public int ReplaceAll()
    {
        _ = FindEngine.BuildReplaceAll(_needles, SampleText.Needle, "pin", matchCase: false, _searcher, out var count);
        return count;
    }

    [Benchmark]
    public void SaveUtf8() => Save(TextEncodingKind.Utf8);

    [Benchmark]
    public void SaveAnsi() => Save(TextEncodingKind.Ansi);

    /// <summary>Open in place, BOM, body, <c>SetEndOfFile</c>, as <c>DocumentService.SaveFile</c> does.</summary>
    private void Save(TextEncodingKind kind)
    {
        var text = kind == TextEncodingKind.Ansi ? _needles : _text;
        using var file = _files.OpenForSave(_output, shareWrite: true);
        file.Write(TextEncoder.GetPreamble(kind));
        _encoder.WriteBody(text, kind, allowBestFit: false, file);
        file.SetEndOfFile();
    }
}
