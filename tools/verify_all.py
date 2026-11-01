"""Run the complete offline acceptance pipeline; never launch the game."""
from __future__ import annotations

import hashlib
import json
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'artifacts'


def main():
    ART.mkdir(exist_ok=True)
    records = []
    game_assembly = Path(r'D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll')
    expected_hash = '6398d44aa1675e1f02b63bc668868f970e0f5d3ae5a50f2aced8d06ca084e3b5'
    assert hashlib.sha256(game_assembly.read_bytes()).hexdigest() == expected_hash, 'Game assembly changed; re-audit before building'

    def run(name, command, expected=0):
        start = time.monotonic()
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace', check=False)
        elapsed = round(time.monotonic() - start, 3)
        (ART / (name + '.log')).write_text(result.stdout + '\n' + result.stderr, encoding='utf-8')
        records.append({'name': name, 'command': command, 'exit': result.returncode, 'expectedExit': expected, 'seconds': elapsed})
        print(f'{name}: exit={result.returncode} ({elapsed}s)', flush=True)
        if result.returncode != expected:
            raise RuntimeError(name + '\n' + result.stdout + result.stderr)
        return result

    run('runtime-build', ['dotnet', 'build', 'src/PortraitRuntime.Unity', '-c', 'Release', '--nologo'])
    run('cli-build', ['dotnet', 'build', 'src/PortraitRuntime.Cli', '-c', 'Release', '--nologo'])
    run('core-tests', ['dotnet', 'run', '--project', 'tests/PortraitRuntime.Tests', '-c', 'Release'])
    run('python-lint', ['python', '-m', 'ruff', 'check', '--isolated', '--select', 'E,F', '--ignore', 'E501', 'tools', 'tests/workbench_test.py'])
    run('workbench-syntax', ['node', '--check', 'workbench/app.js'])
    run('workbench-browser', ['python', 'tests/workbench_test.py'])
    cli = ['dotnet', str(ROOT / 'src/PortraitRuntime.Cli/bin/Release/net10.0/validate_pack.dll')]
    run('workbench-export-validation', cli + ['validate', str(ART / 'workbench-export'), 'catalogs/dialogue_keys.json'])
    stamp = str(time.time_ns())
    state = ART / ('simulation-' + stamp + '.json')
    run('simulate-accept', cli + ['simulate', 'packs/demo', 'demo.courier', str(state), '2', '0'])
    run('simulate-reload-return', cli + ['simulate', 'packs/demo', 'demo.return', str(state), '3', '0'])
    saved = json.loads(state.read_text(encoding='utf-8'))
    assert saved['Cash'] == 120 and saved['Inventory']['demo.signal'] == 1 and saved['State']['demo/finished'] == 'yes'
    before = hashlib.sha256(state.read_bytes()).hexdigest()
    run('simulate-duplicate-rejected', cli + ['simulate', 'packs/demo', 'demo.return', str(state), '3', '0'], expected=1)
    assert hashlib.sha256(state.read_bytes()).hexdigest() == before
    refusal = ART / ('refusal-' + stamp + '.json')
    run('simulate-refusal', cli + ['simulate', 'packs/demo', 'demo.courier', str(refusal), '2', '1'])
    run('simulate-refused-return-rejected', cli + ['simulate', 'packs/demo', 'demo.return', str(refusal), '3', '0'], expected=1)
    run('package', ['python', 'tools/build_pack.py'])
    run('native-contracts', ['dotnet', 'run', '--project', 'tests/NativeContracts', '-c', 'Release', '--', 'dist/player.portraitruntime'])
    run('publish-descriptor', ['python', 'tools/publish.py'])
    report = {'passed': True, 'gameAssemblySha256': expected_hash, 'gameLaunched': False, 'gameRuntimeTested': False, 'steps': records,
              'simulationAfterReload': {'cash': saved['Cash'], 'items': saved['Inventory']},
              'manualAcceptance': ['actual portraits and departure', 'native choice UI', 'native trading and inventory',
                                   'real game save and reload', 'settings input and fonts', '30 minute memory stability']}
    (ART / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('OFFLINE ACCEPTANCE PASSED; LIVE GAME ACCEPTANCE PENDING USER', flush=True)


if __name__ == '__main__':
    main()
