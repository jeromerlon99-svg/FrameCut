param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$appDir=[System.IO.Path]::GetFullPath($AppDirectory)
$testWork=Join-Path $PSScriptRoot 'generated'
New-Item -ItemType Directory -Path $testWork -Force | Out-Null
# The shipped runtime has no capture-device inputs. Use synthetic, hash-locked fixtures.
$fixtureRoot=Join-Path $PSScriptRoot 'fixtures'
$fixtureManifest=Get-Content -LiteralPath (Join-Path $fixtureRoot 'SHA256.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($fixture in $fixtureManifest){
    $path=Join-Path $fixtureRoot $fixture.name
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fixture.sha256){throw 'Fixture hash mismatch'}
    Copy-Item -LiteralPath $path -Destination (Join-Path $testWork $fixture.name) -Force
}
$frameworkDir=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $frameworkDir 'csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 ('/out:'+(Join-Path $testWork 'Tests.exe')) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll (Join-Path (Split-Path $PSScriptRoot) 'src\Core.cs') (Join-Path (Split-Path $PSScriptRoot) 'src\MultiExport.cs') (Join-Path $PSScriptRoot 'Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Core test compilation failed'}
& (Join-Path $testWork 'Tests.exe') $testWork (Join-Path $appDir 'engine')
if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 ('/out:'+(Join-Path $testWork 'ReorderTests.exe')) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll (Join-Path (Split-Path $PSScriptRoot) 'src\Core.cs') (Join-Path (Split-Path $PSScriptRoot) 'src\MultiExport.cs') (Join-Path $PSScriptRoot 'ReorderTests.cs')
if($LASTEXITCODE -ne 0){throw 'Reorder test compilation failed'}
& (Join-Path $testWork 'ReorderTests.exe') $testWork (Join-Path $appDir 'engine')
if($LASTEXITCODE -ne 0){throw 'Reorder tests failed'}
$uiArgs=@('/nologo','/target:exe','/platform:x64','/codepage:65001',('/out:'+(Join-Path $testWork 'UiSmoke.exe')),'/r:System.dll','/r:System.Core.dll','/r:System.Xaml.dll',('/r:'+(Join-Path $appDir 'FrameCut.exe')),('/r:'+(Join-Path $frameworkDir 'WPF\PresentationCore.dll')),('/r:'+(Join-Path $frameworkDir 'WPF\PresentationFramework.dll')),('/r:'+(Join-Path $frameworkDir 'WPF\WindowsBase.dll')),(Join-Path $PSScriptRoot 'UiSmoke.cs'))
& $compiler @uiArgs
if($LASTEXITCODE -ne 0){throw 'UI test compilation failed'}
& (Join-Path $testWork 'UiSmoke.exe') $appDir (Join-Path $testWork 'ui-results.txt') (Join-Path $testWork 'preview.png')
if($LASTEXITCODE -ne 0){throw 'UI smoke tests failed'}
Get-Content -LiteralPath (Join-Path $testWork 'ui-results.txt')
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 ('/out:'+(Join-Path $testWork 'MultiTests.exe')) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll (Join-Path (Split-Path $PSScriptRoot) 'src\Core.cs') (Join-Path (Split-Path $PSScriptRoot) 'src\MultiExport.cs') (Join-Path $PSScriptRoot 'MultiTests.cs')
if($LASTEXITCODE -ne 0){throw 'Multi-video test compilation failed'}
& (Join-Path $testWork 'MultiTests.exe') $testWork (Join-Path $appDir 'engine')
if($LASTEXITCODE -ne 0){throw 'Multi-video tests failed'}
$multiUiArgs=$uiArgs.Clone()
$multiUiArgs[4]='/out:'+(Join-Path $testWork 'MultiUiSmoke.exe')
$multiUiArgs[$multiUiArgs.Length-1]=Join-Path $PSScriptRoot 'MultiUiSmoke.cs'
& $compiler @multiUiArgs
if($LASTEXITCODE -ne 0){throw 'Multi-video UI compilation failed'}
& (Join-Path $testWork 'MultiUiSmoke.exe') $appDir $testWork (Join-Path $testWork 'multi-ui-results.txt') (Join-Path $testWork 'multi-preview.png')
if($LASTEXITCODE -ne 0){throw 'Multi-video UI tests failed'}
Get-Content -LiteralPath (Join-Path $testWork 'multi-ui-results.txt')

