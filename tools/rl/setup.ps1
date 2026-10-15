# Makes tools\rl\.venv: Python 3.10.12 and the ML-Agents trainer (README.md), on Windows.
#   powershell -ExecutionPolicy Bypass -File tools\rl\setup.ps1
# Uses uv when it's installed (it fetches Python 3.10.12 itself, no admin rights needed),
# else the py launcher with Python 3.10.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (Get-Command uv -ErrorAction SilentlyContinue) {
    uv venv --python 3.10.12 --allow-existing .venv
    uv pip install --python .venv\Scripts\python.exe -r requirements.txt
}
elseif (Get-Command py -ErrorAction SilentlyContinue) {
    py -3.10 -m venv .venv
    .venv\Scripts\python.exe -m pip install --upgrade pip
    .venv\Scripts\python.exe -m pip install -r requirements.txt
}
else {
    throw "Needs uv or Python 3.10.12 (x86-64). Without admin rights: powershell -ExecutionPolicy Bypass -c `"irm https://astral.sh/uv/install.ps1 | iex`", then run this again."
}

& .venv\Scripts\mlagents-learn.exe --help | Out-Null
Write-Host "Ready: $PSScriptRoot\.venv\Scripts\mlagents-learn.exe"
