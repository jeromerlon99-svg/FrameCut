$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
$buildOutput = if ($args.Count -gt 0) { [System.IO.Path]::GetFullPath($args[0]) } else { Join-Path (Split-Path $sourceDir) 'build' }
New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + (Join-Path $buildOutput 'FrameCut.exe')),('/resource:' + (Join-Path $sourceDir 'Main.xaml') + ',Main.xaml'),('/win32manifest:' + (Join-Path $sourceDir 'app.manifest')),'/r:System.dll','/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.Xaml.dll',('/r:' + (Join-Path $frameworkDir 'WPF\PresentationCore.dll')),('/r:' + (Join-Path $frameworkDir 'WPF\PresentationFramework.dll')),('/r:' + (Join-Path $frameworkDir 'WPF\WindowsBase.dll')))
if (Test-Path -LiteralPath (Join-Path $sourceDir 'FrameCut.ico')) { $compilerArgs += ('/win32icon:' + (Join-Path $sourceDir 'FrameCut.ico')) }
$compilerArgs += @((Join-Path $sourceDir 'Core.cs'),(Join-Path $sourceDir 'MultiExport.cs'),(Join-Path $sourceDir 'Timeline.cs'),(Join-Path $sourceDir 'SequenceTimeline.cs'),(Join-Path $sourceDir 'App.cs'),(Join-Path $sourceDir 'AssemblyInfo.cs'))
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $sourceDir 'FrameCut.exe.config') -Destination $buildOutput -Force
