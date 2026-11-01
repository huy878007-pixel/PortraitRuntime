"""Build a no-artwork visitor catalog for the standalone runtime debug console."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

SPRITE_FIELDS = ('sprite_literals', 'sprite_expressions')
NAME_FIELDS = ('display_name_keys',)


def read(name):
    return json.loads((ROOT / 'catalogs' / name).read_text(encoding='utf-8-sig'))


def nearest(methods, name, fields, seen=()):
    """Follow same-type factory calls until a record carrying one of `fields` is reached.

    Many visitors are thin wrappers around a shared builder:
    CreateScavFoodSeller -> CreateScavGenericSeller, CreateLouBoxDrop -> CreateLouTalkClient,
    CreateWanted6Supply -> CreateLiReturnClient. The wrapper holds no sprite or name literal,
    so that information has to be inherited from the builder rather than reported as unknown.
    """
    method = methods.get(name)
    if method is None:
        return None
    if any(method.get(field) for field in fields):
        return method
    for call in method.get('calls') or []:
        target = method['type'] + '.' + call
        if target == name or target in seen:
            continue
        found = nearest(methods, target, fields, seen + (name,))
        if found is not None:
            return found
    return None


def main():
    methods = {x['type'] + '.' + x['method']: x for x in read('client_factories.json')}
    texts = {x['key']: x['text'] for x in read('localization.zh.json')}
    entries = []
    for r in read('client_registrations.json'):
        method = methods[r['factory']]
        sprite = nearest(methods, r['factory'], SPRITE_FIELDS) or method
        names = nearest(methods, r['factory'], NAME_FIELDS) or method
        ids = [x for x in method['identifier_literals'] if x and x != 'NO_ID']
        # Wrappers such as CreateLouTalkClient(string identifier) take the client identifier as a
        # parameter, so the registration ID is the real identifier for those.
        identity = r['registration_id'] if r['registration_id'] in ids else next(iter(ids), None) or r['registration_id']
        label = next((texts[x] for x in names['display_name_keys'] if x in texts), r['registration_id'])
        entries.append({'id': 'visit/' + r['registration_id'], 'label': label,
                        'registrationId': r['registration_id'], 'identity': identity, 'factory': r['factory'],
                        'spriteKey': next(iter(sprite['sprite_literals']), None),
                        'spriteExpressions': sprite['sprite_expressions']})
    (ROOT / 'catalogs/debug_visits.json').write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf-8')
    unknown = [x['id'] for x in entries if not x['spriteKey'] and not x['spriteExpressions']]
    print(json.dumps({'debugVisits': len(entries), 'artworkFiles': 0, 'withoutSpriteInfo': len(unknown)}))
    if unknown:
        print('still without sprite info:', unknown)


if __name__ == '__main__':
    main()
