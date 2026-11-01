using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PortraitRuntime.Unity;

public static class SpecialDisplay
{
    public sealed class Target
    {
        public int Scene { get; set; }
        public string Root { get; set; }
        public int[] Children { get; set; }
        public string Name { get; set; }
        public string Key { get; set; }
        public string Component { get; set; }
    }
    sealed class Binding { public UnityEngine.Object Target; public Sprite Before; public Sprite Applied; }
    static readonly List<Binding> bindings = new();
    public static void Apply(Scene scene)
    {
        var r = RuntimeMod.Current;
        if (r == null || !scene.IsValid() || !scene.isLoaded) return;
        var path = Path.Combine(r.Root, "catalogs", "special_targets.json");
        if (!File.Exists(path)) return;
        var targets = JsonConvert.DeserializeObject<Target[]>(File.ReadAllText(path));
        foreach (var t in targets.Where(x => x.Scene == scene.buildIndex))
        {
            if (!r.Catalog.Specials.TryGetValue(t.Key, out var entry)) continue;
            var roots = scene.GetRootGameObjects().Where(x => x.name == t.Root).ToArray();
            if (roots.Length != 1) { RuntimeMod.Log("Special root not unique: " + t.Root); continue; }
            var transform = roots[0].transform;
            foreach (var i in t.Children) { if (i >= transform.childCount) { transform = null; break; } transform = transform.GetChild(i); }
            if (transform == null || transform.name != t.Name) { RuntimeMod.Log("Special target changed: " + t.Name); continue; }
            var renderer = t.Component == "SpriteRenderer" ? transform.GetComponent<SpriteRenderer>() : null;
            var image = t.Component == "Image" ? transform.GetComponent<Image>() : null;
            var before = renderer != null ? renderer.sprite : image?.sprite;
            if (before == null || before.name != t.Key) continue;
            var owner = renderer != null ? (UnityEngine.Object)renderer : image;
            var after = r.Assets.Get(entry.Pack, entry.Value, before, owner);
            if (renderer != null) renderer.sprite = after; else image.sprite = after;
            bindings.Add(new Binding { Target = owner, Before = before, Applied = after });
        }
    }
    public static void Restore()
    {
        foreach (var b in bindings)
        {
            if (b.Target is SpriteRenderer r && r != null && r.sprite == b.Applied) r.sprite = b.Before;
            if (b.Target is Image i && i != null && i.sprite == b.Applied) i.sprite = b.Before;
            RuntimeMod.Current?.Assets.Release(b.Target);
        }
        bindings.Clear();
    }
}
