"""Compile and execute pure C# test probes with the installed .NET SDK, without Unity."""
import json
from pathlib import Path
import re
import shutil
import subprocess


def run(sources, output):
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("an installed .NET SDK is required for C# offline probes")
    entries = subprocess.check_output([dotnet, "--list-sdks"], text=True).splitlines()
    version, base = re.match(r"(\S+) \[(.*)\]", entries[-1]).groups()
    sdk = Path(base) / version
    major = version.split(".")[0]
    pack = sorted((sdk.parent.parent / "packs/Microsoft.NETCore.App.Ref").glob(major+".*"))[-1]
    references = list((pack / "ref" / ("net"+major+".0")).glob("*.dll"))
    output = Path(output)
    assembly = output / "Probe.dll"
    rsp = output / "probe.rsp"
    rsp.write_text('\n'.join(['-nologo', '-nostdlib+', '-langversion:latest', '-target:exe', '-out:"'+str(assembly)+'"']
                             + ['-reference:"'+str(p)+'"' for p in references]
                             + ['"'+str(p)+'"' for p in sources]))
    result = subprocess.run([dotnet, str(sdk / "Roslyn/bincore/csc.dll"), '@'+str(rsp)], capture_output=True, text=True)
    if result.returncode:
        raise AssertionError(result.stdout + result.stderr)
    (output / "Probe.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": "net"+major+".0", "framework": {"name": "Microsoft.NETCore.App", "version": major+".0.0"}}}))
    result = subprocess.run([dotnet, str(assembly)], capture_output=True, text=True)
    if result.returncode:
        raise AssertionError(result.stdout + result.stderr)
    return result.stdout
