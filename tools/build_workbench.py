"""Build an offline workbench catalog from verified game data."""
from __future__ import annotations

import json
import os
import shutil
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return json.loads((ROOT / 'catalogs' / name).read_text(encoding='utf-8-sig'))


def main():
    workbench = ROOT / 'workbench'
    (workbench / 'assets').mkdir(exist_ok=True)
    texts = read('localization.zh.json')
    loc = {x['key']: x['text'] for x in texts}
    methods = {x['type'] + '.' + x['method']: x for x in read('client_factories.json')}
    characters = []
    for entry in read('client_registrations.json'):
        method = methods[entry['factory']]
        characters.append({'id': entry['registration_id'], 'factory': entry['factory'],
                           'identifiers': method['identifier_literals'],
                           'name': next((loc[x] for x in method['display_name_keys'] if x in loc), entry['registration_id'])})
    sprites = {x['name']: x for x in read('sprites.json')}
    original_images = list(Path(os.environ.get('PORTRAIT_SPRITE_DUMP', 'sprite-dump/sprites')).rglob('*.png'))
    items = []
    for entry in read('item_registrations.json'):
        if entry['registry_kind'] != 'GameItem':
            continue
        item = {'id': entry['id'], 'name': next((loc[x] for x in entry['direct_localization_literals'] if x.endswith('_name') and x in loc), entry['id'])}
        if entry['direct_icons']:
            atlas, sprite = entry['direct_icons'][0]
            item.update(atlas=atlas, sprite=sprite)
            meta = sprites.get(sprite)
            if meta:
                prefix = meta['asset'] + '_' + str(meta['path_id']) + '_'
                image = next((p for p in original_images if p.name.startswith(prefix)), None)
                if image:
                    filename = str(meta['path_id']) + '_' + image.name
                    shutil.copy2(image, workbench / 'assets' / filename)
                    item['preview'] = 'assets/' + filename
        items.append(item)
    owners = defaultdict(set)
    for row in read('client_dialogue_references.json'):
        owners[row['key']].add(row['factory'])
    dialogues = [{'key': x['key'], 'text': x['text'], 'owners': sorted(owners[x['key']])} for x in texts if x['table'] in {'Dialogue', 'PhoneDialogue'}]
    data = {'characters': characters, 'items': items, 'dialogues': dialogues, 'dialogueKeys': read('dialogue_keys.json')}
    (workbench / 'data.js').write_text('window.RUNTIME_DATA=' + json.dumps(data, ensure_ascii=False, separators=(',', ':')) + ';\n', encoding='utf-8')
    print(json.dumps({key: len(value) for key, value in data.items()}))


if __name__ == '__main__':
    main()
