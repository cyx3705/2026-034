[CmdletBinding()]
param(
    [switch]$Instantiation
)

# 模板项目的合同校验入口（派生项目会原样继承本文件）。
#
# 规则本体不在这里。体系内所有模块共用 HistoryDiana 持有的那一份
# （b-Code/OneHistory.ModuleContract.ps1），本文件只负责定位并转发。
#
# 模板尤其不能保留规则副本：模板一旦带着一份规则被派生 N 次，就等于一次性
# 制造 N 份必然漂移的副本。派生项目需要差异时改 project.manifest.json 的
# contract 节，不要改本文件。

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dianaEntry = [IO.Path]::GetFullPath(
    (Join-Path $projectRoot '..\2026-019-HistoryDiana\b-Code\OneHistory.ModuleContract.ps1'))

if (-not (Test-Path -LiteralPath $dianaEntry -PathType Leaf)) {
    Write-Host "Shared module contract is missing: $dianaEntry" -ForegroundColor Red
    Write-Host 'Expected HistoryDiana to be checked out alongside this project.' -ForegroundColor Red
    exit 2
}

& $dianaEntry -ProjectRoot $projectRoot -Instantiation:$Instantiation
exit $LASTEXITCODE
