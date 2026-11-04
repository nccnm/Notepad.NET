using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotepadNet.Platform.Interop;

internal static partial class Advapi32
{
    public static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001);
    public static readonly nint HKEY_LOCAL_MACHINE = unchecked((nint)0x80000002);
    public const int KEY_READ = 0x20019;
    public const int KEY_ALL_ACCESS = 0xF003F;
    public const int REG_SZ = 1;
    public const int REG_DWORD = 4;
    public const int ERROR_SUCCESS = 0;

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool IsTextUnicode(byte* lpv, int iSize, ref int lpiResult);

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegOpenKeyEx(nint hKey, string lpSubKey, int ulOptions, int samDesired, out nint phkResult);

    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegCreateKey(nint hKey, string lpSubKey, out nint phkResult);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW")]
    public static partial int RegQueryInfoKey(
        nint hKey, nint lpClass, nint lpcchClass, nint lpReserved, nint lpcSubKeys, nint lpcbMaxSubKeyLen, nint lpcbMaxClassLen,
        out int lpcValues, out int lpcbMaxValueNameLen, out int lpcbMaxValueLen, nint lpcbSecurityDescriptor, nint lpftLastWriteTime);

    [LibraryImport("advapi32.dll", EntryPoint = "RegEnumValueW")]
    public static unsafe partial int RegEnumValue(nint hKey, int dwIndex, char* lpValueName, ref int lpcchValueName, nint lpReserved, out int lpType, byte* lpData, ref int lpcbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteTreeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegDeleteTree(nint hKey, string? subKey);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int RegQueryValueEx(nint hKey, string lpValueName, nint lpReserved, out int lpType, byte* lpData, ref int lpcbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int RegSetValueEx(nint hKey, string lpValueName, int reserved, int dwType, byte* lpData, int cbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegLoadAppKeyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegLoadAppKey(string lpFile, out SafeRegistryHandle phkResult, int samDesired, int dwOptions, int reserved);

    [LibraryImport("advapi32.dll")]
    public static partial int RegCloseKey(nint hKey);
}
