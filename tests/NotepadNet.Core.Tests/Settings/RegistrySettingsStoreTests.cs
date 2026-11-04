using NotepadNet.Core.Editing;
using NotepadNet.Core.Settings;
using NotepadNet.Core.Tests.Fakes;
using NotepadNet.Core.Text;
using static NotepadNet.Core.Settings.SettingsLocation;

namespace NotepadNet.Core.Tests.Settings;

/// <summary>Loading and saving settings: every registry value, its kind and limits, and the
/// machine-wide default font.</summary>
public sealed class RegistrySettingsStoreTests
{
    private readonly MemorySettingsRegistry _registry = new();

    private RegistrySettingsStore Store(bool metric = false) => new(_registry, new SettingsDefaults(metric, "&f", "Page &p"));

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Empty_registry_gives_the_defaults_of_the_table()
    {
        var s = Store().Load();

        s.Font.Should().Be(new FontSpec("Lucida Console", 100));
        s.WordWrap.Should().BeFalse();
        s.StatusBar.Should().BeTrue();
        s.DefaultEncoding.Should().Be(TextEncodingKind.Utf8);
        s.WindowsOnlyEol.Should().BeFalse();
        s.PasteOriginalEol.Should().BeFalse();
        s.Reverse.Should().BeFalse();
        s.WrapAround.Should().BeFalse();
        s.MatchCase.Should().BeFalse();
        s.SearchString.Should().BeEmpty();
        s.ReplaceString.Should().BeEmpty();
        s.Header.Should().Be("&f");
        s.Trailer.Should().Be("Page &p");
        s.Margins.Should().Be(new PageMargins(750, 1000, 750, 1000));
        s.MarginsInInches.Should().BeTrue();
        s.WindowPosition.Should().Be(WindowPosition.Default);
        s.WindowPosition.IsSet.Should().BeFalse();
        s.WindowBaseline.Should().Be(default(WindowRect));
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Metric_locale_defaults_to_25_and_20_millimeter_margins()
    {
        var s = Store(metric: true).Load();

        s.Margins.Should().Be(new PageMargins(2000, 2500, 2000, 2500));
        s.MarginsInInches.Should().BeFalse();
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Five_dwords_are_created_as_zero_on_first_load()
    {
        Store().Load();

        _registry.User!.Keys.Should().BeEquivalentTo(SettingsSchema.CreatedAsZero);
        _registry.User.Values.Should().AllSatisfy(v => v.Should().Be(new MemorySettingsRegistry.Value(MemorySettingsRegistry.Dword, 0)));
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void A_value_of_the_wrong_kind_is_overwritten_with_zero_but_existing_ones_are_kept()
    {
        _registry.SetString(User, SettingsSchema.Reverse, "1");
        _registry.SetDword(User, SettingsSchema.MatchCase, 7);
        _registry.SetDword(User, SettingsSchema.WrapAround, 1);
        _registry.SetDword(User, SettingsSchema.WindowsOnlyEol, 1);
        _registry.SetDword(User, SettingsSchema.PasteOriginalEol, 1);

        var s = Store().Load();

        s.Reverse.Should().BeFalse();
        s.MatchCase.Should().BeTrue();
        s.WrapAround.Should().BeTrue();
        s.WindowsOnlyEol.Should().BeTrue();
        s.PasteOriginalEol.Should().BeTrue();
        _registry.User![SettingsSchema.Reverse].Should().Be(new MemorySettingsRegistry.Value(MemorySettingsRegistry.Dword, 0));
        _registry.User[SettingsSchema.MatchCase].Data.Should().Be(7);
        _registry.Writes.Should().Be(1);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Wrongly_typed_values_take_the_default()
    {
        _registry.SetString(User, SettingsSchema.Wrap, "1");
        _registry.SetString(User, SettingsSchema.StatusBar, "0");
        _registry.Key(User)[SettingsSchema.PointSize] = new(MemorySettingsRegistry.Binary, 120);
        _registry.SetDword(User, SettingsSchema.FaceName, 5);

        var s = Store().Load();

        s.WordWrap.Should().BeFalse();
        s.StatusBar.Should().BeTrue();
        s.Font.Should().Be(FontSpec.Default);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Every_value_is_read_from_the_user_key()
    {
        foreach (var (name, value) in new[]
        {
            (SettingsSchema.Escapement, 1), (SettingsSchema.Orientation, 2), (SettingsSchema.Weight, 700),
            (SettingsSchema.Italic, 0x101), (SettingsSchema.Underline, 1), (SettingsSchema.StrikeOut, 1),
            (SettingsSchema.CharSet, 0x1A3), (SettingsSchema.OutPrecision, 3), (SettingsSchema.ClipPrecision, 2),
            (SettingsSchema.Quality, 5), (SettingsSchema.PitchAndFamily, 49), (SettingsSchema.PointSize, 120),
            (SettingsSchema.Wrap, 1), (SettingsSchema.StatusBar, 0), (SettingsSchema.DefaultEncoding, 4),
            (SettingsSchema.MarginTop, 1), (SettingsSchema.MarginBottom, 2), (SettingsSchema.MarginLeft, 3), (SettingsSchema.MarginRight, 4),
            (SettingsSchema.WindowPosX, 25), (SettingsSchema.WindowPosY, 30), (SettingsSchema.WindowPosDX, 800), (SettingsSchema.WindowPosDY, 600),
        })
        {
            _registry.SetDword(User, name, value);
        }

        _registry.SetString(User, SettingsSchema.FaceName, "Consolas\0");
        _registry.SetString(User, SettingsSchema.SearchString, "find\0");
        _registry.SetString(User, SettingsSchema.ReplaceString, "repl\0");
        _registry.SetString(User, SettingsSchema.Header, "H &d\0");
        _registry.SetString(User, SettingsSchema.Trailer, "\0");

        var s = Store().Load();

        // BYTE fields are cut to their low byte: lfItalic 0x101 is 1, lfCharSet 0x1A3 is 0xA3.
        s.Font.Should().Be(new FontSpec("Consolas", 120, 700, true, true, true, 0xA3, 3, 2, 5, 49, 1, 2));
        s.WordWrap.Should().BeTrue();
        s.StatusBar.Should().BeFalse();
        s.DefaultEncoding.Should().Be(TextEncodingKind.Utf8Bom);
        s.SearchString.Should().Be("find");
        s.ReplaceString.Should().Be("repl");
        s.Header.Should().Be("H &d");
        s.Trailer.Should().BeEmpty();
        s.Margins.Should().Be(new PageMargins(3, 1, 4, 2));
        s.WindowPosition.Should().Be(new WindowPosition(25, 30, 800, 600));
        s.WindowPosition.IsSet.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Machine_default_font_is_used_only_without_a_user_font()
    {
        _registry.SetString(MachineDefaultFonts, SettingsSchema.FaceName, "Consolas\0");
        _registry.SetDword(MachineDefaultFonts, SettingsSchema.PointSize, 110);

        Store().Load().Font.Should().Be(new FontSpec("Consolas", 110));

        _registry.SetString(User, SettingsSchema.FaceName, "Courier New\0");
        Store().Load().Font.Should().Be(new FontSpec("Courier New", 110));

        _registry.SetDword(User, SettingsSchema.PointSize, 90);
        Store().Load().Font.Should().Be(new FontSpec("Courier New", 90));
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Machine_default_key_with_bad_values_falls_back_to_lucida_console()
    {
        _registry.SetDword(MachineDefaultFonts, SettingsSchema.FaceName, 1);
        _registry.SetString(MachineDefaultFonts, SettingsSchema.PointSize, "110");

        Store().Load().Font.Should().Be(FontSpec.Default);
    }

    [Theory]
    [Trait("Doc", "parity.md#registry")]
    [InlineData("abc", 40, "abc")]
    [InlineData("abc\0def", 40, "abc")]
    [InlineData("", 40, "")]
    [InlineData("0123456789", 10, "012345678")]
    [InlineData("012345678\0", 10, "012345678")]
    public void Registry_strings_stop_at_nul_and_fit_the_buffer(string raw, int capacity, string expected) =>
        RegistrySettingsStore.TrimRegistryString(raw, capacity).Should().Be(expected);

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void A_string_longer_than_the_buffer_is_ignored()
    {
        _registry.SetString(User, SettingsSchema.SearchString, new string('x', 129));
        _registry.SetString(User, SettingsSchema.Header, new string('h', 40));

        var s = Store().Load();

        s.SearchString.Should().BeEmpty();
        // Exactly 40 characters without NUL fits the 40-WCHAR buffer; the port cuts it to 39.
        s.Header.Should().Be(new string('h', 39));
    }

    // ---- Save ----

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Nothing_changed_writes_nothing()
    {
        var store = Store();
        var s = store.Load();
        var writes = _registry.Writes;

        store.Save(s, null);
        store.Save(s, s.WindowBaseline);

        _registry.Writes.Should().Be(writes);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void A_window_opened_at_the_default_position_saves_its_position_on_exit()
    {
        var store = Store();
        var s = store.Load();

        store.Save(s, new WindowRect(10, 20, 810, 620));

        var user = _registry.User!;
        user[SettingsSchema.WindowPosX].Data.Should().Be(10);
        user[SettingsSchema.WindowPosY].Data.Should().Be(20);
        user[SettingsSchema.WindowPosDX].Data.Should().Be(800);
        user[SettingsSchema.WindowPosDY].Data.Should().Be(600);
        user.Should().NotContainKeys(SettingsSchema.Wrap, SettingsSchema.FaceName, SettingsSchema.SearchString, SettingsSchema.Header);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Position_equal_to_the_startup_baseline_is_not_written()
    {
        var store = Store();
        var s = store.Load();
        s.WindowBaseline = new WindowRect(10, 20, 810, 620);
        s.ViewChanged = true;

        store.Save(s, new WindowRect(10, 20, 810, 620));

        _registry.User.Should().ContainKeys(SettingsSchema.Wrap, SettingsSchema.StatusBar);
        _registry.User.Should().NotContainKey(SettingsSchema.WindowPosX);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Each_flag_writes_its_own_group()
    {
        var store = Store();
        var s = store.Load();
        s.Font = new FontSpec("Consolas", 120, 700, Italic: true, CharSet: 1, Escapement: 5);
        s.FontChanged = true;
        store.Save(s, null);
        var user = _registry.User!;
        user[SettingsSchema.FaceName].Data.Should().Be("Consolas");
        user[SettingsSchema.PointSize].Data.Should().Be(120);
        user[SettingsSchema.Weight].Data.Should().Be(700);
        user[SettingsSchema.Italic].Data.Should().Be(1);
        user[SettingsSchema.CharSet].Data.Should().Be(1);
        user[SettingsSchema.Escapement].Data.Should().Be(5);
        user.Should().NotContainKeys(SettingsSchema.Wrap, SettingsSchema.Header, SettingsSchema.SearchString);

        s.FontChanged = false;
        s.PageSetupChanged = true;
        s.Header = "head";
        s.Trailer = "foot";
        s.Margins = new PageMargins(1, 2, 3, 4);
        store.Save(s, null);
        user[SettingsSchema.Header].Data.Should().Be("head");
        user[SettingsSchema.Trailer].Data.Should().Be("foot");
        user[SettingsSchema.MarginLeft].Data.Should().Be(1);
        user[SettingsSchema.MarginTop].Data.Should().Be(2);
        user[SettingsSchema.MarginRight].Data.Should().Be(3);
        user[SettingsSchema.MarginBottom].Data.Should().Be(4);

        s.PageSetupChanged = false;
        s.SearchChanged = true;
        s.Reverse = true;
        s.MatchCase = true;
        s.WrapAround = true;
        s.SearchString = "a";
        s.ReplaceString = "b";
        store.Save(s, null);
        user[SettingsSchema.Reverse].Data.Should().Be(1);
        user[SettingsSchema.MatchCase].Data.Should().Be(1);
        user[SettingsSchema.WrapAround].Data.Should().Be(1);
        user[SettingsSchema.SearchString].Data.Should().Be("a");
        user[SettingsSchema.ReplaceString].Data.Should().Be("b");

        s.SearchChanged = false;
        s.ViewChanged = true;
        s.WordWrap = true;
        s.StatusBar = false;
        store.Save(s, null);
        user[SettingsSchema.Wrap].Data.Should().Be(1);
        user[SettingsSchema.StatusBar].Data.Should().Be(0);
    }

    [Theory]
    [Trait("Doc", "parity.md#registry")]
    [InlineData(80, 80)]
    [InlineData(5, OpacityPolicy.Minimum)]
    [InlineData(-1, OpacityPolicy.Minimum)]
    [InlineData(250, OpacityPolicy.Maximum)]
    public void Opacity_is_read_within_its_limits(int stored, int expected)
    {
        Store().Load();
        _registry.User![SettingsSchema.OpacityPercent] = new MemorySettingsRegistry.Value(MemorySettingsRegistry.Dword, stored);

        Store().Load().OpacityPercent.Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Opacity_defaults_to_opaque_is_saved_with_the_view_group_and_is_not_imported()
    {
        _registry.SetDword(Legacy, SettingsSchema.OpacityPercent, 50);
        _registry.SetDword(Legacy, SettingsSchema.Wrap, 1);
        var store = Store();
        var s = store.Load();
        s.OpacityPercent.Should().Be(OpacityPolicy.Maximum, "the original has no such value to import");
        _registry.User.Should().NotContainKey(SettingsSchema.OpacityPercent);

        s.OpacityPercent = 70;
        s.ViewChanged = true;
        store.Save(s, null);

        _registry.User![SettingsSchema.OpacityPercent].Should().Be(new MemorySettingsRegistry.Value(MemorySettingsRegistry.Dword, 70));
        store.Load().OpacityPercent.Should().Be(70);
    }

    [Theory]
    [Trait("Doc", "parity.md#registry")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    public void Always_on_top_is_read_as_a_flag(int stored, bool expected)
    {
        Store().Load();
        _registry.User![SettingsSchema.AlwaysOnTop] = new MemorySettingsRegistry.Value(MemorySettingsRegistry.Dword, stored);

        Store().Load().AlwaysOnTop.Should().Be(expected);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Always_on_top_defaults_to_off_is_saved_with_the_view_group_and_is_not_imported()
    {
        _registry.SetDword(Legacy, SettingsSchema.AlwaysOnTop, 1);
        _registry.SetDword(Legacy, SettingsSchema.Wrap, 1);
        var store = Store();
        var s = store.Load();
        s.AlwaysOnTop.Should().BeFalse("the original has no such value to import");
        _registry.User.Should().NotContainKey(SettingsSchema.AlwaysOnTop);

        s.AlwaysOnTop = true;
        s.ViewChanged = true;
        store.Save(s, null);

        _registry.User![SettingsSchema.AlwaysOnTop].Should().Be(new MemorySettingsRegistry.Value(MemorySettingsRegistry.Dword, 1));
        store.Load().AlwaysOnTop.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Never_written_values_stay_untouched_even_when_every_group_changes()
    {
        var store = Store();
        var s = store.Load();
        _registry.User!.Clear();
        s.FontChanged = s.ViewChanged = s.PageSetupChanged = s.SearchChanged = true;
        s.DefaultEncoding = TextEncodingKind.Ansi;
        s.WindowsOnlyEol = true;

        store.Save(s, new WindowRect(1, 1, 2, 2));

        _registry.User.Should().NotContainKeys(
            SettingsSchema.DefaultEncoding, SettingsSchema.WindowsOnlyEol, SettingsSchema.PasteOriginalEol,
            SettingsSchema.SaveWindowPositions, SettingsSchema.MleIsBroken);
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Saved_strings_are_cut_to_their_buffer()
    {
        var store = Store();
        var s = store.Load();
        s.SearchChanged = true;
        s.SearchString = new string('s', 200);
        s.PageSetupChanged = true;
        s.Header = new string('h', 50);
        s.FontChanged = true;
        s.Font = new FontSpec(new string('f', 40), 100);

        store.Save(s, null);

        _registry.User![SettingsSchema.SearchString].Data.Should().Be(new string('s', 127));
        _registry.User[SettingsSchema.Header].Data.Should().Be(new string('h', 39));
        _registry.User[SettingsSchema.FaceName].Data.Should().Be(new string('f', 31));
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void A_key_that_cannot_be_created_saves_and_creates_nothing()
    {
        _registry.CanCreate = false;
        var store = Store();
        var s = store.Load();
        s.ViewChanged = true;

        store.Save(s, new WindowRect(1, 2, 3, 4));

        _registry.User.Should().BeNull();
        s.Reverse.Should().BeFalse();
    }

    // ---- Import from HKCU\Software\Microsoft\Notepad ----

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void First_run_imports_the_valid_values_of_the_original_key_once()
    {
        _registry.SetDword(Legacy, SettingsSchema.Wrap, 1);
        _registry.SetDword(Legacy, SettingsSchema.WindowPosX, 25);
        _registry.SetDword(Legacy, SettingsSchema.WindowPosY, 25);
        _registry.SetDword(Legacy, SettingsSchema.WindowPosDX, 1920);
        _registry.SetDword(Legacy, SettingsSchema.WindowPosDY, 1025);
        _registry.SetDword(Legacy, SettingsSchema.Reverse, 1);
        _registry.SetDword(Legacy, SettingsSchema.DefaultEncoding, 1);
        _registry.SetString(Legacy, SettingsSchema.SearchString, "bấm\0");
        _registry.SetString(Legacy, SettingsSchema.ReplaceString, "\0");
        _registry.SetDword(Legacy, "ShowStoreBanner", 0);
        _registry.SetDword(Legacy, SettingsSchema.MleIsBroken, 1);
        _registry.SetString(Legacy, SettingsSchema.StatusBar, "bad kind");
        var legacyBefore = _registry.Key(Legacy).ToDictionary();

        var s = Store().Load();

        s.WordWrap.Should().BeTrue();
        s.WindowPosition.Should().Be(new WindowPosition(25, 25, 1920, 1025));
        s.Reverse.Should().BeTrue();
        s.DefaultEncoding.Should().Be(TextEncodingKind.Ansi);
        s.SearchString.Should().Be("bấm");
        s.StatusBar.Should().BeTrue();
        _registry.User.Should().NotContainKeys("ShowStoreBanner", SettingsSchema.MleIsBroken, SettingsSchema.StatusBar);
        _registry.Key(Legacy).Should().Equal(legacyBefore);

        // The port's key exists now: a changed original key is not imported again.
        _registry.SetDword(Legacy, SettingsSchema.Wrap, 0);
        Store().ImportLegacy().Should().BeFalse();
        Store().Load().WordWrap.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#registry")]
    public void Nothing_to_import_without_the_original_key_or_with_only_unused_values()
    {
        Store().ImportLegacy().Should().BeFalse();

        _registry.SetDword(Legacy, "ShowStoreBanner", 0);
        Store().ImportLegacy().Should().BeFalse();
        _registry.User.Should().BeNull();

        _registry.SetDword(Legacy, SettingsSchema.Wrap, 1);
        _registry.CanCreate = false;
        Store().ImportLegacy().Should().BeFalse();
    }
}
