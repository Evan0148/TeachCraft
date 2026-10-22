<#
.SYNOPSIS
    知微 · TeachCraft - Windows 安装包 (Setup.exe) 构建脚本
.DESCRIPTION
    执行 dotnet publish 并通过 Inno Setup 编译生成单文件安装向导 (.exe)。
.PARAMETER Rid
    目标平台架构：win-x64 (默认), win-x86, win-arm64
.PARAMETER Version
    版本号标签，如 1.0.0 或 1.0.0-preview.25
.PARAMETER Deployment
    部署模式：self-contained (独立运行时，默认) 或 framework-dependent (依赖框架)
.PARAMETER SkipPublish
    跳过 dotnet publish 步骤，直接使用已有 dist 产物打包
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Rid = "win-x64",

    [string]$Version = "1.0.0",

    [ValidateSet("self-contained", "framework-dependent")]
    [string]$Deployment = "self-contained",

    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  知微 · TeachCraft - Setup 安装包构建工具" -ForegroundColor Cyan
Write-Host "  架构: $Rid | 模式: $Deployment | 版本: $Version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. 查找 Inno Setup 编译器 (ISCC.exe)
$isccPath = $null
$candidates = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue)?.Source,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) {
        $isccPath = $c
        break
    }
}

if (-not $isccPath) {
    if (Get-Command choco.exe -ErrorAction SilentlyContinue) {
        Write-Host "未检测到本地 ISCC，正在尝试通过 Chocolatey 自动安装 Inno Setup..." -ForegroundColor Yellow
        & choco install innosetup --no-progress -y
        foreach ($c in $candidates) {
            if ($c -and (Test-Path $c)) {
                $isccPath = $c
                break
            }
        }
    }
}

if (-not $isccPath) {
    Write-Error @"
未找到 Inno Setup 编译器 (ISCC.exe)。
请先安装 Inno Setup 6：
  • 通过 winget: winget install JRSoftware.InnoSetup
  • 通过 chocolatey: choco install innosetup
  • 或前往官网下载: https://jrsoftware.org/isinfo.php
"@
    exit 1
}

Write-Host "[1/3] 找到 Inno Setup 编译器: $isccPath" -ForegroundColor Green

# 2. 准备发布目录与构建产物
$modeLabel = if ($Deployment -eq "self-contained") { "sc" } else { "fdd" }
$isSelfContained = ($Deployment -eq "self-contained")
$publishDir = "dist/TeachCraft-$Rid-$modeLabel"
$projectPath = "src/TeachCraft.App/TeachCraft.App.csproj"

if (-not $SkipPublish) {
    Write-Host "[2/3] 正在发布应用 ($Rid · $Deployment)..." -ForegroundColor Yellow
    dotnet publish $projectPath `
        --configuration Release `
        --runtime $Rid `
        --self-contained $isSelfContained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish 失败！"
        exit $LASTEXITCODE
    }
} else {
    Write-Host "[2/3] 跳过发布，使用既有目录: $publishDir" -ForegroundColor Yellow
    if (-not (Test-Path $publishDir)) {
        Write-Error "目录不存在: $publishDir，无法跳过发布！"
        exit 1
    }
}

# 删掉调试符号，避免安装包被 PDB 撑大
Get-ChildItem -Path $publishDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

# 3. 执行 Inno Setup 打包
Write-Host "[3/3] 正在生成 Setup 安装包..." -ForegroundColor Yellow

$arch = switch ($Rid) {
    "win-x64" { "x64" }
    "win-arm64" { "arm64" }
    default { "x86" }
}

$cleanVer = "1.0.0.0"
if ($Version -match '^v?(\d+\.\d+\.\d+)') {
    $cleanVer = "$($Matches[1]).0"
}

$outputDir = "artifacts"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$versionClean = $Version.TrimStart('v')
$outputBaseName = "TeachCraft-v$versionClean-$Rid-$modeLabel-Setup"
$fullPublishDir = (Resolve-Path $publishDir).Path
$fullOutputDir = (Resolve-Path $outputDir).Path
$issScript = "installer/TeachCraft.iss"

$isccArgs = @(
    "/Qp",
    "/DMyAppVersion=$Version",
    "/DMyAppBaseVersion=$cleanVer",
    "/DSourceDir=$fullPublishDir",
    "/DOutputDir=$fullOutputDir",
    "/DOutputBaseFilename=$outputBaseName",
    "/DArch=$arch",
    $issScript
)

& $isccPath $isccArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup 编译失败！"
    exit $LASTEXITCODE
}

$setupFile = Join-Path $outputDir "$outputBaseName.exe"
if (Test-Path $setupFile) {
    $item = Get-Item $setupFile
    $sizeMb = [math]::Round($item.Length / 1MB, 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  安装包构建成功！" -ForegroundColor Green
    Write-Host "  产物路径: $($item.FullName)" -ForegroundColor White
    Write-Host "  文件大小: $sizeMb MB" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Error "未找到预期的输出文件: $setupFile"
    exit 1
}
