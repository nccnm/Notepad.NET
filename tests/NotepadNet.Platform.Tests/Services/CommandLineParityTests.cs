using System.Runtime.InteropServices;
using NotepadNet.Core.CommandLine;
using NotepadNet.Platform.Services;

namespace NotepadNet.Platform.Tests.Services;

/// <summary>
/// The managed command-line rules against the Win32 functions the original calls:
/// <c>CharUpperW</c> (<c>CmdLineHasPrefix</c>), <c>PathFindExtensionW</c>, <c>FindFirstFileW</c>,
/// <c>GetFullPathNameW</c> (<c>ParseFileNameArg</c>).
/// </summary>
public sealed partial class CommandLineParityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-cmd-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [LibraryImport("user32.dll", EntryPoint = "CharUpperW")]
    private static partial nint CharUpper(nint character);

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Upper_casing_matches_CharUpperW_for_every_character_of_the_option_prefixes()
    {
        var prefixChars = string.Concat(CommandLineParser.SetupOption, CommandLineParser.PrintToOption, CommandLineParser.RestartPrefix, "/A/W")
            .Select(char.ToUpperInvariant).ToHashSet();
        var mismatches = new List<int>();
        for (var c = 1; c <= 0xFFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            // Each character against an upper-case prefix letter, the way CmdLineHasPrefix compares.
            var text = ((char)c).ToString();
            var upper = (char)CharUpper(c);
            foreach (var letter in prefixChars)
            {
                var managed = CommandLineParser.HasPrefix(text, letter.ToString());
                var win32 = upper == letter;
                if (managed != win32)
                {
                    mismatches.Add(c);
                }
            }
        }

        mismatches.Should().BeEmpty();
        CommandLineParser.HasPrefix("/.ſETUP", CommandLineParser.SetupOption).Should().BeFalse();
    }

    [Theory]
    [Trait("Doc", "parity.md#dong-lenh")]
    [InlineData("a.txt")]
    [InlineData("a")]
    [InlineData("a.b.c")]
    [InlineData("dir.x\\a")]
    [InlineData("a.b c")]
    [InlineData("a b.c")]
    [InlineData("a.")]
    [InlineData(".bashrc")]
    [InlineData("C:\\x.y\\z")]
    [InlineData("C:/x.y/z")]
    [InlineData("a.txt ")]
    [InlineData("\"a b\".txt")]
    [InlineData("")]
    public void Extension_matches_PathFindExtensionW(string path) =>
        PathRules.FindExtension(path).Should().Be(Win32PathService.FindExtension(path));

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void Resolve_adds_txt_only_when_nothing_is_found_and_there_is_no_extension()
    {
        var paths = new Win32PathService();
        File.WriteAllText(Path.Combine(_dir, "plain"), "x");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");

        FileArgument.Resolve(Path.Combine(_dir, "plain"), paths).Should().Be(Path.Combine(_dir, "plain"));
        FileArgument.Resolve(Path.Combine(_dir, "notes"), paths).Should().Be(Path.Combine(_dir, "notes.txt"));
        FileArgument.Resolve(Path.Combine(_dir, "new"), paths).Should().Be(Path.Combine(_dir, "new.txt"));
        FileArgument.Resolve($"\"{Path.Combine(_dir, "a b")}\" trailing", paths).Should().Be(Path.Combine(_dir, "a b.txt"));
        FileArgument.Resolve(Path.Combine(_dir, "*"), paths).Should().Be(Path.Combine(_dir, "*"), "FindFirstFileW matches the wildcard");
        paths.GetFullPath(Path.Combine(_dir, "x", "..", "y.txt")).Should().Be(Path.Combine(_dir, "y.txt"));
    }

    [Fact]
    [Trait("Doc", "parity.md#dong-lenh")]
    public void The_raw_command_line_starts_with_this_program() =>
        Win32StartupInfo.CommandLine().Should().Contain(Path.GetFileNameWithoutExtension(Environment.ProcessPath!));
}
