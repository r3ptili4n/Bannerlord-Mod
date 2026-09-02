param(
  [string]$Branch = 'feature',
  [string]$Configuration = 'Release',
  [string]$MsBuildPath = '',
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Get-FullPath([string]$p) { [System.IO.Path]::GetFullPath($p) }

$DevRepo     = Split-Path $PSScriptRoot -Parent
$ModulesRoot = Split-Path (Split-Path $DevRepo -Parent) -Parent
$ShelfRoot   = Join-Path $ModulesRoot '_shelf'
$DevSlot     = Join-Path $ShelfRoot 'dev\SoldierBehaviorTweaks'
$Csproj      = Join-Path $DevRepo 'src\AIBannerFix.csproj'
$OutDir      = Join-Path $DevRepo 'bin\Win64_Shipping_Client'
$OutDll      = Join-Path $OutDir 'SoldierBehaviorTweaks.dll'
$OutPdb      = Join-Path $OutDir 'SoldierBehaviorTweaks.pdb'

function Find-MSBuild {
  if ($MsBuildPath) {
    if (Test-Path -LiteralPath $MsBuildPath) { return (Get-FullPath $MsBuildPath) }
    Write-Warning "指定的 MSBuild 不存在: $MsBuildPath"
  }
  $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
  if (Test-Path -LiteralPath $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
    if ($found) { return $found }
  }
  $known = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
  )
  foreach ($p in $known) { if (Test-Path -LiteralPath $p) { return $p } }
  return $null
}

# Git 分支处理
$git = Get-Command git -ErrorAction SilentlyContinue
$current = ''
$dirty = $false
if ($git) {
  $current = (git -C $DevRepo rev-parse --abbrev-ref HEAD 2>$null | Out-String).Trim()
  if ($LASTEXITCODE -eq 0 -and $current) {
    $dirty = ((git -C $DevRepo status --porcelain 2>$null | Out-String).Trim() -ne '')
  }
}

if (-not $SkipBuild -and $git -and $current) {
  if ($Branch -and $Branch -ne $current) {
    if ($dirty) {
      Write-Warning "工作区有未提交改动，跳过切换到 '$Branch'，直接编译当前分支 '$current'。"
    } else {
      git -C $DevRepo checkout $Branch
      if ($LASTEXITCODE -ne 0) { throw "切换分支失败: $Branch" }
      $current = $Branch
      Write-Host "已切换到分支: $current"
    }
  }
}
$branchLabel = if ($current) { $current } else { '未知(未检测到 git)' }
Write-Host "构建分支: $branchLabel"

# 编译
$msbuild = $null
if (-not $SkipBuild) { $msbuild = Find-MSBuild }

if ($SkipBuild) {
  Write-Warning "已指定 -SkipBuild，跳过编译，直接部署现有 DLL。"
} elseif (-not $msbuild) {
  Write-Warning "未找到 MSBuild / .NET SDK，跳过编译，直接部署现有 DLL（可先用 VS 编译，或加 -MsBuildPath）。"
}

if ($msbuild) {
  Write-Host "使用 MSBuild: $msbuild"
  Push-Location $DevRepo
  try {
    & $msbuild $Csproj /restore /t:Build /p:Configuration=$Configuration /p:Platform=x64 /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "MSBuild 退出码 $LASTEXITCODE" }
  } finally {
    Pop-Location
  }
  Write-Host '编译成功。'
}

if (-not (Test-Path -LiteralPath $OutDll)) {
  throw "没有可部署的 DLL：$OutDll（请先编译，或加 -MsBuildPath）"
}

# 部署到开发版待机槽
New-Item -ItemType Directory -Path (Join-Path $DevSlot 'bin\Win64_Shipping_Client') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $DevRepo 'SubModule.xml') -Destination (Join-Path $DevSlot 'SubModule.xml') -Force
Copy-Item -LiteralPath (Join-Path $DevRepo 'GUI') -Destination $DevSlot -Recurse -Force
Copy-Item -LiteralPath $OutDll -Destination (Join-Path $DevSlot 'bin\Win64_Shipping_Client\SoldierBehaviorTweaks.dll') -Force
if (Test-Path -LiteralPath $OutPdb) {
  Copy-Item -LiteralPath $OutPdb -Destination (Join-Path $DevSlot 'bin\Win64_Shipping_Client\SoldierBehaviorTweaks.pdb') -Force
}
$mcm = Join-Path $OutDir 'MCMv5.dll'
if (Test-Path -LiteralPath $mcm) {
  Copy-Item -LiteralPath $mcm -Destination (Join-Path $DevSlot 'bin\Win64_Shipping_Client\MCMv5.dll') -Force
}

Write-Host ''
Write-Host "已部署开发版到: $DevSlot"
Write-Host '要进入游戏测试开发版，运行:  .\tools\swap.ps1 dev'
Write-Host '测完切回长期版，运行:          .\tools\swap.ps1 stable'
