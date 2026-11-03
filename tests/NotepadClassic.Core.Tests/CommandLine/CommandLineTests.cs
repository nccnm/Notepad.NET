using NotepadClassic.Core.CommandLine;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.Tests.CommandLine;

/// <summary>Command line parsing: <c>CmdLineHasPrefix</c>, <c>HandlePrintArgs</c>,
/// <c>HandleRestartManagerArg</c>, file name quoting.</summary>
public sealed class CommandLineTests
{
    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("notepad.exe", "")]
    [InlineData("notepad.exe a.txt", "a.txt")]
    [InlineData("notepad.exe \t  a b.txt ", "a b.txt ")]
    [InlineData("\"C:\\Program Files\\x\\notepad.exe\" /A \"f.txt\"", "/A \"f.txt\"")]
    [InlineData("\"C:\\a b\\n.exe\"", "")]
    [InlineData("C:\\a\"b c\"\\n.exe x", "x")]
    public void Program_name_ends_at_the_first_blank_outside_quotes(string commandLine, string arguments) =>
        CommandLineParser.StripProgramName(commandLine).Should().Be(arguments);

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("/A", "/a", true)]
    [InlineData("/.SETUP", "/.setup x", true)]
    [InlineData("RestartByRestartManager:", "restartbyrestartmanager:", true)]
    [InlineData("/PT", "/P", false)]
    [InlineData("/P", "", false)]
    [InlineData("", "anything", true)]
    public void Prefixes_are_compared_upper_cased(string prefix, string text, bool expected) =>
        CommandLineParser.HasPrefix(text, prefix).Should().Be(expected);

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("", TextEncodingKind.Detect, StartupAction.None, "")]
    [InlineData("   \t ", TextEncodingKind.Detect, StartupAction.None, "")]
    [InlineData("a.txt", TextEncodingKind.Detect, StartupAction.Open, "a.txt")]
    [InlineData("/A a.txt", TextEncodingKind.Ansi, StartupAction.Open, "a.txt")]
    [InlineData("/w  \t\"a b.txt\"", TextEncodingKind.Utf16LE, StartupAction.Open, "\"a b.txt\"")]
    [InlineData("/aXYZ.txt", TextEncodingKind.Ansi, StartupAction.Open, "XYZ.txt")]
    [InlineData("/A", TextEncodingKind.Ansi, StartupAction.None, "")]
    [InlineData("/.NEWWINDOW", TextEncodingKind.Detect, StartupAction.NewWindow, "")]
    [InlineData("/.newwindow", TextEncodingKind.Detect, StartupAction.NewWindow, "")]
    [InlineData("/A /W x", TextEncodingKind.Ansi, StartupAction.Open, "/W x")]
    [InlineData("/.SETUP  c:\\x.inf", TextEncodingKind.Detect, StartupAction.Setup, "c:\\x.inf")]
    [InlineData("/.setup", TextEncodingKind.Detect, StartupAction.Setup, "")]
    [InlineData("/A /.SETUP f", TextEncodingKind.Ansi, StartupAction.Setup, "f")]
    [InlineData("/P a.txt", TextEncodingKind.Detect, StartupAction.Print, "a.txt")]
    [InlineData("/path.txt", TextEncodingKind.Detect, StartupAction.Print, "ath.txt")]
    [InlineData("/P", TextEncodingKind.Detect, StartupAction.InvalidPrint, "")]
    [InlineData("/P   ", TextEncodingKind.Detect, StartupAction.InvalidPrint, "")]
    [InlineData("/PT a.txt \"HP\"", TextEncodingKind.Detect, StartupAction.InvalidPrint, "a.txt \"HP\"")]
    [InlineData("/PT \"a.txt\"", TextEncodingKind.Detect, StartupAction.InvalidPrint, "\"a.txt\"")]
    [InlineData("/PT \"a.txt\" HP", TextEncodingKind.Detect, StartupAction.InvalidPrint, "\"a.txt\" HP")]
    [InlineData("/PT \"unterminated", TextEncodingKind.Detect, StartupAction.InvalidPrint, "\"unterminated")]
    public void Options_and_their_order(string arguments, TextEncodingKind encoding, StartupAction action, string file)
    {
        var parsed = CommandLineParser.Parse(arguments, untrustedSource: false);

        parsed.ForcedEncoding.Should().Be(encoding);
        parsed.Action.Should().Be(action);
        parsed.FileArgument.Should().Be(file);
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("/PT \"a b.txt\" \"Microsoft Print to PDF\"", "\"a b.txt\"", "Microsoft Print to PDF")]
    [InlineData("/pt 'a.txt'\t\"P\" trailing", "'a.txt'", "P")]
    [InlineData("/PT \"a.txt\" \"no closing quote", "\"a.txt\"", "no closing quote")]
    [InlineData("/PT \"a.txt\"\"\"", "\"a.txt\"", "")]
    public void Print_to_takes_the_quoted_printer_after_a_quoted_file(string arguments, string file, string printer)
    {
        var parsed = CommandLineParser.Parse(arguments, untrustedSource: false);

        parsed.Action.Should().Be(StartupAction.PrintTo);
        parsed.FileArgument.Should().Be(file);
        parsed.PrinterName.Should().Be(printer);
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Printer_names_are_cut_to_255_characters()
    {
        var parsed = CommandLineParser.Parse($"/PT \"a\" \"{new string('p', 300)}\"", untrustedSource: false);

        parsed.PrinterName.Should().HaveLength(255);
    }

    [Theory]
    [Trait("Doc", "parity.md#autosave-va-restart-manager")]
    [InlineData("RestartByRestartManager:0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C", "0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C")]
    [InlineData("restartbyrestartmanager:123456789012345678901234567890123456", "123456789012345678901234567890123456")]
    [InlineData("RestartByRestartManager:short", null)]
    [InlineData("RestartByRestartManager:", null)]
    [InlineData("RestartByRestartManager:0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C ", null)]
    public void Restart_takes_exactly_36_characters(string arguments, string? id)
    {
        var parsed = CommandLineParser.Parse(arguments, untrustedSource: false);

        parsed.Action.Should().Be(StartupAction.Restart);
        parsed.RestartId.Should().Be(id);
        parsed.FileArgument.Should().BeEmpty();
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("/.SETUP f", TextEncodingKind.Detect, "/.SETUP f")]
    [InlineData("/P f", TextEncodingKind.Detect, "/P f")]
    [InlineData("/PT \"f\" \"p\"", TextEncodingKind.Detect, "/PT \"f\" \"p\"")]
    [InlineData("RestartByRestartManager:0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C", TextEncodingKind.Detect, "RestartByRestartManager:0B5F1D2A-1C3E-4F5A-9B8C-7D6E5F4A3B2C")]
    [InlineData("/W /P f", TextEncodingKind.Utf16LE, "/P f")]
    public void Untrusted_source_keeps_a_and_w_and_takes_the_rest_as_a_file_name(string arguments, TextEncodingKind encoding, string file)
    {
        var parsed = CommandLineParser.Parse(arguments, untrustedSource: true);

        parsed.ForcedEncoding.Should().Be(encoding);
        parsed.Action.Should().Be(StartupAction.Open);
        parsed.FileArgument.Should().Be(file);
    }

    // ---- File name argument (ParseFileNameArg) ----

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("a.txt", "a.txt", 5)]
    [InlineData("C:\\My Notes\\a b.txt", "C:\\My Notes\\a b.txt", 19)]
    [InlineData("\"a b.txt\" rest", "a b.txt", 9)]
    [InlineData("'a b.txt' rest", "a b.txt", 9)]
    [InlineData("\"it's.txt\"", "it's.txt", 10)]
    [InlineData("'say \"hi\".txt'", "say \"hi\".txt", 14)]
    [InlineData("\"unterminated", "unterminated", 13)]
    [InlineData("\"\"", "", 2)]
    public void File_names_are_quoted_or_take_the_rest(string text, string name, int consumed) =>
        FileArgument.Split(text).Should().Be((name, consumed));

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("notes", false, "C:\\w\\notes.txt")]
    [InlineData("notes", true, "C:\\w\\notes")]
    [InlineData("notes.md", false, "C:\\w\\notes.md")]
    [InlineData("\"my notes\"", false, "C:\\w\\my notes.txt")]
    [InlineData("my.notes v2", false, "C:\\w\\my.notes v2.txt")]
    [InlineData("dir.d\\notes", false, "C:\\w\\dir.d\\notes.txt")]
    public void Missing_names_without_extension_get_txt(string argument, bool exists, string expected)
    {
        var paths = new FakePaths(exists);

        FileArgument.Resolve(argument, paths).Should().Be(expected);
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("a.txt", ".txt")]
    [InlineData("a", "")]
    [InlineData("a.b.c", ".c")]
    [InlineData("dir.x\\a", "")]
    [InlineData("a.b c", "")]
    [InlineData("a.", ".")]
    [InlineData(".bashrc", ".bashrc")]
    [InlineData("", "")]
    public void Extension_rule(string path, string extension) =>
        PathRules.FindExtension(path).Should().Be(extension);

    private sealed class FakePaths(bool exists) : IPathService
    {
        public bool FindFirstFileSucceeds(string path) => exists;

        public string GetFullPath(string path) => "C:\\w\\" + path;
    }
}
