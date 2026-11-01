"""Build a no-artwork visitor catalog for the standalone runtime debug console."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return json.loads((ROOT / 'catalogs' / name).read_text(encoding='utf-8-sig'))


def main():
    methods = {x['type'] + '.' + x['method']: x for x in read('client_factories.json')}
    texts = {x['key']: x['text'] for x in read('localization.zh.json')}
    entries = []
    for r in read('client_registrations.json'):
        method = methods[r['factory']]
        ids = [x for x in method['identifier_literals'] if x and x != 'NO_ID']
        identity = r['registration_id'] if r['registration_id'] in ids else next(iter(ids), None)
        label = next((texts[x] for x in method['display_name_keys'] if x in texts), r['registration_id'])
        entries.append({'id': 'visit/' + r['registration_id'], 'label': label,
                        'registrationId': r['registration_id'], 'identity': identity, 'factory': r['factory'],
                        'spriteKey': next(iter(method['sprite_literals']), None),
                        'spriteExpressions': method['sprite_expressions']})
    (ROOT / 'catalogs/debug_visits.json').write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'debugVisits': len(entries), 'artworkFiles': 0}))


if __name__ == '__main__':
    main()
