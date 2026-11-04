using Microsoft.Win32.SafeHandles;
using NotepadNet.Core.Settings;
using NotepadNet.Platform.Interop;

namespace NotepadNet.Platform.Settings;

/// <summary>
/// The settings keys on real registry roots, read and written with <c>RegQueryValueExW</c> /
/// <c>RegSetValueExW</c> like the original's helpers. Only <see cref="SettingsLocation.User"/> is
/// ever created or written.
/// </summary>
public sealed class Win32SettingsRegistry : ISettingsRegistry, IDisposable
{
    public const string UserPath = @"Software\Notepad.NET";
    public const string MachineDefaultFontsPath = @"Software\Notepad.NET\DefaultFonts";
    public const string LegacyPath = @"Software\Microsoft\Notepad";

    /// <summary>The user key under the app's earlier name, Notepad Classic.</summary>
    public const string PreviousUserPath = @"Software\NotepadClassic";

    /// <summary>Environment variable naming a hive file (<c>RegLoadAppKey</c>) that replaces every
    /// root: end-to-end tests run the app against it so the real registry is never touched.</summary>
    public const string HiveVariable = "NOTEPADNET_SETTINGS_HIVE";

    /// <summary>Inside a hive file the HKLM key lives under this prefix, so that it is not a subkey
    /// of the user key (which would make the user key exist).</summary>
    public const string HiveMachinePrefix = @"Machine\";

    private readonly nint _userRoot;
    private readonly nint _machineRoot;
    private readonly string _machinePrefix;
    private readonly SafeRegistryHandle? _hive;

    private Win32SettingsRegistry(nint userRoot, nint machineRoot, string machinePrefix, SafeRegistryHandle? hive)
    {
        _userRoot = userRoot;
        _machineRoot = machineRoot;
        _machinePrefix = machinePrefix;
        _hive = hive;
    }

    /// <summary>HKCU and HKLM.</summary>
    public static Win32SettingsRegistry ForCurrentUser() => new(Advapi32.HKEY_CURRENT_USER, Advapi32.HKEY_LOCAL_MACHINE, "", null);

    /// <summary>The key path of a location under its root.</summary>
    public string PathOf(SettingsLocation location) => location switch
    {
        SettingsLocation.User => UserPath,
        SettingsLocation.MachineDefaultFonts => _machinePrefix + MachineDefaultFontsPath,
        _ => LegacyPath,
    };

    /// <summary>Every location under the root of an application hive file (created when missing).</summary>
    public static Win32SettingsRegistry ForHive(string hivePath)
    {
        var error = Advapi32.RegLoadAppKey(hivePath, out var hive, Advapi32.KEY_ALL_ACCESS, 0, 0);
        if (error != Advapi32.ERROR_SUCCESS)
        {
            hive.Dispose();
            throw new IOException($"RegLoadAppKey failed for '{hivePath}'.", unchecked((int)(0x80070000 | (uint)error)));
        }

        var root = hive.DangerousGetHandle();
        return new(root, root, HiveMachinePrefix, hive);
    }

    /// <summary>The hive named by <see cref="HiveVariable"/>, otherwise the real roots.</summary>
    public static Win32SettingsRegistry FromEnvironment()
    {
        var hive = Environment.GetEnvironmentVariable(HiveVariable);
        if (!string.IsNullOrEmpty(hive))
        {
            return ForHive(hive);
        }

        var registry = ForCurrentUser();
        registry.TakeOverPreviousUserKey();
        return registry;
    }

