$ErrorActionPreference = 'Stop'

# 版本号唯一来源：Directory.Build.props 的 <Version>
# - 推送 tag（v1.2.3）→ 正式版，标签即版本号
# - push main / 手动触发 → v{Version}-preview.{run_number}
$isTag = $env:GITHUB_REF_TYPE -eq 'tag'

if ($isTag -and $env:GITHUB_REF_NAME -match '^v\d+\.\d+\.\d+([-.].*)?$') {
    $tag = $env:GITHUB_REF_NAME
} else {
    [xml]$props = Get-Content (Join-Path $PSScriptRoot '..\Directory.Build.props')
    $baseVersion = $props.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($baseVersion)) {
        throw 'Directory.Build.props 缺少 <Version> 节点。'
    }

    $runNumber = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { '0' }
    $tag = "v${baseVersion}-preview.${runNumber}"
}

$isRelease = if ($tag -match '-') { 'false' } else { 'true' }

"VERSION_TAG=$tag" >> $env:GITHUB_ENV
"IS_RELEASE=$isRelease" >> $env:GITHUB_ENV

Write-Host "Version: $tag (full-release=$isRelease)"
