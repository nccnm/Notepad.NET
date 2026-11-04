using System.Text;

namespace NotepadNet.Benchmarks;

/// <summary>
/// Sample documents generated at run time (never committed): lines of lower-case ASCII words,
/// CRLF line ends, the same text for the same seed. <see cref="Needle"/> appears exactly every
/// <c>needleEvery</c> characters when asked for, and nowhere else.
/// </summary>
public static class SampleText
{
    public const string Needle = "needle";

    private static readonly string[] Words =
    [
        "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "notepad", "classic", "port", "window",
        "file", "edit", "format", "view", "help", "status", "bar", "line", "column", "encoding", "save", "open",
    ];

    /// <summary>ASCII text of exactly <paramref name="length"/> characters.</summary>
    public static string Ascii(int length, int needleEvery = 0, int seed = 42)
    {
        var random = new Random(seed);
        var builder = new StringBuilder(length + 64);
        var lineLength = 0;
        var nextNeedle = needleEvery;
        while (builder.Length < length)
        {
            if (needleEvery > 0 && builder.Length >= nextNeedle)
            {
                builder.Append(Needle).Append(' ');
                nextNeedle += needleEvery;
                continue;
            }

            var word = Words[random.Next(Words.Length)];
            builder.Append(word);
            lineLength += word.Length + 1;
            if (lineLength > 70 + random.Next(30))
            {
                builder.Append("\r\n");
                lineLength = 0;
            }
            else
            {
                builder.Append(' ');
            }
        }

        builder.Length = length;
        return builder.ToString();
    }

    /// <summary>The same text with every 50th word in Vietnamese, so UTF-8 has multi-byte sequences.</summary>
    public static string Mixed(int length, int seed = 42)
    {
        var ascii = Ascii(length, 0, seed).ToCharArray();
        const string accented = "tiếng Việt ";
        for (var i = 400; i + accented.Length < ascii.Length; i += 400)
        {
            accented.CopyTo(0, ascii, i, accented.Length);
        }

        return new string(ascii);
    }
}
