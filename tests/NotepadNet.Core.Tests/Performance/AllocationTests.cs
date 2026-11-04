using System.Text;
using NotepadNet.Core.Search;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;

namespace NotepadNet.Core.Tests.Performance;

/// <summary>
/// performance.md "Ngưỡng allocation": the hot paths allocate a fixed, small amount whatever the
/// document size, except Replace All, which allocates its result string once. Allocation counts
/// are deterministic, so they gate the build where times cannot (docs/performance.md).
/// </summary>
public sealed class AllocationTests
{
    /// <summary>Bookkeeping objects (encoder, decision records), never proportional to the text.</summary>
    private const long SmallAllocation = 1024;

    private const int Length = 1_000_000;

    private static readonly string Text = BuildText();

    private static string BuildText()
    {
        var builder = new StringBuilder(Length);
        while (builder.Length < Length)
        {
            builder.Append("the quick brown fox jumps over the lazy dog, tiếng Việt\r\n");
        }

        builder.Length = Length;
        return builder.ToString();
    }

    /// <summary>
    /// Allocated bytes on this thread for one call, the smallest of five calls after three
    /// warm-up calls. One-off allocations (JIT tiering and on-stack replacement can allocate on
    /// the calling thread, seen as about 8 KB in one run of fifty) do not repeat, while an
    /// allocation that grows with the text shows up in every call.
    /// </summary>
    private static long Allocated(Action action)
    {
        for (var i = 0; i < 3; i++)
        {
            action();
        }

        var smallest = long.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return smallest;
    }

    [Fact]
    [Trait("Doc", "performance.md#nguong-allocation")]
    public void Detection_does_not_allocate_per_byte()
    {
        var detector = new EncodingDetector(new FakeUnicodeHeuristic());
        var utf8 = Encoding.UTF8.GetBytes(Text);

        Allocated(() => detector.Detect(utf8)).Should().BeLessThan(SmallAllocation);
    }

    [Theory]
    [Trait("Doc", "performance.md#nguong-allocation")]
    [InlineData(TextEncodingKind.Utf8)]
    [InlineData(TextEncodingKind.Utf16LE)]
    [InlineData(TextEncodingKind.Utf16BE)]
    public void Loading_decodes_straight_into_the_editor_buffer(TextEncodingKind kind)
    {
        var detector = new EncodingDetector(new FakeUnicodeHeuristic());
        var loader = new DocumentLoader(detector, new TextDecoder(new ManagedCodePageConverter(1252)));
        var bytes = kind switch
        {
            TextEncodingKind.Utf16LE => Encoding.Unicode.GetBytes(Text),
            TextEncodingKind.Utf16BE => Encoding.BigEndianUnicode.GetBytes(Text),
            _ => Encoding.UTF8.GetBytes(Text),
        };
        var buffers = new ReusedBufferFactory(Length + 1);

        Allocated(() => loader.Load(bytes, kind, keepNulls: false, buffers)).Should().BeLessThan(SmallAllocation);
    }

    [Theory]
    [Trait("Doc", "performance.md#nguong-allocation")]
    [InlineData(TextEncodingKind.Utf8)]
    [InlineData(TextEncodingKind.Utf16LE)]
    [InlineData(TextEncodingKind.Utf16BE)]
    public void Saving_streams_through_a_pooled_chunk(TextEncodingKind kind)
    {
        var encoder = new TextEncoder(new ManagedCodePageConverter(1252));
        var sink = new CountingSink();

        Allocated(() => encoder.WriteBody(Text, kind, allowBestFit: false, sink)).Should().BeLessThan(SmallAllocation);
    }

    [Fact]
    [Trait("Doc", "performance.md#nguong-allocation")]
    public void Counting_matches_allocates_nothing()
    {
        var searcher = new OrdinalSearcher();

        Allocated(() => FindEngine.CountMatches(Text, "fox", matchCase: true, searcher)).Should().Be(0);
    }

    [Fact]
    [Trait("Doc", "performance.md#nguong-allocation")]
    public void Replace_all_allocates_only_its_result()
    {
        var searcher = new OrdinalSearcher();
        string? result = null;

        var allocated = Allocated(() => result = FindEngine.BuildReplaceAll(Text, "fox", "cat", matchCase: true, searcher, out _));

        var resultBytes = (long)result!.Length * sizeof(char);
        allocated.Should().BeInRange(resultBytes, resultBytes + SmallAllocation);
    }

    /// <summary>Hands out the same buffer every time, so the measurement leaves out the one
    /// allocation that is the document itself.</summary>
    private sealed class ReusedBufferFactory(int capacity) : ITextBufferFactory
    {
        private readonly char[] _chars = new char[capacity];

        public ITextBuffer Allocate(int length) => new Buffer(_chars);

        private sealed class Buffer(char[] chars) : ITextBuffer
        {
            public int Capacity => chars.Length;

            public Span<char> Chars => chars;

            public void Dispose()
            {
            }
        }
    }

    private sealed class CountingSink : IByteSink
    {
        public long Count { get; private set; }

        public void Write(ReadOnlySpan<byte> bytes) => Count += bytes.Length;
    }

    /// <summary>Ordinal search without allocation, standing in for <c>FindNLSString</c>.</summary>
    private sealed class OrdinalSearcher : ITextSearcher
    {
        public int Find(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, bool matchCase, bool fromEnd, out int matchLength)
        {
            var index = fromEnd ? text.LastIndexOf(pattern) : text.IndexOf(pattern);
            matchLength = index < 0 ? 0 : pattern.Length;
            return index;
        }
    }
}
