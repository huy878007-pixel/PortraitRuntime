"""Assemble the tested release, source snapshot, workbench and evidence on Desktop."""
from __future__ import annotations

import json
import shutil
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    verification = json.loads((ROOT / 'artifacts/verification.json').read_text(encoding='utf-8'))
    native = json.loads((ROOT / 'artifacts/native-contracts.json').read_text(encoding='utf-8'))
    assert verification['passed'] and native['passed'] and not verification['gameRuntimeTested']
    assert not native['scanner']['Refused'] and not native['scanner']['HardFlags']
    destination = Path(r'C:\Users\Administrator\Desktop\深空内容核心库_0.2.0_双Mod_待游戏验收')
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
                 'workbench-overview.png', 'workbench-character.png', 'workbench-mobile.png', 'workshop_item.vdf']:
        shutil.copy2(ROOT / 'artifacts' / name, evidence / name)
    test_result = next(line for line in (ROOT / 'artifacts/core-tests.log').read_text(encoding='utf-8').splitlines() if line.startswith('RESULT '))
    summary = f'''# 深空内容核心库 0.2.0 · 双 Mod · 待游戏验收

已完成可构建交付与离线验证。游戏内验收尚未完成，游戏窗口由你操作。

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
3. `校验工具/validate_pack.exe`：数据包校验与模拟命令行工具。
4. `源代码`：包含 csproj、代码、完整数据和素材，可按 README 重新构建与测试。
5. `验证报告`：测试结果、截图、文件哈希与私有发布描述文件。

## 当前内容

人物美化内容包：17 位角色，669 条原有剧情配图，11 项特殊资源。
扩展示范默认关闭：信使、接受/拒绝分支、独立信号片物品、回访与交易员；示范立绘复用既有美术。
图标示范：5 件量具，绿色边框，默认关闭。
工作台目录：276 个客人注册项、572 个普通物品、2530 条剧情及电话台词。

## 尚待实际游戏确认

立绘和离场显示、原生选项按钮、交易与库存、真实保存读档、F10 窗口输入和中文字体、30 分钟内存稳定性。静态检查与模拟不能代替这些项目。

F7 打开调试界面，可切换 F8；提供当天全部人物、阶段和剧情配图巡检。默认预览不执行原剧情工厂；真实来访需显式选择并可能改变剧情。巡检期间阻止保存，停止时恢复排队。

详细 JSONL 日志、F11 诊断、巡检报告保存在游戏持久数据目录的 PortraitRuntime/logs。每会话最多 32 个 4 MiB 日志文件，历史会话保留。请备份旧独立美化版到 Mods 目录外，再安装两份新 Mod；游戏内验证由你操作。
'''
    (destination / '先读我.md').write_text(summary, encoding='utf-8')
    report = {'destination': str(destination), 'coreTests': test_result, 'nativeContracts': len(native['checks']),
              'modScannerRefused': False, 'gameRuntimeTested': False, 'sourceSnapshot': True}
    (ROOT / 'artifacts/delivery.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=True))


if __name__ == '__main__':
    main()
