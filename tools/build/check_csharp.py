#!/usr/bin/env python3
"""Compile source without importing a Unity project. This is not a Unity test runner."""
import argparse
from pathlib import Path
import subprocess
import tempfile


def compile_sources(repo, unity, cached_project, output, ml=False):
    contents = unity / 'Contents' if (unity / 'Contents').is_dir() else unity
    scripting = contents / 'Resources/Scripting'
    sdk = scripting / 'DotNetSdk'
    compiler = next((sdk / 'sdk').glob('*/Roslyn/bincore/csc.dll'))
    references = list((scripting / 'NetStandard/ref/2.1.0').glob('*.dll'))
    references += list((scripting / 'NetStandard/compat/2.1.0/shims/netfx').glob('*.dll'))
    references += list((scripting / 'Managed/UnityEngine').glob('*.dll'))
    references += list((scripting / 'Managed').glob('*.dll'))
    references += [p for p in (cached_project / 'Library/ScriptAssemblies').glob('*.dll')
                   if not p.name.startswith('Assembly-CSharp')]
    # Unity packages include assembly definitions; use their already compiled references.
    refs = {}
    for p in references:
        refs.setdefault(p.name, p)
    if not (cached_project / 'Library/ScriptAssemblies').is_dir():
        raise SystemExit('An imported reference project is required; no cache was found')
    sources = [p for p in (repo / 'Assets/!_Project').rglob('*.cs')
               if 'Editor' not in p.parts and '_Tests' not in p.parts]
    defines = 'UNITY_EDITOR;UNITY_EDITOR_OSX;UNITY_6000_0_OR_NEWER;UNITY_2023_1_OR_NEWER;ENABLE_INPUT_SYSTEM;ENABLE_LEGACY_INPUT_MANAGER'
    if ml:
        defines += ';KAREN_MLAGENTS'
    output.mkdir(parents=True, exist_ok=True)
    rsp = output / 'runtime.rsp'
    lines = ['-nostdlib+', '-target:library', '-langversion:latest', '-unsafe+',
             '-define:' + defines, '-out:"' + str(output / 'Assembly-CSharp.dll') + '"']
    lines += ['-reference:"' + str(p) + '"' for p in refs.values()]
    lines += ['"' + str(p) + '"' for p in sources]
    rsp.write_text('\n'.join(lines))
    subprocess.run([str(sdk / 'dotnet'), str(compiler), '@' + str(rsp)], check=True)
    editor_sources = [p for p in (repo / 'Assets/!_Project').rglob('*.cs') if 'Editor' in p.parts]
    nunit = contents / 'Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net472/unity-custom/nunit.framework.dll'
    lines = [line for line in lines if not line.startswith('-out:') and not (line.startswith('"') and line.endswith('.cs"'))]
    lines += ['-out:"' + str(output / 'Assembly-CSharp-Editor.dll') + '"',
              '-reference:"' + str(output / 'Assembly-CSharp.dll') + '"', '-reference:"' + str(nunit) + '"']
    lines += ['"' + str(p) + '"' for p in editor_sources]
    rsp = output / 'editor.rsp'; rsp.write_text('\n'.join(lines))
    subprocess.run([str(sdk / 'dotnet'), str(compiler), '@' + str(rsp)], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', required=True, type=Path, help='Unity.app path')
    parser.add_argument('--references', required=True, type=Path, help='Existing imported project (read only)')
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--ml', action='store_true')
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='kehai-compile-') as output:
        compile_sources(args.repo, args.unity, args.references, Path(output), args.ml)


if __name__ == '__main__':
    main()
