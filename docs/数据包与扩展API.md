# 数据包格式与扩展 API

格式 `portrait-pack/1`；每个包为独立文件夹，内含 `pack.json` 和图片。放入运行时或已注册内容 Mod 的 `packs/` 后重启游戏。运行时版本 0.2.2。

独立内容 Mod 声明对 `player.portraitruntime` 的 Prerequisites，根目录放 `portrait-content.marker`，数据包放固定 `packs/` 子目录。核心读取原生 ModLoader 的启用状态，在载入和定期检查时注册或移除内容，统一校验依赖和优先级。内容 Mod 不引用或复制核心 DLL：游戏原生扫描不会因声明前置 Mod 就允许跨 Mod 程序集引用。见 `src/LaunchGiftPortraits.Content` 的实现。

## 包与覆盖规则

`id` 为 ASCII 字母、数字、点、横线、下划线；`name` 为显示名；`priority` 越高越优先；同优先级按包 ID 的 ordinal 顺序，字典序较大的获胜。`enabled:false` 保留包但不加载。`dependencies` 列出必须存在的启用包 ID。重复包 ID 整组拒绝，坏包独立记录，缺失依赖递归隔离。

`gameBuild` 支持整数的精确匹配与 `>=数字`；Unity 适配另有程序集 SHA256 基线校验，未知程序集关闭游戏钩子。不要仅因包的 >= 声明推断新游戏版本已验证。

## 人物与原版剧情配图

```json
{
  "format":"portrait-pack/1", "id":"myart", "gameBuild":"26337366", "priority":200,
  "characters": {
    "lun": {
      "identifiers":["officerLunVisit"],
      "factories":["StoreClientListStory.CreateOfficerLunVisit"],
      "base":"portraits/lun.png",
      "variants":{"officer_lun":"portraits/lun.png"},
      "story":{"dialog_lun_visit_1":"portraits/lun_hello.png"}
    }
  }
}
```

人物身份通过 identifier 或明确的创建工厂识别。`NO_ID`、空身份不能单独匹配。精灵 key 仅用于已经识别的同一人物的阶段图。图片为 1152×844 PNG，下沿与原画布对齐；保持源画幅与透明背景。

覆盖按具体资源查询：高优先级包未配置某句剧情图时，仍可使用低优先级包该句映射。原版剧情 key 在本地化返回时按对象来源记录，传入 Dialogue/RichTextBuilder 后继续携带；不按字符串内容反查人物。

## 物品

`items` 的键为实际物品 ID。已有物品设置 `icon` 可仅换图，按具体 GameItemElement 解析，不挂全局图集入口改变形状。更换图标保持原物品的实际占格、pivot 和逻辑尺寸。多个 ID 共用原图也可独立覆盖。

新增物品使用 `create:true`，ID 必须以 `包id.` 开头；支持 `name`、`description`、`value`、`tags`、`grid:[宽,高]`、`icon`、后备 `atlas` 和 `sprite`。grid 是游戏的逻辑格数，按当前 UISettings.gridSize 换算显示尺寸；素材宽高为 32 的倍数，比例必须与 grid 一致。新增形状明确由 grid 建立，不从替换图的透明度推导。

高优先级图标包覆盖新增物品时，原创建定义仍保留，不会因换图而丢失注册。

## 新人物与剧情

`actors` 的键必须以 `包id.` 开头。字段：

- `name`：显示名；`character`：包内立绘定义；`vanillaSprite`：原版后备 key。
- `story`：包内剧情 ID；`intent`：DIALOGUE / BUY / SELL / SELLNBUY。
- `cash`：人物交易预算；`sellItems`、`buyItems`：出售与收购的物品 ID。
- `once`：是否仅来访一次，默认 true；`when`：初始出现条件。

`stories` 中每段剧情有 `start` 和 `nodes`。每个节点有 `text`、可选 `portrait`、`actions`，以及三种互斥的去向之一：`end:true`、`next:节点ID`、`choices:[...]`。选项有 `text`、`next`、`when`、`actions`。新剧情直接按节点 ID 关联，不依赖原版台词表。

## 条件与动作

条件组合使用 AND：`minDay`、`maxDay`、`minCash`、`equals:{变量:值}`、`notEquals`、`items:{物品ID:数量}`。物品条件查询游戏全部相关库存中的玩家自有物品。

动作：

| op | 含义 |
|---|---|
| set | 写包内变量，使用 key/value |
| increment | 增加包内整数变量，使用 key/amount |
| cash | 增减信用点，使用 amount；不足时保留待执行状态 |
| giveItem | 发放 key 指定物品，amount 为 1..100 |
| queueActor | 将 key 指定的包内新增人物加入来店队列 |
| extension | 调用受信任 C# 扩展注册的动作，key 是扩展 ID |

动作按人物来访、剧情节点和选项位置记录执行进度。`onceKey` 可让同一包的不同作用域共享一次性执行标记。奖励逐件保存交付进度，失败时保留待执行状态；未完成的选项锁定其它分支，避免改走另一分支造成状态死锁。`_` 开头的状态名保留给运行时。

状态使用原生 ModData 保存在当前游戏存档。进行中的人物在 OnGameLoadedLate 恢复并重新入队；初始触发条件不会阻挡已开始剧情的恢复。缺包时保留其状态；缺失物品由原生占位恢复机制保留 identifier 与保存字段。卸载、升级和恢复缺包仍须实际游戏验收。

## 原版文字覆盖与特殊资源

`texts` 以语言为第一层，例如 `"texts":{"zh":{"Dialogue/dialog_lun_visit_1":"新的中文台词"}}`。优先查询 `表名/key`，也支持原始 key；缺少当前语言时回退包内 en，再保留原版结果。

`special` 只允许 wanted、wanted_portait1..9、drjackson2。规格分别为 1332×1860、136×128、1920×1080。已解析场景目标由固定场景、根对象和子节点索引定位，不执行反复全场景替换扫描。

## 校验与配额

manifest 最大 8 MiB；包最多 10000 文件、1 GiB、1000 文件夹、32 层目录；单 PNG 最大 64 MiB，解压像素数据最大 128 MiB。校验文件边界、链接、图片 CRC 与解压流、尺寸、重复身份、悬空节点和原版 key、命名空间、动作和条件。工作台基础检查后仍需执行完整校验。

```powershell
./dist/tools/validate_pack.exe validate ./packs/demo ./catalogs/dialogue_keys.json
./dist/tools/validate_pack.exe catalog ./packs
./dist/tools/validate_pack.exe simulate ./packs/demo demo.courier ./artifacts/demo-state.json 2 0
./dist/tools/validate_pack.exe simulate ./packs/demo demo.return ./artifacts/demo-state.json 3 0
```

模拟状态文件与真实玩家存档完全独立。

## 受信任 C# 扩展

数据包不执行 C#、Python 或任意命令。复杂机制由独立原生 IMod 编译，并声明对 player.portraitruntime 的依赖。扩展引用 PortraitRuntime.Core.dll：

```csharp
StoryEngine.RegisterEffect("myplugin.doubleCredit", (pack, effect, host) => {
    host.Credit(effect.Amount * 2);
});
// 扩展禁用时：
StoryEngine.UnregisterEffect("myplugin.doubleCredit");
```

包内使用 `{"op":"extension","key":"myplugin.doubleCredit","amount":10}`。扩展未加载时动作保持待执行，不能假定成功。扩展应在游戏主线程使用注册接口，并自行保证副作用与存档迁移的正确性。
