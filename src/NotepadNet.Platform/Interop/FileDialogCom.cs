using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NotepadNet.Platform.Interop;

// Shell file dialog interfaces (shobjidl_core.h). Methods keep the SDK vtable order; the ones
// the port never calls take raw pointers. Every method returns its HRESULT (PreserveSig) so
// callers decide what a failure means, as the original does.

[StructLayout(LayoutKind.Sequential)]
internal struct COMDLG_FILTERSPEC
{
    public nint pszName;
    public nint pszSpec;
}

internal static class FileDialogIds
{
    public static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    public static readonly Guid CLSID_FileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
    public static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public const uint FOS_FORCEFILESYSTEM = 0x40;
    public const uint SIGDN_FILESYSPATH = 0x8005_8000;
    public const int ERROR_CANCELLED_HRESULT = unchecked((int)0x800704C7);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    [PreserveSig]
    int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

    [PreserveSig]
    int GetParent(out nint ppsi);

    [PreserveSig]
    int GetDisplayName(uint sigdnName, out nint ppszName);

    [PreserveSig]
    int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    [PreserveSig]
    int Compare(nint psi, uint hint, out int piOrder);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("B4DB1657-70D7-485E-8E3E-6FCB5A5C1802")]
internal partial interface IModalWindow
{
    [PreserveSig]
    int Show(nint hwndOwner);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
internal unsafe partial interface IFileDialog : IModalWindow
{
    [PreserveSig]
    int SetFileTypes(uint cFileTypes, COMDLG_FILTERSPEC* rgFilterSpec);

    [PreserveSig]
    int SetFileTypeIndex(uint iFileType);

    [PreserveSig]
    int GetFileTypeIndex(out uint piFileType);

    [PreserveSig]
    int Advise(nint pfde, out uint pdwCookie);

    [PreserveSig]
    int Unadvise(uint dwCookie);

    [PreserveSig]
    int SetOptions(uint fos);

    [PreserveSig]
    int GetOptions(out uint pfos);

    [PreserveSig]
    int SetDefaultFolder(nint psi);

    [PreserveSig]
    int SetFolder(nint psi);

    [PreserveSig]
    int GetFolder(out nint ppsi);

    [PreserveSig]
    int GetCurrentSelection(out nint ppsi);

    [PreserveSig]
    int SetFileName(string pszName);

    [PreserveSig]
    int GetFileName(out nint pszName);

    [PreserveSig]
    int SetTitle(string pszTitle);

    [PreserveSig]
    int SetOkButtonLabel(string pszText);

    [PreserveSig]
    int SetFileNameLabel(string pszLabel);

    [PreserveSig]
    int GetResult(out IShellItem? ppsi);

    [PreserveSig]
    int AddPlace(nint psi, int fdap);

    [PreserveSig]
    int SetDefaultExtension(string pszDefaultExtension);

    [PreserveSig]
    int Close(int hr);

    [PreserveSig]
    int SetClientGuid(in Guid guid);

    [PreserveSig]
    int ClearClientData();

    [PreserveSig]
    int SetFilter(nint pFilter);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
internal unsafe partial interface IFileOpenDialog : IFileDialog
{
    [PreserveSig]
    int GetResults(out nint ppenum);

    [PreserveSig]
    int GetSelectedItems(out nint ppsai);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
internal unsafe partial interface IFileSaveDialog : IFileDialog
{
    [PreserveSig]
    int SetSaveAsItem(IShellItem psi);

    [PreserveSig]
    int SetProperties(nint pStore);

    [PreserveSig]
    int SetCollectedProperties(nint pList, int fAppendDefault);

    [PreserveSig]
    int GetProperties(out nint ppStore);

    [PreserveSig]
    int ApplyProperties(nint psi, nint pStore, nint hwnd, nint pSink);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("E6FDD21A-163F-4975-9C8C-A69F1BA37034")]
internal partial interface IFileDialogCustomize
{
    [PreserveSig]
    int EnableOpenDropDown(uint dwIDCtl);

    [PreserveSig]
    int AddMenu(uint dwIDCtl, string pszLabel);

    [PreserveSig]
    int AddPushButton(uint dwIDCtl, string pszLabel);

    [PreserveSig]
    int AddComboBox(uint dwIDCtl);

    [PreserveSig]
    int AddRadioButtonList(uint dwIDCtl);

    [PreserveSig]
    int AddCheckButton(uint dwIDCtl, string pszLabel, int bChecked);

    [PreserveSig]
    int AddEditBox(uint dwIDCtl, string pszText);

    [PreserveSig]
    int AddSeparator(uint dwIDCtl);

    [PreserveSig]
    int AddText(uint dwIDCtl, string pszText);

    [PreserveSig]
    int SetControlLabel(uint dwIDCtl, string pszLabel);

    [PreserveSig]
    int GetControlState(uint dwIDCtl, out int pdwState);

    [PreserveSig]
    int SetControlState(uint dwIDCtl, int dwState);

    [PreserveSig]
    int GetEditBoxText(uint dwIDCtl, out nint ppszText);

    [PreserveSig]
    int SetEditBoxText(uint dwIDCtl, string pszText);

    [PreserveSig]
    int GetCheckButtonState(uint dwIDCtl, out int pbChecked);

    [PreserveSig]
    int SetCheckButtonState(uint dwIDCtl, int bChecked);

    [PreserveSig]
    int AddControlItem(uint dwIDCtl, uint dwIDItem, string pszLabel);

    [PreserveSig]
    int RemoveControlItem(uint dwIDCtl, uint dwIDItem);

    [PreserveSig]
    int RemoveAllControlItems(uint dwIDCtl);

    [PreserveSig]
    int GetControlItemState(uint dwIDCtl, uint dwIDItem, out int pdwState);

    [PreserveSig]
    int SetControlItemState(uint dwIDCtl, uint dwIDItem, int dwState);

    [PreserveSig]
    int GetSelectedControlItem(uint dwIDCtl, out uint pdwIDItem);

    [PreserveSig]
    int SetSelectedControlItem(uint dwIDCtl, uint dwIDItem);

    [PreserveSig]
    int StartVisualGroup(uint dwIDCtl, string pszLabel);

    [PreserveSig]
    int EndVisualGroup();

    [PreserveSig]
    int MakeProminent(uint dwIDCtl);

    [PreserveSig]
    int SetControlItemText(uint dwIDCtl, uint dwIDItem, string pszLabel);
}
