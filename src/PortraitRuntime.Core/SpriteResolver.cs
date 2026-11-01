namespace PortraitRuntime.Core;

public static class SpriteResolver
{
    /// <summary>
    /// Choose the sprite name a debug preview should display.
    /// Returns the first candidate the game actually has. A name that was asked for but is absent is
    /// reported as <c>native-key-missing</c> so the caller can mirror the native random fallback
    /// instead of silently substituting an unrelated portrait (the historical mentor-template bug).
    /// </summary>
    public static bool TryPick(string requested, IEnumerable<string> candidates, Func<string, bool> known, out string sprite, out string status)
    {
        sprite = null;
        status = "unresolved";
        string missing = null;
        if (requested != null)
        {
            if (known(requested)) { sprite = requested; status = "exact"; return true; }
            missing = requested;
        }
        foreach (var candidate in candidates)
        {
            if (candidate == null) continue;
            if (known(candidate)) { sprite = candidate; status = "exact"; return true; }
            missing ??= candidate;
        }
        status = missing != null ? "native-key-missing" : "unresolved";
        return false;
    }
}