    /// <summary>
    /// The first run under this name copies every value of the key of the earlier name, with those the
    /// original Notepad does not have, such as Recent Files. The earlier key stays, for a copy of the old
    /// version. Value by value, since <c>RegCopyTree</c> is refused in an application hive; the key has no subkeys.
    /// </summary>
    /// <returns>Whether the key was copied.</returns>
    public bool TakeOverPreviousUserKey()
    {
        if (Advapi32.RegOpenKeyEx(_userRoot, UserPath, 0, Advapi32.KEY_READ, out var existing) == Advapi32.ERROR_SUCCESS)
        {
            _ = Advapi32.RegCloseKey(existing);
            return false;
        }

        if (Advapi32.RegOpenKeyEx(_userRoot, PreviousUserPath, 0, Advapi32.KEY_READ, out var previous) != Advapi32.ERROR_SUCCESS)
        {
            return false;
        }

        try
        {
            if (Advapi32.RegCreateKey(_userRoot, UserPath, out var current) != Advapi32.ERROR_SUCCESS)
            {
                return false;
            }

            var copied = CopyValues(previous, current);
            _ = Advapi32.RegCloseKey(current);
            if (!copied)
            {
                // An empty or partial key would stop the next run from trying again.
                _ = Advapi32.RegDeleteTree(_userRoot, UserPath);
            }

            return copied;
        }
        finally
        {
            _ = Advapi32.RegCloseKey(previous);
        }
    }

    private static unsafe bool CopyValues(nint from, nint to)
    {
        if (Advapi32.RegQueryInfoKey(from, 0, 0, 0, 0, 0, 0, out var count, out var longestName, out var longestData, 0, 0) != Advapi32.ERROR_SUCCESS)
        {
            return false;
        }

        var name = new char[longestName + 1];
        var data = new byte[longestData];
        fixed (char* namePointer = name)
        fixed (byte* dataPointer = data)
        {
            for (var i = 0; i < count; i++)
            {
                var nameLength = name.Length;
                var dataLength = data.Length;
                if (Advapi32.RegEnumValue(from, i, namePointer, ref nameLength, 0, out var type, dataPointer, ref dataLength) != Advapi32.ERROR_SUCCESS
                    || Advapi32.RegSetValueEx(to, new string(namePointer, 0, nameLength), 0, type, dataPointer, dataLength) != Advapi32.ERROR_SUCCESS)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public ISettingsKey? Open(SettingsLocation location)
    {
        var root = location == SettingsLocation.MachineDefaultFonts ? _machineRoot : _userRoot;
        return Advapi32.RegOpenKeyEx(root, PathOf(location), 0, Advapi32.KEY_READ, out var key) == Advapi32.ERROR_SUCCESS ? new Key(key) : null;
    }

    public ISettingsKey? CreateUser() =>
        Advapi32.RegCreateKey(_userRoot, UserPath, out var key) == Advapi32.ERROR_SUCCESS ? new Key(key) : null;

    public void Dispose() => _hive?.Dispose();

    private sealed unsafe class Key(nint handle) : ISettingsKey
    {
        private nint _handle = handle;

        public bool TryReadDword(string name, out int value)
        {
            int data = 0;
            var size = sizeof(int);
            var ok = Advapi32.RegQueryValueEx(_handle, name, 0, out var type, (byte*)&data, ref size) == Advapi32.ERROR_SUCCESS
                && type == Advapi32.REG_DWORD;
            value = ok ? data : 0;
            return ok;
        }

        public bool TryReadString(string name, int capacity, out string value)
        {
            var buffer = new char[capacity];
            var size = capacity * sizeof(char);
            int type;
            int error;
            fixed (char* p = buffer)
            {
                error = Advapi32.RegQueryValueEx(_handle, name, 0, out type, (byte*)p, ref size);
            }

            var ok = error == Advapi32.ERROR_SUCCESS && type == Advapi32.REG_SZ;
            value = ok ? new string(buffer, 0, size / sizeof(char)) : "";
            return ok;
        }

        public void WriteDword(string name, int value) =>
            _ = Advapi32.RegSetValueEx(_handle, name, 0, Advapi32.REG_DWORD, (byte*)&value, sizeof(int));

        public void WriteString(string name, string value)
        {
            fixed (char* p = value)
            {
                _ = Advapi32.RegSetValueEx(_handle, name, 0, Advapi32.REG_SZ, (byte*)p, (value.Length + 1) * sizeof(char));
            }
        }

        public void Dispose()
        {
            if (_handle != 0)
            {
                _ = Advapi32.RegCloseKey(_handle);
                _handle = 0;
            }
        }
    }
}
