# -*- coding: utf-8 -*-
"""Turn the offline audit into one page you can skim instead of touring 500 items in game."""
from __future__ import annotations

import base64
import html
import io
import json
from collections import defaultdict
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
AUDIT = ROOT / 'artifacts/offline-audit.json'
OUTPUT = ROOT / 'artifacts/offline-audit.html'
THUMB_WIDTH = 220


def thumb(path: Path) -> str:
    with Image.open(path) as image:
        image = image.convert('RGBA')
        height = max(1, round(image.height * THUMB_WIDTH / image.width))
        image = image.resize((THUMB_WIDTH, height), Image.LANCZOS)
        buffer = io.BytesIO()
        image.save(buffer, 'PNG', optimize=True)
    return 'data:image/png;base64,' + base64.b64encode(buffer.getvalue()).decode()


def main():
    report = json.loads(AUDIT.read_text(encoding='utf-8'))
    summary = report['summary']
    visits = report['visits']
    mapped = [row for row in visits if row['ok']]
    unmapped = [row for row in visits if not row['ok']]
    by_character = defaultdict(list)
    for row in mapped:
        by_character[row['character']].append(row)

    # one thumbnail per distinct image, shared by every row that shows it
    images: dict[str, str] = {}
    for row in mapped:
        pack = next(p for p in report['packs'])
        key = row['pack'] + '/' + row['path']
        if key in images:
            continue
        candidate = ROOT / 'packs' / pack / row['path']
        images[key] = thumb(candidate) if candidate.is_file() else ''

    def esc(value) -> str:
        return html.escape('' if value is None else str(value))

    parts = [f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<title>离线核对 · 每个客人会显示什么</title>
<style>
:root{{--bg:#0f1419;--panel:#171f27;--line:#2a3743;--ink:#e6edf3;--muted:#93a4b5;--ok:#4ec9a0;--warn:#e0b252}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 "Microsoft YaHei UI","Microsoft YaHei",sans-serif}}
main{{max-width:1180px;margin:auto;padding:28px}}
h1{{font-size:26px;margin:0 0 6px}}h2{{font-size:19px;margin:32px 0 10px;border-left:3px solid var(--ok);padding-left:10px}}
p{{margin:6px 0}}.muted{{color:var(--muted)}}
.cards{{display:flex;gap:12px;flex-wrap:wrap;margin:18px 0}}
.card{{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:12px 18px;min-width:150px}}
.card b{{display:block;font-size:24px}}
table{{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:8px;overflow:hidden}}
th,td{{padding:7px 10px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;font-size:14px}}
th{{background:#1d2732;font-weight:600}}.mono{{font-family:Consolas,monospace;font-size:12.5px;color:#bfcedb;word-break:break-all}}
.tag{{display:inline-block;padding:1px 8px;border-radius:99px;font-size:12px;border:1px solid}}
.tag.ok{{color:var(--ok);border-color:var(--ok)}}.tag.vanilla{{color:var(--muted);border-color:var(--line)}}
.girls{{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:14px}}
.girl{{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:10px}}
.girl img{{width:100%;background:#0b1015;border-radius:5px}}
.girl h3{{margin:8px 0 4px;font-size:15px}}.girl ul{{margin:4px 0 0;padding-left:18px;font-size:13px;color:var(--muted)}}
details{{margin:10px 0}}summary{{cursor:pointer;color:var(--warn)}}
</style><main>
<h1>离线核对 · 每个客人会显示什么</h1>
<p class="muted">用运行时同一套解析规则算出来的，不需要开游戏。生成于 {esc(report['generated'])}，数据包 {esc(', '.join(report['packs']))}。</p>
<div class="cards">
  <div class="card"><b>{summary['visits']}</b>来访注册项</div>
  <div class="card"><b style="color:var(--ok)">{summary['mapped']}</b>会换成娘化图</div>
  <div class="card"><b>{summary['unmapped']}</b>保持原版</div>
  <div class="card"><b>{summary['characters']}</b>人物定义</div>
  <div class="card"><b>{summary['storyLines']}</b>剧情配图</div>
  <div class="card"><b style="color:{'var(--ok)' if summary['brokenStoryImages'] == 0 else 'var(--warn)'}">{summary['brokenStoryImages']}</b>坏图</div>
</div>
<p class="muted">“保持原版”不等于错误：通用路人（拾荒者、下层区公民、随机供货商等）本来就没有具名角色，游戏给他们随机外观，这里也照旧。</p>
''']

    parts.append('<h2>会换图的客人</h2><div class="girls">')
    for character, rows in sorted(by_character.items()):
        first = rows[0]
        source = images.get(first['pack'] + '/' + first['path'], '')
        names = ''.join(f'<li>{esc(r["label"])} <span class="mono">{esc(r["id"])}</span></li>' for r in rows)
        parts.append(f'''<div class="girl"><img src="{source}" alt="{esc(character)}">
<h3>{esc(character)} <span class="muted mono">{esc(first['pack'])}/{esc(first['path'])}</span></h3>
<ul>{names}</ul></div>''')
    parts.append('</div>')

    parts.append('<h2>人物与精灵名覆盖</h2>'
                 '<p class="muted">「原版名」是游戏取图时真正使用的名字（例如 lun、Mentor）。这一列必须全部命中，'
                 '否则入场剪影会退回原版。</p><table><tr><th>人物</th><th>图片</th><th>原版名</th><th>variant 键</th><th>结果</th></tr>')
    for character in report['characters']:
        base_ok = character['baseHit']
        parts.append(f'<tr><td>{esc(character["id"])}</td><td class="mono">{esc(character["base"])}</td>'
                     f'<td class="mono">{esc(character["baseStem"])}</td><td class="mono">—</td>'
                     f'<td>{"✔" if base_ok else "✘ 未命中"}</td></tr>')
        for variant in character['variants']:
            parts.append(f'<tr><td class="muted">↳</td><td class="mono">{esc(variant["path"])}</td>'
                         f'<td class="mono">{esc(variant["key"])}</td><td class="mono">{esc(variant["key"])}</td>'
                         f'<td>{"✔" if variant["keyHit"] and variant["stemHit"] else "✘"}</td></tr>')
    parts.append('</table>')

    parts.append(f'<h2>保持原版的客人（{len(unmapped)}）</h2>'
                 '<p class="muted">按创建工厂分组。这些工厂没有声明具名角色，运行时会显示游戏自己的随机外观。</p>')
    parts.append('<details><summary>展开完整列表</summary><table><tr><th>条目</th><th>工厂</th><th>原因</th></tr>')
    for row in unmapped:
        parts.append(f'<tr><td class="mono">{esc(row["id"])}</td><td class="mono">{esc(row["factory"])}</td>'
                     f'<td>{esc(row["reason"])}</td></tr>')
    parts.append('</table></details>')

    problems = report['storyProblems']
    parts.append(f'<h2>剧情配图问题（{len(problems)}）</h2>')
    if problems:
        parts.append('<table><tr><th>人物</th><th>台词 key</th><th>图片</th><th>问题</th></tr>')
        for row in problems:
            parts.append(f'<tr><td>{esc(row["character"])}</td><td class="mono">{esc(row["key"])}</td>'
                         f'<td class="mono">{esc(row["path"])}</td><td>{esc(row["problem"])}</td></tr>')
        parts.append('</table>')
    else:
        parts.append('<p>全部 669 条剧情配图的文件都在、都能解码、尺寸合规。</p>')

    parts.append('</main></html>')
    OUTPUT.write_text(''.join(parts), encoding='utf-8')
    print(json.dumps({'output': str(OUTPUT), 'bytes': OUTPUT.stat().st_size,
                      'mapped': summary['mapped'], 'unmapped': summary['unmapped'],
                      'thumbnails': len(images)}, ensure_ascii=True))


if __name__ == '__main__':
    main()
