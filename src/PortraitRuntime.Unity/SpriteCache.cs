using System.Runtime.CompilerServices;
using PortraitRuntime.Core;
using UnityEngine;
using UObj = UnityEngine.Object;

namespace PortraitRuntime.Unity;

public sealed class SpriteCache
{
    sealed class Owner { public string Key; }
    sealed class Cached
    {
        public string Key; public Texture2D Texture; public Sprite Sprite; public long Bytes; public long Touch; public bool Permanent;
        public readonly List<WeakReference<object>> Owners = new();
    }
    readonly Dictionary<string, Cached> cache = new(StringComparer.Ordinal);
    readonly ConditionalWeakTable<object, Owner> bindings = new();
    long tick;
    public long Budget { get; set; } = 256 * 1024L * 1024;
    public long Bytes => cache.Values.Sum(x => x.Bytes);
    public int Count => cache.Count;
    public Sprite Get(Pack pack, string path, Sprite template, object owner = null, Vector2? logicalSize = null)
    {
        if (path == null || template == null) return template;
        string key = pack.Id + "/" + path + "|" + template.rect + "|" + template.pivot + "|" + template.pixelsPerUnit + "|" + logicalSize;
        if (!cache.TryGetValue(key, out var value))
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(PackReader.AssetPath(pack, path)))) throw new InvalidDataException("Cannot decode " + path);
                float logicalWidth = logicalSize?.x ?? template.rect.width, logicalHeight = logicalSize?.y ?? template.rect.height;
                float scaleX = texture.width / logicalWidth, scaleY = texture.height / logicalHeight;
                if (Math.Abs(scaleX - scaleY) > 0.015f) throw new InvalidDataException("Aspect ratio differs from original: " + path);
                texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
                var pivot = logicalSize.HasValue ? new Vector2(.5f, .5f) : new Vector2(template.pivot.x / template.rect.width, template.pivot.y / template.rect.height);
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, template.pixelsPerUnit * scaleX);
                sprite.name = "PortraitRuntime/" + pack.Id + "/" + path;
                UObj.DontDestroyOnLoad(texture); UObj.DontDestroyOnLoad(sprite);
                value = new Cached { Key = key, Texture = texture, Sprite = sprite, Bytes = texture.width * (long)texture.height * 4 };
                cache.Add(key, value);
            }
            catch { UObj.Destroy(texture); throw; }
        }
        value.Touch = ++tick;
        if (owner == null) value.Permanent = true;
        else
        {
            var binding = bindings.GetOrCreateValue(owner);
            if (binding.Key != key) { binding.Key = key; value.Owners.Add(new WeakReference<object>(owner)); }
        }
        Trim();
        return value.Sprite;
    }
    public void Release(object owner) { if (owner != null) bindings.Remove(owner); }
    public void RestoreItemOwners()
    {
        foreach (var c in cache.Values)
            foreach (var weak in c.Owners.ToArray())
                if (weak.TryGetTarget(out var owner) && owner is GameItemElement item)
                { RuntimeMod.Guard("restore item image", item.ValidatePartialSprite); Release(item); }
    }
    bool Bound(Cached c)
    {
        c.Owners.RemoveAll(w => !w.TryGetTarget(out var o) || (o is UObj u && u == null) || !bindings.TryGetValue(o, out var b) || b.Key != c.Key);
        return c.Permanent || c.Owners.Count != 0;
    }
    public void Trim(bool allUnbound = false)
    {
        foreach (var c in cache.Values.OrderBy(x => x.Touch).ToArray())
        {
            if (!allUnbound && Bytes <= Budget) break;
            if (Bound(c)) continue;
            UObj.Destroy(c.Sprite); UObj.Destroy(c.Texture); cache.Remove(c.Key);
        }
    }
}
