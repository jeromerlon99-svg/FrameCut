param(
    [string]$OutputDirectory,
    [string]$EngineDirectory
)

$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoDirectory 'build\FrameCut'
}
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)

& (Join-Path $repoDirectory 'src\build.ps1') $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }

Copy-Item -LiteralPath (Join-Path $repoDirectory 'assets\demo.mp4') -Destination (Join-Path $outputPath '示例视频.mp4') -Force
Copy-Item -LiteralPath (Join-Path $repoDirectory 'LICENSE') -Destination (Join-Path $outputPath 'LICENSE-FrameCut.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoDirectory 'docs\USER-GUIDE.zh-CN.txt') -Destination (Join-Path $outputPath '使用说明.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoDirectory 'THIRD-PARTY.md') -Destination $outputPath -Force

if (![string]::IsNullOrWhiteSpace($EngineDirectory)) {
    $engineSource = (Resolve-Path -LiteralPath $EngineDirectory).Path
    foreach ($requiredFile in @('ffmpeg.exe', 'ffprobe.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $engineSource $requiredFile))) {
            throw "The supplied engine directory is missing $requiredFile."
        }
    }
    $licenseFiles = @(Get-ChildItem -LiteralPath $engineSource -File | Where-Object {
        $_.Name -match '^(LICENSE|COPYING|NOTICE)'
    })
    if ($licenseFiles.Count -eq 0) {
        throw 'Keep the FFmpeg license files with the engine before copying it.'
    }

    $engineTarget = Join-Path $outputPath 'engine'
    New-Item -ItemType Directory -Path $engineTarget -Force | Out-Null
    if (![string]::Equals($engineSource, $engineTarget, [System.StringComparison]::OrdinalIgnoreCase)) {
        Get-ChildItem -LiteralPath $engineSource -File | Where-Object {
            $_.Name -in @('ffmpeg.exe', 'ffprobe.exe') -or $_.Extension -eq '.dll' -or
            $_.Name -match '^(LICENSE|COPYING|NOTICE)' -or $_.Name -match 'buildconf'
        } | Copy-Item -Destination $engineTarget -Force
    }

    $ffmpegVersion = @(& (Join-Path $engineTarget 'ffmpeg.exe') -version 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'The supplied ffmpeg executable did not start.' }
    $ffprobeVersion = @(& (Join-Path $engineTarget 'ffprobe.exe') -version 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'The supplied ffprobe executable did not start.' }
    $fingerprints = @(Get-ChildItem -LiteralPath $engineTarget -File | Where-Object {
        $_.Extension -in @('.exe', '.dll')
    } | ForEach-Object {
        [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{
        ffmpeg = [string]$ffmpegVersion[0]
        ffprobe = [string]$ffprobeVersion[0]
        files = $fingerprints
        note = 'Local dependency inventory only. Redistribution materials must be checked separately.'
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputPath 'ENGINE-INVENTORY.json') -Encoding UTF8

    Write-Output 'Application and local FFmpeg engine are ready for media tests.'
} else {
    Write-Output 'Application compiled. Supply an engine directory to enable media operations and full tests.'
}
Write-Output "Build output: $outputPath"
