using NotepadNet.Core.Settings;

namespace NotepadNet.Core.Tests.Fakes;

/// <summary>Registry values in memory, with the kind and raw size the Win32 calls would see.</summary>
internal sealed class MemorySettingsRegistry : ISettingsRegistry
{
    public sealed record Value(int Kind, object Data);

    public const int Sz = 1;
    public const int Binary = 3;
    public const int Dword = 4;

    public Dictionary<SettingsLocation, Dictionary<string, Value>> Keys { get; } = [];

    /// <summary>When false, creating the user key fails (no rights).</summary>
    public bool CanCreate { get; set; } = true;

    public int Writes { get; private set; }

    public Dictionary<string, Value> Key(SettingsLocation location)
    {
        if (!Keys.TryGetValue(location, out var key))
        {
            key = [];
            Keys[location] = key;
        }

        return key;
    }

    public Dictionary<string, Value>? User => Keys.GetValueOrDefault(SettingsLocation.User);

    public void SetDword(SettingsLocation location, string name, int value) => Key(location)[name] = new(Dword, value);

    public void SetString(SettingsLocation location, string name, string value) => Key(location)[name] = new(Sz, value);

    public ISettingsKey? Open(SettingsLocation location) =>
        Keys.TryGetValue(location, out var key) ? new MemoryKey(this, key, writable: false) : null;

    public ISettingsKey? CreateUser() => CanCreate ? new MemoryKey(this, Key(SettingsLocation.User), writable: true) : null;

    private sealed class MemoryKey(MemorySettingsRegistry owner, Dictionary<string, Value> values, bool writable) : ISettingsKey
    {
        public bool TryReadDword(string name, out int value)
        {
            value = 0;
            if (!values.TryGetValue(name, out var v) || v.Kind != Dword || v.Data is not int i)
            {
                return false;
            }

            value = i;
            return true;
        }

        public bool TryReadString(string name, int capacity, out string value)
        {
            value = "";
            // The data must fit in the buffer, NUL included when the registry has one.
            if (!values.TryGetValue(name, out var v) || v.Kind != Sz || v.Data is not string s || s.Length > capacity)
            {
                return false;
            }

            value = s;
            return true;
        }

        public void WriteDword(string name, int value)
        {
            Assert.True(writable);
            owner.Writes++;
            values[name] = new(Dword, value);
        }

        public void WriteString(string name, string value)
        {
            Assert.True(writable);
            owner.Writes++;
            values[name] = new(Sz, value);
        }

        public void Dispose()
        {
        }
    }
}
