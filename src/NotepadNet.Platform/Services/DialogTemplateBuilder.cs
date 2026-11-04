using System.Buffers.Binary;

namespace NotepadNet.Platform.Services;

/// <summary>
/// Writes an in-memory <c>DLGTEMPLATEEX</c> for <c>DialogBoxIndirectParamW</c>: header, menu and
/// class left empty, title, font (with <c>DS_SETFONT</c>), then each item aligned to a DWORD with a
/// predefined class atom or a class name, and no creation data. Coordinates are dialog units.
/// </summary>
internal sealed class DialogTemplateBuilder(uint style, uint exStyle, DialogUnits bounds, string title, string fontFace, ushort pointSize)
{
    public const uint DS_SETFONT = 0x40;
    public const uint DS_MODALFRAME = 0x80;
    public const uint WS_POPUP = 0x8000_0000;
    public const uint WS_CHILD = 0x4000_0000;
    public const uint WS_VISIBLE = 0x1000_0000;
    public const uint WS_CAPTION = 0x00C0_0000;
    public const uint WS_BORDER = 0x0080_0000;
    public const uint WS_SYSMENU = 0x0008_0000;
    public const uint WS_GROUP = 0x0002_0000;
    public const uint WS_TABSTOP = 0x0001_0000;
    public const uint ES_AUTOHSCROLL = 0x80;
    public const uint ES_NUMBER = 0x2000;
    public const uint BS_DEFPUSHBUTTON = 0x1;

    public const ushort ButtonClass = 0x0080;
    public const ushort EditClass = 0x0081;
    public const ushort StaticClass = 0x0082;

    private readonly List<Item> _items = [];

    public const ushort ComboBoxClass = 0x0085;

    public DialogTemplateBuilder AddItem(uint id, ushort classAtom, string text, uint itemStyle, DialogUnits itemBounds)
    {
        ArgumentNullException.ThrowIfNull(text);
        _items.Add(new(id, classAtom, null, text, itemStyle, itemBounds));
        return this;
    }

    /// <summary>An item of a registered window class named by string (for example <c>SysLink</c>).</summary>
    public DialogTemplateBuilder AddItem(uint id, string className, string text, uint itemStyle, DialogUnits itemBounds)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(text);
        _items.Add(new(id, 0, className, text, itemStyle, itemBounds));
        return this;
    }

    public byte[] Build()
    {
        var writer = new Writer();
        writer.UInt16(1);                 // dlgVer
        writer.UInt16(0xFFFF);            // signature
        writer.UInt32(0);                 // helpID
        writer.UInt32(exStyle);
        writer.UInt32(style);
        writer.UInt16((ushort)_items.Count);
        writer.Bounds(bounds);
        writer.UInt16(0);                 // no menu
        writer.UInt16(0);                 // default dialog class
        writer.String(title);
        if ((style & DS_SETFONT) != 0)
        {
            writer.UInt16(pointSize);
            writer.UInt16(0);             // weight
            writer.Byte(0);               // italic
            writer.Byte(0);               // charset
            writer.String(fontFace);
        }

        foreach (var item in _items)
        {
            writer.AlignToDword();
            writer.UInt32(0);             // helpID
            writer.UInt32(0);             // exStyle
            writer.UInt32(item.Style);
            writer.Bounds(item.Bounds);
            writer.UInt32(item.Id);
            if (item.ClassName is null)
            {
                writer.UInt16(0xFFFF);
                writer.UInt16(item.ClassAtom);
            }
            else
            {
                writer.String(item.ClassName);
            }

            writer.String(item.Text);
            writer.UInt16(0);             // no creation data
        }

        return writer.ToArray();
    }

    private sealed record Item(uint Id, ushort ClassAtom, string? ClassName, string Text, uint Style, DialogUnits Bounds);

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void Byte(byte value) => _bytes.Add(value);

        public void UInt16(ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void UInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void Bounds(DialogUnits bounds)
        {
            UInt16((ushort)bounds.X);
            UInt16((ushort)bounds.Y);
            UInt16((ushort)bounds.Width);
            UInt16((ushort)bounds.Height);
        }

        public void String(string text)
        {
            foreach (var c in text)
            {
                UInt16(c);
            }

            UInt16(0);
        }

        public void AlignToDword()
        {
            while (_bytes.Count % 4 != 0)
            {
                _bytes.Add(0);
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}

/// <summary>A rectangle in dialog units.</summary>
internal readonly record struct DialogUnits(short X, short Y, short Width, short Height);
