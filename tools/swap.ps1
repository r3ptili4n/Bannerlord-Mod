param(
  [Parameter(Position = 0)]
  [ValidateSet('stable','dev','status')]
  [string]$Target = 'status'
)

$ErrorActionPreference = 'Stop'

$DevRepo     = Split-Path $PSScriptRoot -Parent
$ModulesRoot = Split-Path (Split-Path $DevRepo -Parent) -Parent
$ActiveSlot  = Join-Path $ModulesRoot 'SoldierBehaviorTweaks'
$ShelfRoot   = Join-Path $ModulesRoot '_shelf'
$MarkerFile  = Join-Path $ShelfRoot '.active.txt'

function Get-FullPath([string]$p) { [System.IO.Path]::GetFullPath($p) }

function Assert-UnderModules([string]$p) {
  $root = Get-FullPath $ModulesRoot
  $full = Get-FullPath $p
  if ($full -ne $root -and -not $full.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝操作 Modules 之外的路径: $full"
  }
}

function Get-Slot([string]$name) { Join-Path $ShelfRoot "$name\SoldierBehaviorTweaks" }

function Test-Slot([string]$name) {
  $s = Get-Slot $name
  return ((Test-Path -LiteralPath (Join-Path $s 'SubModule.xml')) -and
          (Test-Path -LiteralPath (Join-Path $s 'bin\Win64_Shipping_Client\SoldierBehaviorTweaks.dll')))
}

function Get-CurrentActive {
  if (Test-Path -LiteralPath $MarkerFile) {
    $c = (Get-Content -LiteralPath $MarkerFile -Raw).Trim()
    if ($c -in @('stable','dev')) { return $c }
  }
  return 'stable'
}

function Show-Status {
  $cur = Get-CurrentActive
  Write-Host ''
  Write-Host "当前启用: $cur"
  Write-Host "启用槽(游戏实际加载): $ActiveSlot"
  foreach ($n in @('stable','dev')) {
    $state = if (Test-Slot $n) { '完整' } else { '缺失/不完整' }
    Write-Host ("  待机槽 {0,-6}: {1}  ->  {2}" -f $n, $state, (Get-Slot $n))
  }
  Write-Host ''
}

if ($Target -eq 'status') { Show-Status; exit 0 }

$cur = Get-CurrentActive
if ($cur -eq $Target) {
  Write-Host ("目标 '{0}' 已是当前启用版本，无需切换。" -f $Target)
  Show-Status
  exit 0
}

if (-not (Test-Slot $Target)) {
  Write-Error ("目标待机槽 '{0}' 缺失或不完整：{1}" -f $Target, (Get-Slot $Target))
  exit 1
}
if (-not (Test-Path -LiteralPath $ActiveSlot)) {
  Write-Error "启用槽不存在：$ActiveSlot"
  exit 1
}

New-Item -ItemType Directory -Path $ShelfRoot -Force | Out-Null

$curSlot    = Get-Slot $cur
$targetSlot = Get-Slot $Target

# 1) 当前启用版放回它自己的待机槽
Assert-UnderModules $ActiveSlot
Assert-UnderModules $curSlot
if (Test-Path -LiteralPath $curSlot) {
  Remove-Item -LiteralPath $curSlot -Recurse -Force
}
New-Item -ItemType Directory -Path (Split-Path $curSlot -Parent) -Force | Out-Null
Move-Item -LiteralPath $ActiveSlot -Destination $curSlot

# 2) 目标待机槽提升为启用版
Assert-UnderModules $targetSlot
Assert-UnderModules $ActiveSlot
Move-Item -LiteralPath $targetSlot -Destination $ActiveSlot

# 3) 记录当前启用
Set-Content -LiteralPath $MarkerFile -Value $Target -Encoding UTF8

Write-Host ("已切换: {0} -> {1}" -f $cur, $Target)
Show-Status
