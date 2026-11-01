# 深空内容核心库 PortraitRuntime

本地开发版本 0.3.7。目标游戏：Probably Stolen 0.46D / BuildID 26337366。

运行时 Mod `player.portraitruntime`：不带任何人物美术，负责内容注册、取图规则、渲染机制、生命周期通知、F3 调试台与日志。人物立绘、剧情差分、特殊场景美术、显示设置与 F10 窗口在**另一个仓库**：

- 美化包（素材 + 内容包 + 美化 Mod 源码）：https://github.com/huy878007-pixel/probably-waifu

两者通过 JSON 桥接通信，美化包不引用本仓库的程序集——游戏原生扫描会拒绝跨 Mod 程序集引用。核心可单独使用，只是不替换任何人物或场景美术。

F3 打开核心调试界面，可改 F4；支持当天全部人物巡检、阶段与剧情配图巡检、单项预览、自动播放和问题报告。默认预览隔离剧情回调，真实来访模式需手动选择并使用测试存档。详细 JSONL 日志、F11 诊断和巡检报告位于游戏持久数据目录的 `PortraitRuntime/logs`。具体步骤见验收文档。

核心库包含身份驱动的取图规则、按物品 ID 的图标覆盖、新人物与剧情分支、新物品注册、原生 ModData 持久化、数据包工具和离线工作台。

**当前交付状态：可构建、自动测试版本。游戏内显示、交易、真实存档和 30 分钟稳定性尚待用户验收。** 自动模拟与 ModScanner 结果不能替代游戏实测。游戏窗口由用户操作。

## 目录

- `src/PortraitRuntime.Core`：数据模型、校验、优先级、条件、动作与持久化模拟。
- `src/PortraitRuntime.Unity`：原生 IMod/Harmony 游戏适配与 `PortraitApi` 桥。
- `src/PortraitRuntime.Cli`：`validate_pack` 校验、模拟与离线巡检工具。
- `packs/demo`：新信使、分支选择、信号片、回访与交易员示范。
- `packs/icon-examples`：五件物品图标测试包，默认关闭，绿色边框用于辨认。
- `workbench/index.html`：可双击使用的中文工作台，无需服务器。
- `catalogs`：运行时需要的兼容索引（台词 key、巡检目录）。
- `docs`：数据格式、使用、验收及实现约定。
- `beauty/`：**不入库**。美化仓库的本地检出，打包时才需要。
- `artifacts` / `dist`：测试输出、截图、包文件哈希与构建产物。

## 构建

需要 .NET SDK 10、Python 3 和 Pillow；运行时目标为 Unity Mono 的 netstandard2.1。项目从 `Directory.Build.props` 的 `GameManaged` 读取本地游戏程序集。

只验证核心库：

```powershell
python tools/verify_all.py
```

打包成可安装的两个 Mod 还需要美化仓库：

```powershell
git clone https://github.com/huy878007-pixel/probably-waifu.git beauty
python tools/build_pack.py
```

`beauty/` 已在 `.gitignore` 中；也可以用 `PORTRAIT_BEAUTY_DIR` 指向任意检出位置。缺这个目录时，打包与完整验证会明确报错，不会悄悄产出一个没有美术的美化包。

```powershell
python tools/verify_all.py
```

输入素材和审计目录已存在时，可用 `python tools/migrate.py` 重建核心包与示范，`python tools/build_workbench.py` 重建工作台数据，`python tools/build_icon_examples.py` 重建五件图标示范。迁移只读取原始素材。

这些重建脚本不从仓库读素材，路径由环境变量给出：

| 变量 | 用途 | 默认 |
|---|---|---|
| `PORTRAIT_SOURCE_DIR` | 反编译审计与旧版发布所在的素材目录 | `~/Desktop` |
| `PORTRAIT_SPRITE_DUMP` | 解包得到的贴图目录 | `sprite-dump/sprites` |
| `PORTRAIT_DELIVERY_DIR` | 交付包输出位置 | `~/Desktop` |

`verify_all.py` 不需要这些变量，也不依赖游戏本体。


也可双击 `一键构建与测试.cmd`。它不修改 PowerShell 执行策略；本机有签名策略时优先使用 Python/批处理入口。

## 安装与验收

见 `docs/使用与游戏验收.md`。构建不会自动写入游戏 Mods 目录，不会修改已有 Mod、玩家存档或工坊内容。

新增剧情、人物、物品应在专用测试存档中验证。旧独立美化版必须关闭；新依赖内容版与核心一起启用。交付中的剧情和图标示范默认关闭。新增内容通过 `ModHelper.SetModData/GetModData` 和游戏原生物品保存流程持久化。

## 授权

本仓库**只有代码**，按 **GNU AGPL-3.0** 提供（见 `LICENSE`）：你可以自由使用、修改、再分发，包括商用，但修改后的分发必须同样开源。

人物美术不在本仓库，它是另一个项目的一部分，按 **CC BY-NC 4.0**（署名—非商业性使用）授权，署名：啊啊啊~哈稽道人。见 https://github.com/huy878007-pixel/probably-waifu

例外只有一处：`packs/icon-examples` 里的五件物品图标是**原游戏资源的本地测试变体**，权利归游戏作者，不随本项目授权，仅保留用于图标覆盖功能的自测。

## 关于官方资源与反编译

本项目**不是**官方作品，与开发商 Questing Goose Studio 没有任何隶属或授权关系。

它通过**反编译与解包官方游戏 Probably Stolen 0.46D** 来了解游戏内部的接口、数据结构与资源布局，从而在运行时对齐官方的取图与生命周期行为。这一点在 `NOTICE.md` 中有完整说明。本仓库：

- **不包含**游戏本体、游戏程序集，也不包含反编译得到的源码；
- **不包含**人物美术，占空间的美术素材都在美化仓库；
- 只保留兼容所需的标识符与索引（`catalogs/` 下两个运行时文件）；
- 游戏名称、剧情文本、角色设定与原始美术素材的版权归原权利方所有。

如果你是该游戏的权利方并希望移除本仓库内容，请通过 issue 联系。

