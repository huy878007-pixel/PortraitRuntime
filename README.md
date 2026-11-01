# 深空内容核心库 PortraitRuntime

本地开发版本 0.2.2。目标游戏：Probably Stolen 0.46D / BuildID 26337366。

交付分为两个 Mod：`player.portraitruntime` 是不含美术内容的核心库；`player.launchgiftportraits` 是依赖核心库的人物美化内容版。后者替换旧同 ID 独立美化版，安装前请备份旧版到 Mods 目录外。核心可单独使用。

F3 打开核心调试界面，可改 F4；支持当天全部人物巡检、阶段与剧情配图巡检、单项预览、自动播放和问题报告。默认预览隔离剧情回调，真实来访模式需手动选择并使用测试存档。详细 JSONL 日志、F11 诊断和巡检报告位于游戏持久数据目录的 `PortraitRuntime/logs`。具体步骤见验收文档。

核心库包含身份驱动的立绘与剧情配图、按物品 ID 的图标覆盖、特殊资源明确目标替换、新人物与剧情分支、新物品注册、原生 ModData 持久化、数据包工具和离线工作台。

**当前交付状态：可构建、自动测试版本。游戏内显示、交易、真实存档和 30 分钟稳定性尚待用户验收。** 自动模拟与 ModScanner 结果不能替代游戏实测。游戏窗口由用户操作。

## 目录

- `src/PortraitRuntime.Core`：数据模型、校验、优先级、条件、动作与持久化模拟。
- `src/PortraitRuntime.Unity`：原生 IMod/Harmony 游戏适配。
- `src/PortraitRuntime.Cli`：`validate_pack` 校验和模拟工具。
- `packs/core`：17 位角色、669 条原有剧情配图、11 项特殊资源。
- `packs/demo`：新信使、分支选择、信号片、回访与交易员示范；立绘复用已批准的核心美术。
- `packs/icon-examples`：五件物品图标测试包，默认关闭，绿色边框用于辨认。
- `workbench/index.html`：可双击使用的中文工作台，无需服务器。
- `catalogs`：本机审计得到的完整索引与绑定依据。
- `docs`：数据格式、使用、验收及实现约定。
- `artifacts`：测试输出、截图和包文件哈希。
- `dist`：构建后的 Mod、工作台、工具和交付包。

## 构建

本机需要 .NET SDK 10、Python 3 和 Pillow；运行时目标为 Unity Mono 的 netstandard2.1。项目从 `Directory.Build.props` 的 `GameManaged` 读取本地游戏程序集。

```powershell
cd D:\AI\PortraitRuntime
python tools/verify_all.py
```

输入素材和审计目录已存在时，可用 `python tools/migrate.py` 重建核心包与示范，`python tools/build_workbench.py` 重建工作台数据，`python tools/build_icon_examples.py` 重建五件图标示范。迁移只读取原始素材。

也可双击 `一键构建与测试.cmd`。它不修改 PowerShell 执行策略；本机有签名策略时优先使用 Python/批处理入口。

## 安装与验收

见 `docs/使用与游戏验收.md`。本次构建不会自动写入游戏 Mods 目录，不会修改已有 Mod、玩家存档或工坊内容。

新增剧情、人物、物品应在专用测试存档中验证。旧独立美化版必须关闭；新依赖内容版与核心一起启用。交付中的剧情和图标示范默认关闭。新增内容通过 `ModHelper.SetModData/GetModData` 和游戏原生物品保存流程持久化。

代码按 AGPL-3.0 提供；核心美术归原作者，沿用内容包声明的 CC BY-NC 4.0。游戏资源预览和五件图标测试素材保留游戏原有权利，仅供本机参考与测试。
