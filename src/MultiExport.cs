using System;
using System.IO;
using System.Text;

namespace FrameCut {
 public sealed partial class Engine {
  sealed class ExportPlan{public string Prefix,Graph,Suffix;}
  ExportPlan MultiPlan(MediaInfo media,Project project,string target,bool video,bool audio){
   Editing.Validate(project,media);audio=audio&&media.HasAudio;if(!video&&!audio)throw new Exception("所有素材均无音轨，无法导出音频。");
   StringBuilder inputs=new StringBuilder("-hide_banner -v error -nostdin -y -copyts"),graph=new StringBuilder();
   for(int i=0;i<media.PartCount;i++)inputs.Append(" -threads 2 -i "+Q(media.Part(i).Path));inputs.Append(" -filter_complex_threads 2");
   int width=Math.Max(2,media.Part(0).DisplayWidth/2*2),height=Math.Max(2,media.Part(0).DisplayHeight/2*2);
   if(video){
    for(int i=0;i<project.Clips.Count;i++){
     Clip clip=project.Clips[i];int sourceIndex=media.PartIndex(clip.In);MediaInfo source=media.Part(sourceIndex);int first=media.FirstFrame(sourceIndex),start=clip.In-first,end=clip.Out-first;
     double beginPts=source.Frames[start].Pts,endPts=end==source.Frames.Count?source.EndPts:source.Frames[end].Pts;
     graph.Append("["+sourceIndex+":"+source.VideoIndex+"]trim=start="+N(beginPts)+":end="+N(endPts)+",setpts=PTS-STARTPTS,settb=AVTB,scale='trunc(iw*sar/2)*2:trunc(ih/2)*2',setsar=1,scale="+width+":"+height+":force_original_aspect_ratio=decrease:force_divisible_by=2,pad="+width+":"+height+":(ow-iw)/2:(oh-ih)/2,setsar=1[v"+i+"];");
    }
    for(int i=0;i<project.Clips.Count;i++)graph.Append("[v"+i+"]");graph.Append("concat=n="+project.Clips.Count+":v=1:a=0[v];");
   }
   if(audio){
    // This bus follows immutable material import order, independently of picture cards.
    for(int i=0;i<media.PartCount;i++){
     MediaInfo source=media.Part(i);if(!source.HasAudio)graph.Append("anullsrc=r=48000:cl=stereo,atrim=duration="+N(source.Duration)+"[base"+i+"];");
     else graph.Append("["+i+":"+source.AudioIndex+"]atrim=start="+N(source.Origin)+":end="+N(source.EndPts)+",asetpts=PTS-"+N(source.Origin)+"/TB,aresample=48000:async=1:first_pts=0,aformat=channel_layouts=stereo,apad=whole_dur="+N(source.Duration)+",atrim=duration="+N(source.Duration)+"[base"+i+"];");
    }
    for(int i=0;i<media.PartCount;i++)graph.Append("[base"+i+"]");graph.Append("concat=n="+media.PartCount+":v=0:a=1[bus];");
    double duration=Editing.OutputDuration(project,media);
    if(project.KeepOriginalAudio){
     string original="bus";
     if(project.OriginalAudio!=null&&project.OriginalAudio.Count>0){int count=project.OriginalAudio.Count;graph.Append("[bus]asplit="+count);for(int i=0;i<count;i++)graph.Append("[os"+i+"]");graph.Append(';');
      for(int i=0;i<count;i++){AudioSpan span=project.OriginalAudio[i];if(span.Silence)graph.Append("[os"+i+"]anullsink;anullsrc=r=48000:cl=stereo,atrim=duration="+N(span.Length)+"[og"+i+"];");else graph.Append("[os"+i+"]atrim=start="+N(span.Start)+":end="+N(span.Start+span.Length)+",asetpts=PTS-"+N(span.Start)+"/TB,aresample=48000:async=1:first_pts=0,apad=whole_dur="+N(span.Length)+",atrim=duration="+N(span.Length)+"[og"+i+"];");}
      for(int i=0;i<count;i++)graph.Append("[og"+i+"]");graph.Append("concat=n="+count+":v=0:a=1[original];");original="original";
     }
     graph.Append("["+original+"]volume="+N(project.OriginalMuted?0:project.OriginalGain)+",apad=whole_dur="+N(duration)+",atrim=duration="+N(duration)+"[a];");
    }
    else {
     graph.Append("[bus]asplit="+project.Clips.Count);for(int i=0;i<project.Clips.Count;i++)graph.Append("[ab"+i+"]");graph.Append(';');
     for(int i=0;i<project.Clips.Count;i++){
      Clip clip=project.Clips[i];double length=media.At(clip.Out)-media.At(clip.In);
      graph.Append("[ab"+i+"]atrim=start="+N(clip.AudioIn)+":end="+N(Math.Max(clip.AudioIn,clip.AudioOut))+",asetpts=PTS-"+N(clip.AudioIn)+"/TB,aresample=48000:async=1:first_pts=0,apad=whole_dur="+N(length)+",atrim=duration="+N(length)+",volume="+N(clip.Muted?0:clip.Gain)+"[a"+i+"];");
     }
     for(int i=0;i<project.Clips.Count;i++)graph.Append("[a"+i+"]");graph.Append("concat=n="+project.Clips.Count+":v=0:a=1[a];");
    }
   }
   if(graph.Length>0&&graph[graph.Length-1]==';')graph.Length--;
   string suffix="";if(video)suffix+=" -map "+Q("[v]")+" -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -fps_mode vfr -enc_time_base:v 1:1000000 -video_track_timescale 1000000 -threads 2";
   if(audio)suffix+=" -map "+Q("[a]")+(Path.GetExtension(target).Equals(".wav",StringComparison.OrdinalIgnoreCase)?" -c:a pcm_s16le":" -c:a aac -b:a 192k");
   if(!Path.GetExtension(target).Equals(".wav",StringComparison.OrdinalIgnoreCase))suffix+=" -movflags +faststart";
   suffix+=" -map_metadata -1 -progress pipe:1 -nostats "+Q(target);return new ExportPlan{Prefix=inputs.ToString(),Graph=graph.ToString(),Suffix=suffix};
  }
 }
}
