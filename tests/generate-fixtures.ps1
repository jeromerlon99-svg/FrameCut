param([Parameter(Mandatory=$true)][string]$FFmpegPath,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Run-FFmpeg([string[]]$Arguments){ & $FFmpegPath @Arguments; if($LASTEXITCODE -ne 0){throw 'Fixture generation failed'} }
# These are the complete procedural definitions of the bundled synthetic fixtures.
# Use an FFmpeg build with the lavfi input device when regenerating; runtime media tests
# consume the included hash-locked copies and do not need capture/generator devices.
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','testsrc2=size=320x180:rate=24:duration=4','-f','lavfi','-i','sine=frequency=660:sample_rate=48000:duration=4','-c:v','libx264','-preset','ultrafast','-g','24','-pix_fmt','yuv420p','-c:a','aac','-shortest',(Join-Path $OutputDirectory '测试 source.mp4'))
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=3','-vf',"select='if(lt(n,30),not(mod(n,2)),1)'",'-fps_mode','vfr','-c:v','libx264','-preset','ultrafast','-g','30','-an',(Join-Path $OutputDirectory 'vfr.mkv'))
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','testsrc2=size=160x90:rate=10:duration=2','-itsoffset','0.4','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=1.5','-map','0:v','-map','1:a','-c:v','libx264','-preset','ultrafast','-c:a','pcm_s16le',(Join-Path $OutputDirectory 'offset.mkv'))
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','color=c=red:s=160x90:r=24:d=1','-f','lavfi','-i','color=c=green:s=160x90:r=24:d=1','-f','lavfi','-i','color=c=blue:s=160x90:r=24:d=1','-f','lavfi','-i','aevalsrc=0.15*sin(2*PI*(220*t+110*t*t)):s=48000:d=3','-filter_complex','[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]','-map','[v]','-map','3:a','-c:v','libx264','-preset','ultrafast','-c:a','pcm_s16le',(Join-Path $OutputDirectory 'color-chirp.mkv'))
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','color=c=red:s=160x90:r=24:d=1','-f','lavfi','-i','sine=frequency=220:sample_rate=48000:duration=1','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-shortest',(Join-Path $OutputDirectory '红色 A.mp4'))
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','color=c=blue:s=90x160:r=30:d=1','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-an',(Join-Path $OutputDirectory 'portrait-B.mp4'))
Run-FFmpeg @('-v','error','-nostdin','-y','-f','lavfi','-i','color=c=green:s=320x180:r=25:d=1','-f','lavfi','-i','sine=frequency=880:sample_rate=48000:duration=1','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-shortest',(Join-Path $OutputDirectory 'green-C.mp4'))
Write-Output 'Regenerated fixtures. Encoding versions can change bytes; inspect and deliberately update SHA256.json if replacing the reference set.'
