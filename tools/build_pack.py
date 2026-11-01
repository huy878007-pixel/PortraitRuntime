"""Validate content, then build separate runtime and dependent beauty Mods."""
from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import time
from pathlib import Path
from xml.etree.ElementTree import Element, SubElement, ElementTree

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
CORE_VERSION = '0.3.3'
BEAUTY_VERSION = '2.1.3'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def manifest(dest, mod_id, name, version, description, assemblies, dependencies=(), capabilities=()):
    root = Element('ModManifest')
    for key, value in {'ID': mod_id, 'Name': name, 'Author': '啊啊啊~哈稽道人 / PortraitRuntime',
                       'ModVersion': version, 'Description': description}.items():
        SubElement(root, key).text = value
    SubElement(SubElement(root, 'GameVersions'), 'Version').text = '0.46D'
    prereqs = SubElement(root, 'Prerequisites')
    for dependency in dependencies:
        SubElement(prereqs, 'Mod').text = dependency
    listed = SubElement(root, 'Assemblies')
    for assembly in assemblies:
        SubElement(listed, 'Assembly').text = assembly
    caps = SubElement(root, 'Capabilities')
    for capability in capabilities:
        SubElement(caps, 'Capability').text = capability
    ElementTree(root).write(dest / 'manifest.xml', encoding='utf-8', xml_declaration=True)


def promote(source, destination):
    if destination.exists():
        backup = ROOT / 'artifacts' / (destination.name + '-previous-' + str(time.time_ns()))
        assert destination.resolve().is_relative_to(ROOT / 'dist') and backup.resolve().is_relative_to(ROOT / 'artifacts')
        shutil.move(str(destination), str(backup))
    assert source.resolve().is_relative_to(ROOT / 'artifacts') and destination.resolve().is_relative_to(ROOT / 'dist')
    destination.parent.mkdir(exist_ok=True)
    shutil.move(str(source), str(destination))


def main():
    cli = ROOT / 'src/PortraitRuntime.Cli/bin/Release/net10.0/validate_pack.dll'
    reports = []
    for folder in sorted((ROOT / 'packs').iterdir()):
        if not (folder / 'pack.json').is_file():
            continue
        result = subprocess.run(['dotnet', str(cli), 'validate', str(folder), str(ROOT / 'catalogs/dialogue_keys.json')], capture_output=True, text=True, encoding='utf-8', check=False)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        for path in folder.rglob('*.png'):
            with Image.open(path) as image:
                image.verify()
        reports.append(json.loads(result.stdout))
    staging = ROOT / 'artifacts' / ('package-stage-' + str(time.time_ns()))
    runtime = staging / 'player.portraitruntime'
    beauty = staging / 'player.launchgiftportraits'
    runtime.mkdir(parents=True)
    beauty.mkdir()
    for name, project in [('PortraitRuntime.Core', 'Core'), ('PortraitRuntime.Unity', 'Unity')]:
        shutil.copy2(ROOT / f'src/PortraitRuntime.{project}/bin/Release/netstandard2.1/{name}.dll', runtime / (name + '.dll'))
    (runtime / 'packs').mkdir()
    (runtime / 'packs/内容包说明.txt').write_text('核心库不附带人物美术。人物美化 Mod 通过依赖关系注册独立内容目录；也可在此添加社区包目录。', encoding='utf-8')
    (runtime / 'catalogs').mkdir()
    for name in ['dialogue_keys.json', 'debug_visits.json']:
        shutil.copy2(ROOT / 'catalogs' / name, runtime / 'catalogs' / name)
    manifest(runtime, 'player.portraitruntime', '深空内容核心库 · F3调试台', CORE_VERSION,
             '独立内容运行时，无人物美术。F3调试台（可改F4），当天人物巡览、剧情配图巡检、详细日志、F11诊断。显示设置由人物美化内容包提供。',
             ['PortraitRuntime.Core.dll', 'PortraitRuntime.Unity.dll'], capabilities=['files', 'reflection-external'])
    bridge = ROOT / 'src/LaunchGiftPortraits.Content/bin/Release/netstandard2.1/LaunchGiftPortraits.Content.dll'
    shutil.copy2(bridge, beauty / bridge.name)
    (beauty / 'portrait-content.marker').write_text('portrait-content/1\n', encoding='utf-8')
    # Scene knowledge for the special artwork belongs to the beauty Mod: the core performs no replacement.
    shutil.copy2(ROOT / 'catalogs/special_targets.json', beauty / 'special_targets.json')
    shutil.copytree(ROOT / 'packs', beauty / 'packs')
    for sample in ['demo', 'icon-examples']:
        path = beauty / 'packs' / sample / 'pack.json'
        data = json.loads(path.read_text(encoding='utf-8'))
        data['enabled'] = False
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    manifest(beauty, 'player.launchgiftportraits', '深空娘化 · 人物美化内容包', BEAUTY_VERSION,
             '独立人物美化Mod：17位角色、669条剧情配图、11项特殊资源、F10原生显示设置窗口。依赖深空内容核心库，不包含旧版全局精灵替换逻辑。',
             [bridge.name], dependencies=['player.portraitruntime'], capabilities=['files', 'reflection-external'])
    for destination in [runtime, beauty]:
        shutil.copy2(ROOT / 'LICENSE', destination / 'LICENSE.txt')
    (beauty / '美术与素材署名.txt').write_text('人物美术：啊啊啊~哈稽道人，CC BY-NC 4.0。示范人物复用已有美术。图标示范为原游戏资源的本地测试变体，权利归游戏作者。', encoding='utf-8')
    assert not list(runtime.rglob('*.png')) and not list(runtime.rglob('pack.json'))
    assert not (beauty / 'PortraitRuntime.Core.dll').exists()
    files = []
    for mod in [runtime, beauty]:
        files.extend({'path': p.relative_to(staging).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted(mod.rglob('*')) if p.is_file())
    report = {'version': CORE_VERSION, 'beautyVersion': BEAUTY_VERSION, 'packs': reports, 'files': files, 'mods': ['player.portraitruntime', 'player.launchgiftportraits'],
              'runtimeHasArtwork': False, 'beautyDependsOnRuntime': True, 'installed': False, 'gameRuntimeTested': False}
    (ROOT / 'artifacts/package.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    for mod, version in [(runtime, CORE_VERSION), (beauty, BEAUTY_VERSION)]:
        promote(mod, ROOT / 'dist' / mod.name)
        shutil.make_archive(str(ROOT / 'dist' / (mod.name + '-' + version)), 'zip', ROOT / 'dist', mod.name)
    shutil.copytree(cli.parent, ROOT / 'dist/tools', dirs_exist_ok=True)
    shutil.copytree(ROOT / 'workbench', ROOT / 'dist/workbench', dirs_exist_ok=True)
    shutil.copytree(ROOT / 'docs', ROOT / 'dist/docs', dirs_exist_ok=True)
    print(json.dumps({'valid': True, 'files': len(files), 'mods': report['mods'], 'runtimeHasArtwork': False}))


if __name__ == '__main__':
    main()
