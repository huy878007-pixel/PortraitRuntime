"""Migrate approved local artwork and audited catalogs without changing originals."""
from __future__ import annotations

import json
import shutil
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = Path(r'C:\Users\Administrator\Desktop')
AUDIT = Path((DESKTOP / '深空解包_2026-11-01/最新审计目录.txt').read_text(encoding='utf-8-sig').strip())
RELEASE = DESKTOP / '深空娘化_剧情立绘Mod/Mod发布'
ART = DESKTOP / '深空当铺人物美化/12_正式版移植/projects/LaunchGiftPortraits_1.2/Resources'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def main():
    core = ROOT / 'packs/core'
    story_map = read(RELEASE / 'StoryMap.json')
    aliases = {'officer_lun': 'lun', 'inventor': 'jackson', 'dr._jackson': 'jackson',
               'the_landlord': 'landlord', 'george': 'Georgio', 'mentor': 'Mentor',
               'the_mentor': 'Mentor', 'serpico': 'Serpico', 'joe': 'wild',
               'joe_wild': 'wild', 'odin': 'wanted4'}
    characters = {}
    for char, keys in story_map['spriteOf'].items():
        variants = {}
        for key in keys:
            filename = aliases.get(key, key) + '.png'
            source = ART / filename
            if source.is_file():
                target = core / 'portraits/base' / filename
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
                variants[key] = target.relative_to(core).as_posix()
        assert variants, char
        characters[char] = {'identifiers': [], 'factories': [], 'base': next(iter(variants.values())),
                            'variants': variants, 'story': {}}
    known = read(DESKTOP / '深空解包_2026-11-01/char_identifiers.json')
    for char, ids in known.items():
        characters[char]['identifiers'] = list(ids)
    missing_keys = []
    all_zh = read(AUDIT / 'localization/zh.json')
    dialogue_keys = {x['key'] for x in all_zh if x['table'] in {'Dialogue', 'PhoneDialogue'}}
    for key, relative in story_map['lines'].items():
        if key not in dialogue_keys:
            missing_keys.append(key)
            continue
        char = story_map['charOf'][key]
        target = core / 'portraits/story' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(RELEASE / 'Story' / relative, target)
        characters[char]['story'][key] = target.relative_to(core).as_posix()
    assert not missing_keys, missing_keys
    name_keys = {
        'name_lun': 'lun', 'name_officer_lun': 'lun', 'name_dr._jackson': 'jackson',
        'name_landlord': 'landlord', 'name_the_landlord': 'landlord', 'name_georgio': 'Georgio',
        'name_eleanor': 'eleanor', 'name_mentor': 'Mentor', 'name_the_mentor': 'Mentor',
        'name_serpico': 'Serpico', 'name_joe': 'wild', 'name_joe_wild': 'wild',
        'name_lou': 'lou', 'name_li_bei_wen': 'li', 'name_john_smith': 'john', 'name_leroy': 'leroy',
    }
    evidence, ambiguities = [], []
    id_owner = {i: c for c, d in characters.items() for i in d['identifiers']}
    for method in read(AUDIT / 'catalogs/client_factories.json'):
        candidates = {name_keys[k] for k in method['display_name_keys'] if k in name_keys}
        candidates.update(id_owner[i] for i in method['identifier_literals'] if i in id_owner)
        owners = {story_map['charOf'][k] for k in method['dialogue_keys'] if k in story_map['charOf']}
        if not candidates:
            candidates = owners
        if len(candidates) != 1:
            if candidates:
                ambiguities.append({'method': method['type'] + '.' + method['method'], 'candidates': sorted(candidates)})
            continue
        char = candidates.pop()
        factory = method['type'] + '.' + method['method']
        # Explicit creation provenance; no runtime display-name or sprite-name ownership inference.
        characters[char]['factories'].append(factory)
        for identity in method['identifier_literals']:
            if identity != 'NO_ID' and identity not in id_owner:
                characters[char]['identifiers'].append(identity)
                id_owner[identity] = char
        evidence.append({'character': char, 'factory': factory, 'file': method['file'], 'line': method['line'],
                         'name_keys': method['display_name_keys'], 'dialogue_owners': sorted(owners),
                         'identifiers': method['identifier_literals']})
    for d in characters.values():
        d['identifiers'] = sorted(set(d['identifiers']))
        d['factories'] = sorted(set(d['factories']))
    special_names = ['wanted', 'drjackson2'] + ['wanted_portait' + str(i) for i in range(1, 10)]
    special = {}
    for name in special_names:
        target = core / 'special' / (name + '.png')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ART / target.name, target)
        special[name] = target.relative_to(core).as_posix()
    write(core / 'pack.json', {'format': 'portrait-pack/1', 'id': 'core', 'name': '深空娘化 · 核心内容包',
          'author': '啊啊啊~哈稽道人', 'license': 'CC-BY-NC-4.0', 'gameBuild': '26337366',
          'priority': 100, 'characters': characters, 'special': special})
    catalogs = ROOT / 'catalogs'
    catalogs.mkdir(exist_ok=True)
    for filename in ['client_registrations.json', 'client_factories.json', 'item_registrations.json',
                     'client_dialogue_references.json', 'sprites.json', 'serialized_fields.json']:
        shutil.copy2(AUDIT / 'catalogs' / filename, catalogs / filename)
    write(catalogs / 'dialogue_keys.json', sorted(dialogue_keys))
    write(catalogs / 'localization.zh.json', all_zh)
    write(catalogs / 'identity_evidence.json', evidence)
    write(catalogs / 'identity_ambiguities.json', ambiguities)
    shutil.copy2(AUDIT / 'source_manifest.json', catalogs / 'source_manifest.json')
    write(catalogs / 'special_targets.json', special_targets(special_names))
    demo = ROOT / 'packs/demo'
    icon = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(icon)
    draw.rectangle((5, 3, 26, 28), fill='#142537', outline='#8fe9f4', width=2)
    draw.line((10, 10, 22, 10), fill='#8fe9f4', width=2)
    draw.line((10, 16, 22, 16), fill='#8fe9f4', width=2)
    demo.mkdir(parents=True, exist_ok=True)
    icon.save(demo / 'signal.png')
    base = characters['lun']['base']
    shutil.copy2(core / base, demo / 'courier.png')
    write(demo / 'pack.json', {
        'format': 'portrait-pack/1', 'id': 'demo', 'name': '扩展示范：失联信号',
        'author': 'PortraitRuntime', 'license': 'Example code; portrait retains core artwork license',
        'priority': 200, 'dependencies': ['core'], 'enabled': True,
        'characters': {'courier': {'identifiers': ['demo.courier', 'demo.return'], 'base': 'courier.png'}},
        'items': {'demo.signal': {'create': True, 'name': '加密信号片', 'description': '内容核心库示范物品，可保存、交易。',
                  'icon': 'signal.png', 'grid': [1, 1], 'value': 50, 'tags': ['HOUSEHOLD_GOOD']}},
        'actors': {
            'demo.courier': {'name': '信号信使（扩展示范）', 'character': 'courier', 'story': 'first',
                             'vanillaSprite': 'officer_lun', 'when': {'minDay': 2}},
            'demo.return': {'name': '信号信使（回访）', 'character': 'courier', 'story': 'return',
                            'vanillaSprite': 'officer_lun', 'when': {'minDay': 3, 'equals': {'accepted': 'yes'}}},
            'demo.trader': {'name': '信号片交易员（扩展示范）', 'character': 'courier', 'story': 'trade',
                            'vanillaSprite': 'officer_lun', 'intent': 'SELLNBUY', 'sellItems': ['demo.signal'],
                            'buyItems': ['demo.signal'], 'cash': 500, 'when': {'minDay': 4, 'equals': {'accepted': 'yes'}}},
        },
        'stories': {
            'first': {'start': 'start', 'nodes': {
                'start': {'text': '我带来一枚加密信号片。你愿意接收它吗？', 'choices': [
                    {'text': '接收信号片', 'next': 'accept'}, {'text': '拒绝', 'next': 'decline'}]},
                'accept': {'text': '请保管好它。我会再来。你可以先保存并退出，再读档验证。', 'end': True,
                           'actions': [{'op': 'giveItem', 'key': 'demo.signal'}, {'op': 'set', 'key': 'accepted', 'value': 'yes'}]},
                'decline': {'text': '我明白了。这条剧情会记住你的选择。', 'end': True,
                            'actions': [{'op': 'set', 'key': 'accepted', 'value': 'no'}]}}},
            'return': {'start': 'start', 'nodes': {
                'start': {'text': '我们又见面了。上次的选择已经保存。', 'next': 'reward'},
                'reward': {'text': '这是解码报酬：20 信用点。之后交易员也会来。', 'end': True,
                           'actions': [{'op': 'cash', 'amount': 20}, {'op': 'set', 'key': 'finished', 'value': 'yes'}]}}},
            'trade': {'start': 'start', 'nodes': {'start': {'text': '我出售也收购加密信号片。请通过原生交易界面完成买卖。', 'end': True}}},
        }})
    report = {'characters': len(characters), 'story_mappings': sum(len(x['story']) for x in characters.values()),
              'identity_count': sum(len(x['identifiers']) for x in characters.values()),
              'factory_bindings': len(evidence), 'ambiguities': ambiguities,
              'artwork_files': len(list(core.rglob('*.png'))), 'special_targets': len(special_targets(special_names))}
    write(ROOT / 'artifacts/migration.json', report)
    print(json.dumps(report, ensure_ascii=True))


