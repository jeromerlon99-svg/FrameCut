param(
    [string]$WorkDirectory,
    [string]$GitBash,
    [string]$DownloadCache
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$WorkDirectory) { $WorkDirectory = Join-Path $repo 'build\media-engine' }
$work = [System.IO.Path]::GetFullPath($WorkDirectory)
if ($work -match '\s') { throw 'Use a work directory without spaces, for example C:\FrameCutBuild.' }
if (!$GitBash) {
    foreach ($candidate in @("$env:ProgramFiles\Git\usr\bin\bash.exe", "${env:ProgramFiles(x86)}\Git\usr\bin\bash.exe")) {
        if (Test-Path -LiteralPath $candidate) { $GitBash = $candidate; break }
    }
}
if (!(Test-Path -LiteralPath $GitBash)) { throw 'Pass -GitBash with the bash.exe path from Git for Windows.' }
$lock = Get-Content -LiteralPath (Join-Path $repo 'third-party\engine-sources.lock.json') -Raw | ConvertFrom-Json
$downloads = Join-Path $work 'downloads'
New-Item -ItemType Directory -Path $downloads,(Join-Path $work 'sources') -Force | Out-Null

function Assert-Archive([string]$path, [string]$expected) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        throw "SHA-256 mismatch: $path"
    }
}

foreach ($source in $lock.sources) {
    $archive = Join-Path $repo ('third-party\source-archives\' + $source.file)
    Assert-Archive $archive $source.sha256
    if (!(Test-Path -LiteralPath (Join-Path $work ('sources\' + $source.root)))) {
        & tar -xf $archive -C (Join-Path $work 'sources')
        if ($LASTEXITCODE -ne 0) { throw "Source extraction failed: $($source.name)" }
    }
}

foreach ($tool in $lock.buildTools) {
    $archive = Join-Path $downloads $tool.file
    if (!(Test-Path -LiteralPath $archive)) {
        $cached = if ($DownloadCache) { Join-Path $DownloadCache $tool.file } else { $null }
        if ($cached -and (Test-Path -LiteralPath $cached)) {
            Copy-Item -LiteralPath $cached -Destination $archive
        } else {
            Write-Output "Downloading fixed build tool: $($tool.name)"
            Invoke-WebRequest -Uri $tool.url -OutFile $archive -UseBasicParsing
        }
    }
    Assert-Archive $archive $tool.sha256
    if (!(Test-Path -LiteralPath (Join-Path $work $tool.root))) {
        if ($tool.file.EndsWith('.zip')) {
            Expand-Archive -LiteralPath $archive -DestinationPath $work
        } else {
            New-Item -ItemType Directory -Path (Join-Path $work $tool.root) -Force | Out-Null
            & tar -xf $archive -C (Join-Path $work $tool.root)
            if ($LASTEXITCODE -ne 0) { throw "Tool extraction failed: $($tool.name)" }
        }
    }
}

$recipe = (Join-Path $repo 'scripts\build-engine.sh').Replace('\','/')
& $GitBash --noprofile --norc $recipe ($work.Replace('\','/'))
if ($LASTEXITCODE -ne 0) {
    throw "Engine build failed. Logs: $(Join-Path $work 'logs')"
}
Copy-Item -LiteralPath (Join-Path $repo 'third-party\licenses\FFmpeg-GPLv3.txt') -Destination (Join-Path $work 'engine\LICENSE-FFmpeg.txt') -Force
Copy-Item -LiteralPath (Join-Path $work 'logs\buildconf.txt') -Destination (Join-Path $work 'engine\buildconf.txt') -Force
Copy-Item -LiteralPath (Join-Path $repo 'third-party\licenses') -Destination (Join-Path $work 'engine') -Recurse -Force
$runtimeFiles = @(Get-ChildItem -LiteralPath (Join-Path $work 'engine') -File | Where-Object { $_.Name -in @('ffmpeg.exe','ffprobe.exe') } | ForEach-Object {
    [ordered]@{name=$_.Name;size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[ordered]@{
    format=1
    sourceLockSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'third-party\engine-sources.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    recipeSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'scripts\build-engine.sh') -Algorithm SHA256).Hash.ToLowerInvariant()
    patchSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'third-party\ffmpeg-windows-make.patch') -Algorithm SHA256).Hash.ToLowerInvariant()
    buildFixScriptSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'scripts\apply-build-fixes.sh') -Algorithm SHA256).Hash.ToLowerInvariant()
    files=$runtimeFiles
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'engine\BUILD-PROVENANCE.json') -Encoding UTF8
Write-Output "Complete local engine: $(Join-Path $work 'engine')"
