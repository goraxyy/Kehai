#!/usr/bin/env python3
"""Run production edition/career/guide pure NUnit tests with bundled dotnet; never starts Unity."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, default=Path('/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app'))
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    contents = args.unity / 'Contents'
    sdk = contents / 'Resources/Scripting/DotNetSdk'
    compiler = next((sdk / 'sdk').glob('*/Roslyn/bincore/csc.dll'))
    refs = list((sdk / 'packs/Microsoft.NETCore.App.Ref').glob('*/ref/net8.0/*.dll'))
    nunit = contents / 'Resources/PackageManager/BuiltInPackages/com.unity.ext.nunit/net472/unity-custom/nunit.framework.dll'
    core = repo / 'Assets/!_Project/_Core/Scripts/Runtime'
    sources = [core / name for name in ('EditionRules.cs', 'CareerProgress.cs', 'FirstShiftGuideState.cs')]
    sources += [repo / 'Assets/!_Project/_Tests/Editor/EditionCareerGuideTests.cs']
    with tempfile.TemporaryDirectory(prefix='kehai-phase67-') as scratch:
        folder = Path(scratch)
        runner = folder / 'Runner.cs'
        runner.write_text('''using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
class Runner {
 static int Main() {
  int passed = 0, failed = 0;
  var fixture = new EditionCareerGuideTests();
  foreach (var method in typeof(EditionCareerGuideTests).GetMethods().Where(m => m.IsDefined(typeof(TestAttribute)))) {
   try { method.Invoke(fixture, null); Console.WriteLine("PASS " + method.Name); passed++; }
   catch (TargetInvocationException e) { Console.WriteLine("FAIL " + method.Name + ": " + e.InnerException); failed++; }
  }
  Console.WriteLine($"{passed} passed, {failed} failed (pure logic only; no Unity runtime).");
  return failed == 0 ? 0 : 1;
 }
}''')
        lines = ['-nologo', '-target:exe', '-nostdlib+', '-out:"' + str(folder / 'check.dll') + '"']
        lines += ['-reference:"' + str(p) + '"' for p in refs + [nunit]]
        lines += ['"' + str(p) + '"' for p in sources + [runner]]
        rsp = folder / 'check.rsp'; rsp.write_text('\n'.join(lines))
        subprocess.run([str(sdk / 'dotnet'), str(compiler), '@' + str(rsp)], check=True)
        shutil.copy2(nunit, folder / nunit.name)
        (folder / 'check.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0',
            'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}, 'rollForward': 'LatestPatch'}}))
        subprocess.run([str(sdk / 'dotnet'), str(folder / 'check.dll')], check=True)


if __name__ == '__main__':
    main()
