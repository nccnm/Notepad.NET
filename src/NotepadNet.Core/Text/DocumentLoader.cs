namespace NotepadNet.Core.Text;

/// <summary>Decoded document text. Owns <see cref="Buffer"/> until it is handed to the editor.</summary>
/// <param name="Buffer">Text followed by a NUL at index <see cref="Length"/>.</param>
/// <param name="StartsWithLogMarker">Text starts with <c>.LOG</c>: the caller
/// moves the caret to the end and inserts a time stamp.</param>
public sealed record DecodedDocument(ITextBuffer Buffer, int Length, TextEncodingKind Encoding, bool StartsWithLogMarker);

/// <summary>
/// The text part of the original's file load (<c>LoadFile</c>):
/// size limit, encoding choice, decode, NUL to space, <c>.LOG</c> marker.
/// </summary>
public sealed class DocumentLoader(EncodingDetector detector, TextDecoder decoder)
{
    /// <summary>Files of this size or larger are refused (1 GiB).</summary>
    public const long MaxFileSize = 0x4000_0000;

    private const string LogMarker = ".LOG";

    private readonly EncodingDetector _detector = detector;
    private readonly TextDecoder _decoder = decoder;

    public static bool IsTooLarge(long fileLength) => fileLength >= MaxFileSize;

    /// <param name="data">Whole file content.</param>
    /// <param name="forced"><see cref="TextEncodingKind.Detect"/> or the encoding the user chose.</param>
    /// <param name="keepNulls">Keep U+0000 characters and skip the <c>.LOG</c> check
    /// (the original's third <c>LoadFile</c> argument).</param>
    /// <exception cref="OutOfMemoryException">The text buffer cannot be allocated.</exception>
    public DecodedDocument Load(ReadOnlySpan<byte> data, TextEncodingKind forced, bool keepNulls, ITextBufferFactory buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (IsTooLarge(data.Length))
        {
            throw new ArgumentException("File is too large to load.", nameof(data));
        }

        var decision = forced == TextEncodingKind.Detect ? _detector.Detect(data) : EncodingDetector.Forced(forced, data);
        return Decode(data, decision, keepNulls, buffers);
    }

    /// <summary>
    /// Decodes with a plan the caller already has, BOM or not: an autosave text file is raw
    /// UTF-16 LE, so a leading U+FEFF is text, not a BOM.
    /// </summary>
    /// <exception cref="OutOfMemoryException">The text buffer cannot be allocated.</exception>
    public DecodedDocument Load(ReadOnlySpan<byte> data, EncodingDecision decision, bool keepNulls, ITextBufferFactory buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (IsTooLarge(data.Length))
        {
            throw new ArgumentException("File is too large to load.", nameof(data));
        }

        return Decode(data, decision, keepNulls, buffers);
    }

    private DecodedDocument Decode(ReadOnlySpan<byte> data, EncodingDecision decision, bool keepNulls, ITextBufferFactory buffers)
    {
        var length = _decoder.GetCharCount(decision, data);
        var buffer = buffers.Allocate(length);
        try
        {
            var chars = buffer.Chars;
            length = _decoder.Decode(decision, data, chars);
            chars[length] = '\0';

            var isLog = false;
            if (!keepNulls)
            {
                // Lets binary files display instead of being cut at the first NUL.
                chars[..length].Replace('\0', ' ');
                isLog = chars[..length].StartsWith(LogMarker, StringComparison.Ordinal);
            }

            return new DecodedDocument(buffer, length, decision.Encoding, isLog);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }
}
