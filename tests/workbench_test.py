"""Exercise the offline editor in a separate headless browser."""
from __future__ import annotations

import json
import zipfile
from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts'


def main():
    ARTIFACTS.mkdir(exist_ok=True)
    errors = []
    with sync_playwright() as p:
        browser = p.chromium.launch(channel='chrome', headless=True)
        page = browser.new_page(viewport={'width': 1500, 'height': 950}, accept_downloads=True)
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto((ROOT / 'workbench/index.html').as_uri())
        page.wait_for_load_state('networkidle')
        assert page.locator('#list .row').count() == 276
        page.screenshot(path=str(ARTIFACTS / 'workbench-overview.png'), full_page=True)

        def add(identifier):
            page.once('dialog', lambda dialog: dialog.accept(identifier))
            page.locator('#add').click()

        def image(path):
            with page.expect_file_chooser() as chooser:
                page.get_by_role('button', name='选择 PNG 图片').click()
            chooser.value.set_files(str(path))
            page.wait_for_selector('img.preview')

        add('courier')
        image(ROOT / 'packs/demo/courier.png')
        page.screenshot(path=str(ARTIFACTS / 'workbench-character.png'), full_page=True)
        page.get_by_role('button', name='物品与图标').click()
        add('mypack.signal')
        page.get_by_label('物品名称', exact=True).fill('测试信号片')
        page.get_by_label('物品名称', exact=True).press('Tab')
        image(ROOT / 'packs/demo/signal.png')
        page.get_by_role('button', name='剧情与分支').click()
        add('first')
        page.get_by_label('台词 · start').fill('测试剧情的第一句')
        page.get_by_label('台词 · start').press('Tab')
        page.once('dialog', lambda dialog: dialog.accept('accept'))
        page.get_by_role('button', name='＋ 添加节点').click()
        page.locator('.node').first.get_by_role('checkbox').uncheck()
        page.get_by_label('选项 JSON · start').fill('[{"text":"接受","next":"accept"}]')
        page.get_by_label('选项 JSON · start').press('Tab')
        page.get_by_label('结果动作 JSON · accept').fill('[{"op":"giveItem","key":"mypack.signal","amount":1}]')
        page.get_by_label('结果动作 JSON · accept').press('Tab')
        page.get_by_role('button', name='新增来店人物').click()
        add('mypack.visitor')
        page.reload()
        page.wait_for_load_state('networkidle')
        page.get_by_role('button', name='人物与立绘').click()
        page.locator('#list .row').first.click()
        page.wait_for_selector('img.preview')
        assert page.locator('img.preview').evaluate('(i)=>i.naturalWidth') == 1152
        page.locator('#check').click()
        page.wait_for_selector('dialog[open]')
        assert '基本检查通过' in page.locator('#errors pre').inner_text()
        page.locator('#close-errors').click()
        with page.expect_download() as download:
            page.locator('#export').click()
        output = ARTIFACTS / 'workbench-export.zip'
        download.value.save_as(output)
        page.set_viewport_size({'width': 760, 'height': 1000})
        page.screenshot(path=str(ARTIFACTS / 'workbench-mobile.png'), full_page=True)
        assert not errors, errors
        browser.close()
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        data = json.loads(archive.read('pack.json'))
        assert data['items']['mypack.signal']['create']
        assert data['stories']['first']['nodes']['start']['choices'][0]['next'] == 'accept'
        assert len(archive.namelist()) == 3
        archive.extractall(ARTIFACTS / 'workbench-export')
    report = {'passed': True, 'browser': 'headless Chrome', 'origin': 'file://', 'consoleErrors': errors,
              'checks': ['native catalogs', 'character image', 'item icon', 'branch editing', 'actor association',
                         'metadata and image reload persistence', 'ZIP download and CRC', 'responsive render']}
    (ARTIFACTS / 'workbench-test.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=True))


if __name__ == '__main__':
    main()
