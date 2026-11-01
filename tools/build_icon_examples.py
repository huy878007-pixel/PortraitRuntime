"""Create five clearly labelled, disabled-by-default visual override examples."""
from __future__ import annotations

import json
import math
import os
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]


def main():
    registrations = json.loads((ROOT / 'catalogs/item_registrations.json').read_text(encoding='utf-8'))
    sprites = {x['name']: x for x in json.loads((ROOT / 'catalogs/sprites.json').read_text(encoding='utf-8'))}
    originals = list(Path(os.environ.get('PORTRAIT_SPRITE_DUMP', 'sprite-dump/sprites')).rglob('*.png'))
    folder = ROOT / 'packs/icon-examples'
    folder.mkdir(parents=True, exist_ok=True)
    items = {}
    candidates = sorted(registrations, key=lambda x: (x['id'] != 'graygoo_minigray', x['id']))
    for entry in candidates:
        if entry['registry_kind'] != 'GameItem' or not entry['direct_icons']:
            continue
        atlas, sprite = entry['direct_icons'][0]
        meta = sprites.get(sprite)
        if not meta:
            continue
        prefix = meta['asset'] + '_' + str(meta['path_id']) + '_'
        source = next((p for p in originals if p.name.startswith(prefix)), None)
        if not source:
            continue
        with Image.open(source) as raw:
            image = raw.convert('RGBA')
        factor = math.lcm(32 // math.gcd(image.width, 32), 32 // math.gcd(image.height, 32))
        image = image.resize((image.width * factor, image.height * factor), Image.Resampling.NEAREST)
        draw = ImageDraw.Draw(image)
        draw.rectangle((0, 0, image.width - 1, image.height - 1), outline='#62efd0', width=max(1, factor))
        filename = entry['id'] + '.png'
        image.save(folder / filename)
        items[entry['id']] = {'icon': filename, 'atlas': atlas, 'sprite': sprite}
        if len(items) == 5:
            break
    assert len(items) == 5
    pack = {'format': 'portrait-pack/1', 'id': 'icon-examples', 'name': '五件物品换图示范（绿色测试边框）',
            'author': 'PortraitRuntime / original game icon reference', 'license': 'Local testing only; original game assets retain their rights',
            'priority': 900, 'enabled': False, 'items': items}
    (folder / 'pack.json').write_text(json.dumps(pack, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'sampleItems': list(items), 'enabled': False}))


if __name__ == '__main__':
    main()
