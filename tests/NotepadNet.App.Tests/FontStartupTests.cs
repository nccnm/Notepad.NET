using NotepadNet.App.Editor;
using NotepadNet.Core.Editing;

namespace NotepadNet.App.Tests;

/// <summary>Startup font check: <c>GetTextFaceW</c>, then <c>EnumFontsW</c> takes the
/// character set of the installed font with the saved name.</summary>
public sealed class FontStartupTests
{
    private const byte AnsiCharSet = 0;
    private const byte SymbolCharSet = 2;

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void An_installed_face_with_its_own_character_set_is_kept()
    {
        var font = new FontSpec("Consolas", 110);

        EditorSurface.WithInstalledCharSet(font, 144).Should().BeSameAs(font);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void A_character_set_the_face_lacks_is_replaced_by_the_installed_one()
    {
        // GDI maps Consolas with SYMBOL_CHARSET to a symbol font, so the face differs.
        var font = new FontSpec("Consolas", 110, CharSet: SymbolCharSet);

        EditorSurface.WithInstalledCharSet(font, 144).Should().Be(font with { CharSet = AnsiCharSet });
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void A_face_that_is_not_installed_keeps_the_saved_font()
    {
        var font = new FontSpec("No Such Font 4F2A", 100, CharSet: AnsiCharSet);

        EditorSurface.WithInstalledCharSet(font, 96).Should().Be(font);
    }
}
