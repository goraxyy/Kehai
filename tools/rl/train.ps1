# Trains rung F's tactic chooser with PPO, on Windows (see train.sh and README.md).
#   powershell -ExecutionPolicy Bypass -File tools\rl\train.ps1 -Run karen_g1 [-Envs 8] [-Config karen_ppo.yaml] `
#       [-Player <folder\Kehai>] [-Extra --resume]
# -Player is the training player without ".exe"; one env per two CPU cores is a good start.
param(
    [Parameter(Mandatory = $true)][string]$Run,
    [int]$Envs = 4,
    [string]$Config = "$PSScriptRoot\karen_ppo.yaml",
    [string]$Player = "$PSScriptRoot\..\..\Builds\KehaiTrain\Windows\Kehai",
    [string[]]$Extra = @()
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot   # the configs' demo_path is relative to tools\rl

& "$PSScriptRoot\.venv\Scripts\mlagents-learn.exe" $Config --run-id $Run --env $Player --num-envs $Envs `
    --no-graphics --timeout-wait 180 --results-dir "$PSScriptRoot\results" @Extra `
    --env-args -kehai-ablation -ablation-rungs F -ablation-interleave -ablation-careers 100000 `
    -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records
