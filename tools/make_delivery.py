"""Assemble the tested release, source snapshot, workbench and evidence on Desktop."""
from __future__ import annotations

import json
import os
import shutil
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    verification = json.loads((ROOT / 'artifacts/verification.json').read_text(encoding='utf-8'))
    native = json.loads((ROOT / 'artifacts/native-contracts.json').read_text(encoding='utf-8'))
    package = json.loads((ROOT / 'artifacts/package.json').read_text(encoding='utf-8'))
    assert verification['passed'] and native['passed'] and not verification['gameRuntimeTested']
    assert not native['scanner']['Refused'] and not native['scanner']['HardFlags']
    version = package['version']
    beauty_version = package['beautyVersion']
    # Home Desktop by default; set PORTRAIT_DELIVERY_DIR to put the drop somewhere else.
    destination = Path(os.environ.get('PORTRAIT_DELIVERY_DIR', str(Path.home() / 'Desktop'))) / f'深空内容核心库_{version}_双Mod_待游戏验收'
    if destination.exists():
        destination = destination.with_name(destination.name + '_' + str(time.time_ns()))
    destination.mkdir()
    shutil.copytree(ROOT / 'dist/player.portraitruntime', destination / 'Mod/player.portraitruntime')
    shutil.copytree(ROOT / 'dist/player.launchgiftportraits', destination / 'Mod/player.launchgiftportraits')
    shutil.copytree(ROOT / 'dist/workbench', destination / '工作台')
    shutil.copytree(ROOT / 'dist/tools', destination / '校验工具')
    shutil.copytree(ROOT / 'docs', destination / '文档')
    shutil.copytree(ROOT, destination / '源代码', ignore=shutil.ignore_patterns('.git', 'dist', 'artifacts', 'bin', 'obj', '__pycache__'))
    evidence = destination / '验证报告'
    evidence.mkdir()
    for name in ['verification.json', 'native-contracts.json', 'package.json', 'core-tests.log', 'workbench-test.json',
                 'workbench-overview.png', 'workbench-character.png', 'workbench-mobile.png', 'workshop_item.vdf',
                 'offline-audit.html', 'offline-audit.json']:
        shutil.copy2(ROOT / 'artifacts' / name, evidence / name)
    test_result = next(line for line in (ROOT / 'artifacts/core-tests.log').read_text(encoding='utf-8').splitlines() if line.startswith('RESULT '))
    summary = f'''# 深空内容核心库 {version} · 双 Mod · 待游戏验收

已完成可构建交付与离线验证。游戏内验收尚未完成，游戏窗口由你操作。

- 核心 `player.portraitruntime` {version}；人物美化内容包 `player.launchgiftportraits` {beauty_version}。
- 核心规则测试：{test_result}
- 原生接口静态检查：{len(native['checks'])} 项通过。
- 游戏原生 ModScanner：Refused=false，扫描 {native['scanner']['MethodsScanned']} 个方法，无硬拒绝项。
- 工作台：独立无头 Chrome 测试通过，包含选图、分支、持久保存、导出 ZIP 及完整包校验。
- 跨进程模拟：接受奖励、保存、重新读取、回访推进、重复奖励拦截及拒绝分支拦截均通过。
- 原游戏程序集指纹已复核，本次没有启动游戏、安装 Mod 或修改玩家存档，没有上传工坊。

## 你可以直接使用

1. `工作台/index.html`：双击进入中文离线工作台。
2. `Mod/player.portraitruntime`：独立核心库，不含人物美术。
3. `Mod/player.launchgiftportraits`：依赖核心的人物美化内容版，替换旧独立版；两个 Mod 分开放置。
4. `校验工具/validate_pack.exe`：数据包校验与模拟命令行工具。
5. `源代码`：包含 csproj、代码、完整数据和素材，可按 README 重新构建与测试。
6. `验证报告`：测试结果、截图、文件哈希与私有发布描述文件。

## 本轮修复后请重点核对（对应你勾选的 11 项）

- 已确认正确的 7 项（贝利首次与回访、卢放箱／取箱／催款、李北文供货与终章）：人物与图片保持不变。交付前再次核对：23 张基础立绘与 11 张特殊资源与 1.2.5 原版 DLL 嵌入资源逐字节一致，安装副本与源码也一致。
- 需要修改的 4 项（彩票代表、下层区长者、老拾荒客、拾荒食物供货商）：不是素材错，也不是绑错了人，而是巡检预览在查不到取图信息时借用了字典第一张图（导师）。核实原版行为：这四位在原版里本来就取不到具名立绘——彩票代表把中文显示名当成精灵 key、长者取图指向一条精灵为空的原版数据、拾荒供货商直接取随机池——游戏会回退成随机拾荒者外观。预览现在照同一规则显示，并在 HUD 与报告里标「待核对」，不再冒充导师，也没有给他们硬绑具名角色。
- 请用 F3 巡检这 4 项：确认显示的是拾荒者外观、状态标为「待核对」；再看卢与李北文的条目，姓名应是「卢」「李北文」而不是内部标签。

## 当前内容

人物美化内容包：17 位角色，669 条原有剧情配图，11 项特殊资源。
扩展示范默认关闭：信使、接受/拒绝分支、独立信号片物品、回访与交易员；示范立绘复用既有美术。
图标示范：5 件量具，绿色边框，默认关闭。
工作台目录：276 个客人注册项、572 个普通物品、2530 条剧情及电话台词。

## 尚待实际游戏确认

- 立绘与离场显示，含柜台外博士、朝左／朝右离场、剧情差分结束回到基础图
- 高清立绘开关与离场后的相机还原
- F10 原生窗口的滑条、保存／重置／关闭、窗口拖动与越界回收
- 原生选项按钮、交易与库存、真实保存读档、30 分钟内存稳定性

静态检查与模拟不能代替以上项目。

核心库单独启用时：F3 调试界面照常可用，只做原版取图与控制台巡检，**不替换任何人物或场景美术**，也不接管相机与位置。人物立绘、剧情差分、特殊资源、显示参数与 F10 全在美化内容包里。物品图标是另一回事——它属于内容包的物品运行时，核心照常按 `items[].icon` 应用，与人物美化无关。

F3 打开调试界面，可切换 F4；提供当天全部人物、阶段和剧情配图巡检。默认预览不执行原剧情工厂；真实来访需显式选择并可能改变剧情。巡检期间阻止保存，停止时恢复排队。预览按游戏自己的取图规则显示：查不到的条目会标为「待核对」并走原生的随机拾荒者兜底，不会冒充导师模板。

详细 JSONL 日志、F11 诊断、巡检报告保存在游戏持久数据目录的 PortraitRuntime/logs。每会话最多 32 个 4 MiB 日志文件，历史会话保留。请备份旧独立美化版到 Mods 目录外，再安装两份新 Mod；游戏内验证由你操作。
'''
    (destination / '先读我.md').write_text(summary, encoding='utf-8')
    report = {'destination': str(destination), 'coreTests': test_result, 'nativeContracts': len(native['checks']),
              'modScannerRefused': False, 'gameRuntimeTested': False, 'sourceSnapshot': True}
    (ROOT / 'artifacts/delivery.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=True))


if __name__ == '__main__':
    main()
