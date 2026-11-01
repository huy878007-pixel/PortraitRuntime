"""Validate images and package a reviewable, uninstalled native Mod."""
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


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


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
    final = ROOT / 'dist/player.portraitruntime'
    staging = ROOT / 'artifacts' / ('package-stage-' + str(time.time_ns()))
    dest = staging / 'player.portraitruntime'
    dest.mkdir(parents=True)
    for name, project in [('PortraitRuntime.Core', 'Core'), ('PortraitRuntime.Unity', 'Unity')]:
        shutil.copy2(ROOT / f'src/PortraitRuntime.{project}/bin/Release/netstandard2.1/{name}.dll', dest / (name + '.dll'))
    shutil.copytree(ROOT / 'packs', dest / 'packs', dirs_exist_ok=True)
    (dest / 'catalogs').mkdir(exist_ok=True)
    for name in ['dialogue_keys.json', 'special_targets.json']:
        shutil.copy2(ROOT / 'catalogs' / name, dest / 'catalogs' / name)
    manifest = Element('ModManifest')
    for key, value in {'ID': 'player.portraitruntime', 'Name': '深空内容核心库 · 娘化与扩展',
                       'Author': 'PortraitRuntime / 啊啊啊~哈稽道人（核心美术）', 'ModVersion': '0.1.0',
                       'Description': '身份驱动立绘、剧情与物品内容包。含17角色娘化包和新增人物/分支/物品/存档示范。游戏内验证待完成。F10设置，F11诊断，F12示范。'}.items():
        SubElement(manifest, key).text = value
    SubElement(SubElement(manifest, 'GameVersions'), 'Version').text = '0.46D'
    assemblies = SubElement(manifest, 'Assemblies')
    for name in ['PortraitRuntime.Core.dll', 'PortraitRuntime.Unity.dll']:
        SubElement(assemblies, 'Assembly').text = name
    caps = SubElement(manifest, 'Capabilities')
    for name in ['files', 'reflection-external']:
        SubElement(caps, 'Capability').text = name
    ElementTree(manifest).write(dest / 'manifest.xml', encoding='utf-8', xml_declaration=True)
    shutil.copy2(ROOT / 'LICENSE', dest / 'LICENSE.txt')
    (dest / '美术与素材署名.txt').write_text('核心娘化美术：啊啊啊~哈稽道人，CC BY-NC 4.0。\n示范人物复用核心包美术。五件图标为游戏原始资源的本地测试示范，原始权利归游戏作者。\n', encoding='utf-8')
    tools_dest = ROOT / 'dist/tools'
    shutil.copytree(cli.parent, tools_dest, dirs_exist_ok=True)
    shutil.copytree(ROOT / 'workbench', ROOT / 'dist/workbench', dirs_exist_ok=True)
    shutil.copytree(ROOT / 'docs', ROOT / 'dist/docs', dirs_exist_ok=True)
    (ROOT / 'artifacts').mkdir(exist_ok=True)
    files = [{'path': p.relative_to(dest).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted(dest.rglob('*')) if p.is_file()]
    report = {'packs': reports, 'files': files, 'installed': False, 'gameRuntimeTested': False}
    (ROOT / 'artifacts/package.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    if final.exists():
        backup = ROOT / 'artifacts' / ('previous-native-' + str(time.time_ns()))
        assert final.resolve().is_relative_to(ROOT) and backup.resolve().is_relative_to(ROOT)
        shutil.move(str(final), str(backup))
    final.parent.mkdir(exist_ok=True)
    assert dest.resolve().is_relative_to(ROOT) and final.resolve().is_relative_to(ROOT)
    shutil.move(str(dest), str(final))
    shutil.make_archive(str(ROOT / 'dist/PortraitRuntime-0.1.0'), 'zip', final.parent, final.name)
    print(json.dumps({'valid': True, 'files': len(files), 'destination': str(final)}, ensure_ascii=True))


if __name__ == '__main__':
    main()
