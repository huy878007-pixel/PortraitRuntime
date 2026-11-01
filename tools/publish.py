"""Prepare a private Workshop upload descriptor; do not upload or log in."""
from __future__ import annotations

import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--content', type=Path, default=ROOT / 'dist/player.portraitruntime')
    parser.add_argument('--preview', type=Path)
    parser.add_argument('--description', type=Path)
    parser.add_argument('--title', default='深空内容核心库')
    parser.add_argument('--workshop-id', type=int, default=0)
    args = parser.parse_args()
    description = args.description.read_text(encoding='utf-8') if args.description else '人物、剧情、物品与内容包核心库。测试版本，游戏内验收待完成。'
    if len(description.encode('utf-8')) > 8000 or '"' in description or '"' in args.title:
        raise ValueError('Description must be <=8000 UTF-8 bytes; English double quotes are not allowed')
    if not (args.content / 'manifest.xml').is_file():
        raise ValueError('Content folder is missing manifest.xml')
    if args.preview and args.preview.stat().st_size >= 1024 * 1024:
        raise ValueError('Preview must be smaller than 1 MiB')
    values = {'appid': '4348910', 'publishedfileid': str(args.workshop_id),
              'contentfolder': args.content.resolve().as_posix(),
              'previewfile': args.preview.resolve().as_posix() if args.preview else '',
              'visibility': '2', 'title': args.title,
              'description': description.replace('\r', '').replace('\n', r'\n'),
              'changenote': 'Local test build; awaiting game acceptance'}
    output = ROOT / 'artifacts/workshop_item.vdf'
    output.parent.mkdir(exist_ok=True)
    output.write_text('"workshopitem"\n{\n' + ''.join(f'    "{k}" "{v}"\n' for k, v in values.items()) + '}\n', encoding='utf-8')
    print('Prepared private upload descriptor: ' + str(output))
    print('No upload performed.')


if __name__ == '__main__':
    main()
