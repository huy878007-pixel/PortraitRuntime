namespace PortraitRuntime.Core;

public static class ContentSources
{
    static readonly Dictionary<string, string> sources = new(StringComparer.Ordinal);
    public static event Action Changed;
    public static IReadOnlyDictionary<string, string> Snapshot => new Dictionary<string, string>(sources, StringComparer.Ordinal);
    public static void Register(string modId, string packsDirectory)
    {
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Content owner is required");
        var path = Path.GetFullPath(packsDirectory);
        if (!System.IO.Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        if (sources.TryGetValue(modId, out var prior) && string.Equals(path, prior, StringComparison.OrdinalIgnoreCase)) return;
        sources[modId] = path;
        Changed?.Invoke();
    }
    public static bool Unregister(string modId)
    {
        if (!sources.Remove(modId)) return false;
        Changed?.Invoke(); return true;
    }
}
