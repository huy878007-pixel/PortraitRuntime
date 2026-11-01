using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

var managed = Environment.GetEnvironmentVariable("PORTRAIT_GAME_MANAGED") ?? @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var file = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
};
return Contract.Run(args, managed);

static class Contract
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args, string managed)
    {
        var checks = new List<object>(); bool ok = true;
        void Method(Type type, string name, Type[] parameters = null)
        {
            var method = parameters == null ? HarmonyLib.AccessTools.Method(type, name) : HarmonyLib.AccessTools.Method(type, name, parameters);
            checks.Add(new { target = type.Name + "." + name, found = method != null }); ok &= method != null;
        }
        Method(typeof(PlayerStore), "GetNextClient"); Method(typeof(StoreUIManager), "OnNextClientArrived");
        Method(typeof(StoreClientMono), "OnLeft"); Method(typeof(DialogUIManager), "DisplayClientText", new[] { typeof(Dialogue) });
        Method(typeof(Dialogue), "SetText", new[] { typeof(string), typeof(string) });
        Method(typeof(GameItemElement), "ResolveItemSprite"); Method(typeof(RenderHandler), "LoadFromFile"); Method(typeof(SpriteDict), "GetSprite");
        Method(typeof(DialogUIManager), "Start"); Method(typeof(DialogUIManager), "SetNormalMode"); Method(typeof(DialogUIManager), "SetShootingMode");
        Method(typeof(ModHelper), "SetModData"); Method(typeof(ModHelper), "GetModData");
        Method(typeof(GeneralHelper), "SetItemOwned");
        Method(typeof(StoreUIManager), "RunMorningDialogue");
        Method(typeof(LocHelper), "Get");
        Method(typeof(Dialogue), "SetText", new[] { typeof(string), typeof(RichTextBuilder) });
        Method(typeof(Dialogue), "SetDummy"); Method(typeof(Dialogue), "SetNextDialogue");
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("dist/player.portraitruntime");
        var allowed = System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).Concat(new[] { "PortraitRuntime.Core", "PortraitRuntime.Unity" }).ToArray();
        var scan = ModScanner.Scan(new[] { Path.Combine(root, "PortraitRuntime.Core.dll"), Path.Combine(root, "PortraitRuntime.Unity.dll") }, allowed, root, true);
        var result = new { passed = ok && !scan.Refused, runtimeExecuted = false, checks,
            notExecuted = new[] { "Native dialogue construction requires RenderHandler.current.windowUICanvas in a running Unity scene." },
            scanner = new { scan.Refused, scan.MethodsScanned, scan.Error, scan.Capabilities, scan.HardFlags, findings = scan.FormatFindings(40) } };
        string json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
        Console.WriteLine(json);
        System.IO.Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/native-contracts.json", json);
        return ok && !scan.Refused ? 0 : 1;
    }
}
