namespace PortraitRuntime.Core;

public sealed class DebugCase
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string RegistrationId { get; set; }
    public string Identity { get; set; }
    public string Factory { get; set; }
    public string SpriteKey { get; set; }
    public string[] SpriteExpressions { get; set; } = Array.Empty<string>();
    public string DialogueKey { get; set; }
    public string Text { get; set; }
    public string PackId { get; set; }
    public string CharacterId { get; set; }
}
public sealed class DebugTourPlan
{
    public IReadOnlyList<DebugCase> Cases { get; private set; } = Array.Empty<DebugCase>();
    public int Index { get; private set; } = -1;
    public bool Active { get; private set; }
    public DebugCase Current => Index >= 0 && Index < Cases.Count ? Cases[Index] : null;
    public void Start(IEnumerable<DebugCase> cases)
    {
        var list = cases.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)).GroupBy(x => x.Id, StringComparer.Ordinal).Select(x => x.First()).ToArray();
        if (list.Length == 0 || list.Length > 5000) throw new ArgumentException("Tour needs 1..5000 entries");
        Cases = list; Index = -1; Active = true;
    }
    public DebugCase Next()
    {
        if (!Active) return null;
        Index++;
        if (Index >= Cases.Count) { Active = false; return null; }
        return Cases[Index];
    }
    public void Seek(int index)
    {
        if (index < 0 || index >= Cases.Count) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index - 1; Active = true;
    }
    public void Stop() { Active = false; }
}
