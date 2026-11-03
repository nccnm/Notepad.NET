using NotepadClassic.Core.Documents;
using NotepadClassic.Core.IO;
using NotepadClassic.Core.Localization;
using NotepadClassic.Core.Services;
using NotepadClassic.Core.Text;

namespace NotepadClassic.Core.CommandLine;

/// <summary>Printing a file named on the command line (<c>/P</c>, <c>/PT</c>).</summary>
/// <param name="PrinterName">Null for the default printer.</param>
public sealed record PrintRequest(string? PrinterName);

/// <summary>What the startup sequence needs from the shell around the document.</summary>
public interface IStartupHost
{
    /// <summary><c>/.SETUP</c>: <c>SlipUpAcc</c> keys, window style <c>0x00FD0000</c>, minimize disabled.</summary>
    void EnterSetupMode();

    /// <summary>Posts <c>WM_CLOSE</c> (after a command-line print).</summary>
    void RequestClose();

    /// <summary>Prints the loaded document (<c>PrintDocument(1 or 2)</c>).</summary>
    void Print(PrintRequest request);

    /// <summary><c>RestartByRestartManager:</c> with a 36-character id: restores the autosaved document.</summary>
    void Restore(string restartId);

    /// <summary>A start without arguments: takes the next note that a closed window left behind.</summary>
    void RestoreClosedNote();
}

public enum StartupResult
{
    /// <summary>Run the message loop.</summary>
    Run,

    /// <summary><c>InitApplication</c> returned 0: Cancel in the "create the file?" question.</summary>
    Exit,
}

/// <summary>
/// Step 18 of <c>InitApplication</c>: runs after the window is shown, so the
/// user sees an untitled window while a large file loads. Error boxes follow the original's
/// mapping for each path.
/// </summary>
public sealed class StartupCoordinator(
    DocumentService documents, IFileSystem fileSystem, IPathService paths, IDialogService dialogs, ILocalizedStrings strings, IStartupHost host)
{
    public StartupResult Run(ParsedCommandLine command)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command.Action)
        {
            case StartupAction.Open:
                return OpenFile(FileArgument.Resolve(command.FileArgument, paths), command.ForcedEncoding);
            case StartupAction.Setup:
                host.EnterSetupMode();
                if (command.FileArgument.Length > 0)
                {
                    OpenForSetup(FileArgument.Resolve(command.FileArgument, paths), command.ForcedEncoding);
                }

                return StartupResult.Run;
            case StartupAction.Print:
            case StartupAction.PrintTo:
                PrintFile(FileArgument.Resolve(command.FileArgument, paths), command);
                host.RequestClose();
                return StartupResult.Run;
            case StartupAction.Restart:
                if (command.RestartId is not null)
                {
                    host.Restore(command.RestartId);
                }

                return StartupResult.Run;
            case StartupAction.None:
                host.RestoreClosedNote();
                return StartupResult.Run;
            default:
                // NewWindow, and InvalidPrint: deliberate difference (parity.md), the original
                // retries the whole "/P..." line as a file name.
                return StartupResult.Run;
        }
    }

    /// <summary>
    /// The normal path: a missing file asks string 2 with Yes/No/Cancel (Yes creates it, No leaves
    /// an empty document, Cancel ends the program); any other open error shows the file error.
    /// </summary>
    private StartupResult OpenFile(string path, TextEncodingKind forced)
    {
        var outcome = documents.Load(path, forced);
        if (outcome != LoadOutcome.OpenFailed)
        {
            return StartupResult.Run;
        }

        if (documents.LastOpenError != FileErrors.FileNotFound)
        {
            documents.ShowFileError(path, documents.LastOpenError);
            return StartupResult.Run;
        }

        switch (Ask(StringKeys.CannotFindCreate, path, MessageKind.WarningYesNoCancel))
        {
            case MessageResult.Cancel:
                return StartupResult.Exit;
            case MessageResult.Yes:
                CreateAndLoad(path, forced);
                break;
        }

        return StartupResult.Run;
    }

    /// <summary>
    /// <c>HandleSetupArg</c>: like the normal path, but Cancel in the create question does not end
    /// the program, and other errors use an OK/Cancel box (5 -> string 26, 123 -> string 13,
    /// otherwise string 1). Deliberate difference (parity.md): when the file cannot be opened the
    /// original goes on to open the whole "/.SETUP file" text as a file name; the port keeps the
    /// empty document.
    /// </summary>
    private void OpenForSetup(string path, TextEncodingKind forced)
    {
        var outcome = documents.Load(path, forced);
        if (outcome != LoadOutcome.OpenFailed)
        {
            return;
        }

        var error = documents.LastOpenError;
        if (error == FileErrors.FileNotFound)
        {
            if (Ask(StringKeys.CannotFindCreate, path, MessageKind.WarningYesNoCancel) == MessageResult.Yes)
            {
                CreateAndLoad(path, forced);
            }

            return;
        }

        var key = error switch
        {
            FileErrors.AccessDenied => StringKeys.AccessDenied,
            FileErrors.InvalidName => StringKeys.InvalidFileName,
            _ => StringKeys.CannotOpenFile,
        };
        Ask(key, path, MessageKind.WarningOkCancel);
    }

    /// <summary>
    /// <c>HandlePrintArgs</c>: an open error shows an OK box (2 -> string 2, 5 or 65 -> string 26,
    /// 123 -> string 13, otherwise string 1). Deliberate difference (parity.md): the original also
    /// prints when the load fails after the file opened (an empty page); the port prints only a
    /// loaded document.
    /// </summary>
    private void PrintFile(string path, ParsedCommandLine command)
    {
        var outcome = documents.Load(path, command.ForcedEncoding);
        if (outcome == LoadOutcome.Loaded)
        {
            host.Print(new PrintRequest(command.Action == StartupAction.PrintTo ? command.PrinterName : null));
            return;
        }

        if (outcome != LoadOutcome.OpenFailed)
        {
            return;
        }

        var key = documents.LastOpenError switch
        {
            FileErrors.FileNotFound => StringKeys.CannotFindCreate,
            FileErrors.AccessDenied or FileErrors.NetworkAccessDenied => StringKeys.AccessDenied,
            FileErrors.InvalidName => StringKeys.InvalidFileName,
            _ => StringKeys.CannotOpenFile,
        };
        Ask(key, path, MessageKind.Warning);
    }

    /// <summary><c>CreateFileW(GENERIC_READ | GENERIC_WRITE, OPEN_ALWAYS)</c>, then the load.</summary>
    private void CreateAndLoad(string path, TextEncodingKind forced)
    {
        try
        {
            fileSystem.OpenForSave(path, shareWrite: true).Dispose();
        }
        catch (IOException e)
        {
            documents.ShowFileError(path, FileErrors.Win32ErrorOf(e));
            return;
        }

        if (documents.Load(path, forced) == LoadOutcome.OpenFailed)
        {
            documents.ShowFileError(path, documents.LastOpenError);
        }
    }

    private MessageResult Ask(string key, string path, MessageKind kind) =>
        dialogs.ShowMessage(MessageText.InsertArgument(strings.GetString(key), path), kind);
}