def special_targets(names):
    sprites = {(x['asset'], x['path_id']): x['name'] for x in read(AUDIT / 'catalogs/sprites.json') if x['name'] in names}
    targets = []
    for level in ['level0', 'level1']:
        folder = AUDIT / 'objects' / level
        gos = {int(p.stem): read(p) for p in (folder / 'GameObject').glob('*.json')}
        transforms = {int(p.stem): read(p) for kind in ['Transform', 'RectTransform'] for p in (folder / kind).glob('*.json')}
        go_transform = {t['m_GameObject']['m_PathID']: i for i, t in transforms.items()}
        for kind in ['SpriteRenderer', 'MonoBehaviour']:
            for path in (folder / kind).glob('*.json'):
                data = read(path)
                sprite = data.get('m_Sprite')
                if not isinstance(sprite, dict) or not sprite['m_PathID']:
                    continue
                # Scene externals were independently verified in the audit.
                externals = read(AUDIT / 'catalogs/sprite_dicts.json')
                ext = next((x['externals'] for x in externals if x['source'].startswith(level + '/')), [])
                file_id = sprite['m_FileID']
                asset = level if file_id == 0 else Path(ext[file_id - 1]).name if file_id <= len(ext) else ''
                name = sprites.get((asset, sprite['m_PathID']))
                go = data.get('m_GameObject', {}).get('m_PathID')
                if name not in names or go not in go_transform:
                    continue
                transform = transforms[go_transform[go]]
                children = []
                while transform['m_Father']['m_PathID']:
                    parent = transforms[transform['m_Father']['m_PathID']]
                    children.append([x['m_PathID'] for x in parent['m_Children']].index(go_transform[transform['m_GameObject']['m_PathID']]))
                    transform = parent
                targets.append({'scene': int(level[-1]), 'root': gos[transform['m_GameObject']['m_PathID']]['m_Name'],
                                'children': list(reversed(children)), 'name': gos[go]['m_Name'], 'key': name,
                                'component': 'SpriteRenderer' if kind == 'SpriteRenderer' else 'Image'})
    return targets


if __name__ == '__main__':
    main()
