#!/usr/bin/env python3
"""Execute the actual phase-one pure C# policies using bundled dotnet; never launches Unity."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, default=Path('/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app'))
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    sdk = args.unity / 'Contents/Resources/Scripting/DotNetSdk'
    compiler = next((sdk / 'sdk').glob('*/Roslyn/bincore/csc.dll'))
    refs = next((sdk / 'packs/Microsoft.NETCore.App.Ref').glob('*/ref/net8.0'))
    version = next((sdk / 'shared/Microsoft.NETCore.App').iterdir()).name
    with tempfile.TemporaryDirectory(prefix='kehai-phase1-rules-') as temp:
        output = Path(temp)
        dll = output / 'PhaseOneRulesCheck.dll'
        lines = ['-nostdlib+', '-target:exe', '-langversion:latest', '-out:"' + str(dll) + '"']
        lines += ['-reference:"' + str(ref) + '"' for ref in refs.glob('*.dll')]
        lines += ['"' + str(repo / file) + '"' for file in (
            'Assets/!_Project/_Game/AI/Scripts/World/PhaseOneRules.cs',
            'tools/tricks/PhaseOneRulesCheck.cs')]
        rsp = output / 'check.rsp'
        rsp.write_text('\n'.join(lines))
        subprocess.run([str(sdk / 'dotnet'), str(compiler), '@' + str(rsp)], check=True)
        dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
            'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': version}}}))
        subprocess.run([str(sdk / 'dotnet'), str(dll)], check=True)


if __name__ == '__main__':
    main()
