# Records rung E's tactic choices as demonstrations, on Windows (see record_demos.sh).
#   powershell -ExecutionPolicy Bypass -File tools\rl\record_demos.ps1 [-Steps 2000] [-Player <folder\Kehai.exe>]
param(
    [int]$Steps = 2000,
    [string]$Player = "$PSScriptRoot\..\..\Builds\KehaiTrain\Windows\Kehai.exe"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Player)) { throw "No training player at $Player — unzip Windows.zip and pass -Player." }

New-Item -ItemType Directory -Force "$PSScriptRoot\demos" | Out-Null
& $Player -batchmode -nographics -logFile "$PSScriptRoot\demos\record.log" `
    -kehai-ablation -ablation-rungs F -ablation-interleave -ablation-careers 1000 `
    -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records `
    -karen-record-demos "$PSScriptRoot\demos" -karen-demo-steps $Steps | Out-Null
Get-ChildItem "$PSScriptRoot\demos\*.demo"
