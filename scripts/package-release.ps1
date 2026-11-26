param(
    [Parameter(Mandatory=$true)][string]$EngineDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'dist' }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$version = '1.2.1'
$sourceZip = Join-Path $output "FrameCut-Source-v$version.zip"
$portable = Join-Path $output "FrameCut-Portable-v$version"
if ((Test-Path -LiteralPath $portable) -and @(Get-ChildItem -LiteralPath $portable -Force).Count -gt 0) {
    throw 'The versioned portable folder is not empty. Choose a fresh OutputDirectory to avoid including stale files.'
}
New-Item -ItemType Directory -Path $output,$portable -Force | Out-Null

foreach ($source in (Get-Content -LiteralPath (Join-Path $repo 'third-party\engine-sources.lock.json') -Raw | ConvertFrom-Json).sources) {
    $archive = Join-Path $repo ('third-party\source-archives\' + $source.file)
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) { throw 'Source archive mismatch.' }
}
$provenancePath = Join-Path $EngineDirectory 'BUILD-PROVENANCE.json'
if (!(Test-Path -LiteralPath $provenancePath)) { throw 'Use an engine produced by scripts/build-media-engine.ps1 with its provenance record.' }
$runtimeLock = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
if (@(Get-ChildItem -LiteralPath $EngineDirectory -File | Where-Object { $_.Extension -in @('.exe','.dll') -and $_.Name -notin @('ffmpeg.exe','ffprobe.exe') }).Count -gt 0) {
    throw 'Unexpected executable or DLL in the engine directory. Use the dedicated static source build.'
}
if ($runtimeLock.sourceLockSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repo 'third-party\engine-sources.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant() -or
    $runtimeLock.recipeSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repo 'scripts\build-engine.sh') -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Engine provenance differs from the supplied source inputs or recipe.'
}
if ($runtimeLock.patchSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repo 'third-party\ffmpeg-windows-make.patch') -Algorithm SHA256).Hash.ToLowerInvariant() -or
    $runtimeLock.buildFixScriptSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repo 'scripts\apply-build-fixes.sh') -Algorithm SHA256).Hash.ToLowerInvariant()) {
    throw 'Engine build-fix provenance does not match the packaged patch.'
}
foreach ($binary in $runtimeLock.files) {
    $path = Join-Path $EngineDirectory $binary.name
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $binary.sha256) {
        throw 'Engine differs from the source-correspondence record. Rebuild, test and update the record before publishing.'
    }
}

& (Join-Path $repo 'scripts\build.ps1') -OutputDirectory $portable -EngineDirectory $EngineDirectory
Copy-Item -LiteralPath $provenancePath -Destination (Join-Path $portable 'engine') -Force
Copy-Item -LiteralPath (Join-Path $repo 'third-party\licenses') -Destination $portable -Recurse -Force

# Explicit source allowlist keeps build outputs, private videos and local Git data out.
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
$zip = [System.IO.Compression.ZipFile]::Open($sourceZip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in @('src','tests','assets','scripts','docs','third-party','.github','.gitignore','.gitattributes','FrameCut.sln','README.md','THIRD-PARTY.md','LICENSE','CHANGELOG.md')) {
        $basePath = Join-Path $repo $item
        if (!(Test-Path -LiteralPath $basePath)) { throw "Missing source item: $item" }
        $files = if ((Get-Item -LiteralPath $basePath).PSIsContainer) { Get-ChildItem -LiteralPath $basePath -Recurse -File -Force } else { Get-Item -LiteralPath $basePath }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($repo.Length + 1).Replace('\','/')
            if ($relative -match '(^|/)(generated|bin|obj|\.git|\.cache)/' -or $file.Extension -in @('.exe','.dll','.pdb','.framecut')) { continue }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, ('FrameCut-Source/' + $relative), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $zip.Dispose() }

$sourceDir = Join-Path $portable 'sources'
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
Copy-Item -LiteralPath $sourceZip -Destination $sourceDir -Force
@(
    "Complete matching source is included in sources/FrameCut-Source-v$version.zip.",
    'It contains the application, exact FFmpeg/x264/zlib source inputs, build recipe and license notices.',
    'No external server or written source offer is needed to obtain this shipped copy.'
) | Set-Content -LiteralPath (Join-Path $portable 'SOURCE-CODE.txt') -Encoding UTF8
$portableZip = Join-Path $output "FrameCut-Portable-v$version.zip"
Compress-Archive -LiteralPath $portable -DestinationPath $portableZip -CompressionLevel Optimal -Force
$checksums = @()
foreach ($archive in @($sourceZip,$portableZip)) {
    ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($archive)) |
        ForEach-Object { $checksums += $_ }
}
$checksums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Release prepared: $portableZip"
Write-Output "Matching source included: $sourceZip"
