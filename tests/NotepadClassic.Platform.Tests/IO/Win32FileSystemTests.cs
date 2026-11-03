using NotepadClassic.Core.IO;
using NotepadClassic.Platform.IO;

namespace NotepadClassic.Platform.Tests.IO;

/// <summary>Save in place: OPEN_ALWAYS, overwrite in place,
/// SetEndOfFile, share modes, ERROR_ALREADY_EXISTS.</summary>
public sealed class Win32FileSystemTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("npport-fs-").FullName;
    private readonly Win32FileSystem _fs = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_stamp_follows_writes_and_is_null_for_a_missing_file()
    {
        var path = PathOf("s.txt");
        _fs.GetStamp(path).Should().BeNull();

        System.IO.File.WriteAllText(path, "abc");
        var first = _fs.GetStamp(path);
        first.Should().NotBeNull();
        first!.Value.Length.Should().Be(3);
        _fs.GetStamp(path).Should().Be(first, "nothing was written in between");

        System.IO.File.WriteAllText(path, "abcd");
        _fs.GetStamp(path).Should().NotBe(first);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_stamp_sees_a_write_of_the_same_length()
    {
        var path = PathOf("t.txt");
        System.IO.File.WriteAllText(path, "abc");
        System.IO.File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var first = _fs.GetStamp(path);

        System.IO.File.WriteAllText(path, "xyz");

        _fs.GetStamp(path).Should().NotBe(first);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_stamp_works_while_another_program_holds_the_file_without_sharing_and_sees_its_writes()
    {
        var path = PathOf("held.txt");
        System.IO.File.WriteAllText(path, "abc");
        var before = _fs.GetStamp(path);

        using var holder = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        holder.Seek(0, SeekOrigin.End);
        holder.WriteByte((byte)'d');
        holder.Flush(flushToDisk: true);

        _fs.GetStamp(path).Should().NotBeNull().And.NotBe(before);
    }

    private static void WaitForSignal(SemaphoreSlim signal, string what) =>
        signal.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).Should().BeTrue($"{what} should be noticed");

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_watcher_sees_writes_replacement_and_deletion_of_its_file_but_not_of_others()
    {
        var path = PathOf("watched.txt");
        System.IO.File.WriteAllText(path, "a");
        using var signal = new SemaphoreSlim(0);
        using var watch = new Win32FileWatcher().Watch(path, () => signal.Release());
        watch.Should().NotBeNull();

        System.IO.File.WriteAllText(PathOf("other.txt"), "x");
        signal.Wait(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken).Should().BeFalse("another file is not this one");

        System.IO.File.WriteAllText(path, "ab");
        WaitForSignal(signal, "a write");
        Thread.Sleep(100);
        while (signal.Wait(0, TestContext.Current.CancellationToken))
        {
        }

        System.IO.File.WriteAllText(PathOf("new.tmp"), "abc");
        System.IO.File.Move(PathOf("new.tmp"), path, overwrite: true);
        WaitForSignal(signal, "a replacement");
        Thread.Sleep(100);
        while (signal.Wait(0, TestContext.Current.CancellationToken))
        {
        }

        System.IO.File.Delete(path);
        WaitForSignal(signal, "a deletion");
    }

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_disposed_watcher_is_silent_and_leaves_the_folder_free()
    {
        var folder = Directory.CreateDirectory(PathOf("sub")).FullName;
        var path = Path.Combine(folder, "w.txt");
        System.IO.File.WriteAllText(path, "a");
        using var signal = new SemaphoreSlim(0);

        new Win32FileWatcher().Watch(path, () => signal.Release())!.Dispose();
        System.IO.File.WriteAllText(path, "ab");

        signal.Wait(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken).Should().BeFalse();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_folder_that_cannot_be_watched_gives_no_watcher()
    {
        new Win32FileWatcher().Watch(PathOf(@"missing\x.txt"), () => { }).Should().BeNull();
        new Win32FileWatcher().Watch("x.txt", () => { }).Should().BeNull();
    }

    [Fact]
    [Trait("Doc", "parity.md#file-doi-ben-ngoai")]
    public void A_directory_has_no_stamp() => _fs.GetStamp(_dir).Should().BeNull();

    [Fact]
    public void Reads_whole_file_through_mapping_and_by_offset()
    {
        System.IO.File.WriteAllBytes(PathOf("a.txt"), [1, 2, 3, 4, 5]);
        using var file = _fs.OpenRead(PathOf("a.txt"));
        var two = new byte[2];

        file.Length.Should().Be(5);
        file.GetAllBytes().ToArray().Should().Equal(1, 2, 3, 4, 5);
        file.GetAllBytes().Length.Should().Be(5, "the mapping is reused");
        file.Read(3, two).Should().Be(2);
        two.Should().Equal(4, 5);
    }

    [Fact]
    public void Empty_file_maps_to_empty_span()
    {
        System.IO.File.WriteAllBytes(PathOf("e.txt"), []);
        using var file = _fs.OpenRead(PathOf("e.txt"));

        file.GetAllBytes().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Reading_shares_read_and_write()
    {
        System.IO.File.WriteAllBytes(PathOf("s.txt"), [1]);
        using var writer = new FileStream(PathOf("s.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        using var file = _fs.OpenRead(PathOf("s.txt"));

        file.Length.Should().Be(1);
    }

    [Fact]
    public void Missing_file_reports_win32_error_2()
    {
        var act = () => _fs.OpenRead(PathOf("missing.txt"));

        FileErrors.Win32ErrorOf(act.Should().Throw<IOException>().Which).Should().Be(FileErrors.FileNotFound);
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Save_reports_whether_the_file_already_existed()
    {
        using (var created = _fs.OpenForSave(PathOf("n.txt"), shareWrite: false))
        {
            created.AlreadyExisted.Should().BeFalse();
        }

        using var existing = _fs.OpenForSave(PathOf("n.txt"), shareWrite: false);
        existing.AlreadyExisted.Should().BeTrue();
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Save_overwrites_in_place_and_only_truncates_at_SetEndOfFile()
    {
        System.IO.File.WriteAllBytes(PathOf("p.txt"), "0123456789"u8.ToArray());

        using (var file = _fs.OpenForSave(PathOf("p.txt"), shareWrite: false))
        {
            file.Write("ab"u8);
            file.Write("c"u8);
        }

        System.IO.File.ReadAllBytes(PathOf("p.txt")).Should().Equal("abc3456789"u8.ToArray(), "nothing is truncated without SetEndOfFile");

        using (var file = _fs.OpenForSave(PathOf("p.txt"), shareWrite: false))
        {
            file.Write("xy"u8);
            file.SetEndOfFile();
        }

        System.IO.File.ReadAllBytes(PathOf("p.txt")).Should().Equal("xy"u8.ToArray());
    }

    [Fact]
    [Trait("Doc", "parity.md#luu-file")]
    public void Save_keeps_the_file_identity()
    {
        var path = PathOf("id.txt");
        System.IO.File.WriteAllBytes(path, [1, 2, 3]);
        var link = PathOf("hardlink.txt");
        CreateHardLink(link, path);

        using (var file = _fs.OpenForSave(path, shareWrite: false))
        {
            file.Write([9]);
            file.SetEndOfFile();
        }

        System.IO.File.ReadAllBytes(link).Should().Equal(9);
    }

    [Theory]
    [Trait("Doc", "parity.md#luu-file")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Save_shares_write_only_for_save_as(bool shareWrite, bool expectSharingViolation)
    {
        var path = PathOf("share.txt");
        System.IO.File.WriteAllBytes(path, [1]);
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var act = () => _fs.OpenForSave(path, shareWrite).Dispose();

        if (expectSharingViolation)
        {
            FileErrors.Win32ErrorOf(act.Should().Throw<IOException>().Which).Should().Be(FileErrors.SharingViolation);
        }
        else
        {
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Exists_is_true_for_files_and_directories_like_PathFileExists()
    {
        System.IO.File.WriteAllBytes(PathOf("x.txt"), []);

        _fs.Exists(PathOf("x.txt")).Should().BeTrue();
        _fs.Exists(_dir).Should().BeTrue();
        _fs.Exists(PathOf("nope")).Should().BeFalse();
    }

    [Fact]
    public void Delete_removes_and_reports_errors()
    {
        System.IO.File.WriteAllBytes(PathOf("d.txt"), []);

        _fs.Delete(PathOf("d.txt"));

        System.IO.File.Exists(PathOf("d.txt")).Should().BeFalse();
        var act = () => _fs.Delete(PathOf("d.txt"));
        FileErrors.Win32ErrorOf(act.Should().Throw<IOException>().Which).Should().Be(FileErrors.FileNotFound);
    }

    private static void CreateHardLink(string link, string target)
    {
        if (!NativeMethods.CreateHardLinkW(link, target, 0))
        {
            throw new IOException("CreateHardLink failed", System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }
    }
}

internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, nint lpSecurityAttributes);
}
