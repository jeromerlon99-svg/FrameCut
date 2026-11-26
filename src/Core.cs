using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FrameCut {
 public sealed class FrameInfo {
  public double Pts; public double Duration; public bool Key;
 }
 public sealed class MediaInfo {
  public string Path; public int VideoIndex, AudioIndex = -1, Width, Height;
  public double Fps, ContainerStart, AudioStart, EndPts; public string Codec;public int Rotation;
  public List<MediaInfo> Parts;public List<int> PartFrames;public List<double> PartTimes;
  public int PartCount{get{return Parts==null?1:Parts.Count;}}
  public bool HasAudio{get{return AudioIndex>=0;}}
  public MediaInfo Part(int index){return Parts==null?this:Parts[index];}
  public int FirstFrame(int index){return PartFrames==null?0:PartFrames[index];}
  public double FirstTime(int index){return PartTimes==null?0:PartTimes[index];}
  public int PartIndex(int frame){if(Parts==null)return 0;int lo=0,hi=Parts.Count-1;while(lo<hi){int m=(lo+hi+1)/2;if(PartFrames[m]<=frame)lo=m;else hi=m-1;}return lo;}
  public int PartAtTime(double time){if(Parts==null)return 0;int lo=0,hi=Parts.Count-1;while(lo<hi){int m=(lo+hi+1)/2;if(PartTimes[m]<=time+0.0000001)lo=m;else hi=m-1;}return lo;}
  public bool IsSourcePath(string path){string full=System.IO.Path.GetFullPath(path);return Enumerable.Range(0,PartCount).Any(i=>System.IO.Path.GetFullPath(Part(i).Path).Equals(full,StringComparison.OrdinalIgnoreCase));}
  public int DisplayWidth{get{return Math.Abs(Rotation)%180==90?Height:Width;}}
  public int DisplayHeight{get{return Math.Abs(Rotation)%180==90?Width:Height;}}
  public static MediaInfo Combine(IEnumerable<MediaInfo> items){
   List<MediaInfo> parts=items.ToList();if(parts.Count==0)throw new Exception("工程中没有视频素材。");if(parts.Count==1)return parts[0];
   MediaInfo first=parts[0],result=new MediaInfo{Path=first.Path,Width=first.DisplayWidth,Height=first.DisplayHeight,Fps=first.Fps,Codec="MULTI",VideoIndex=first.VideoIndex,AudioIndex=parts.Any(m=>m.HasAudio)?0:-1,Parts=parts,PartFrames=new List<int>(),PartTimes=new List<double>()};double time=0;
   foreach(MediaInfo part in parts){result.PartFrames.Add(result.Frames.Count);result.PartTimes.Add(time);foreach(FrameInfo frame in part.Frames)result.Frames.Add(new FrameInfo{Pts=time+frame.Pts-part.Origin,Duration=frame.Duration,Key=frame.Key});time+=part.Duration;}
   result.EndPts=time;return result;
  }
  public List<FrameInfo> Frames = new List<FrameInfo>();
  public double[] Wave = new double[0];
  public double Origin { get { return Frames[0].Pts; } }
  public double Duration { get { return EndPts - Origin; } }
  public double At(int index) { return index >= Frames.Count ? Duration : Frames[Math.Max(0,index)].Pts - Origin; }
  public int FrameAt(double seconds) {
   int lo=0, hi=Frames.Count-1;
   while(lo<hi) { int m=(lo+hi+1)/2; if(At(m)<=seconds+0.0000001)lo=m;else hi=m-1; }
   return lo;
  }
  public int BoundaryAt(double seconds) {
   if(seconds>=Duration-0.0000001)return Frames.Count;
   int i=FrameAt(seconds); return i+1<Frames.Count && Math.Abs(At(i+1)-seconds)<Math.Abs(At(i)-seconds)?i+1:i;
  }
 }
 public sealed class Clip {
  public int Id, In, Out; public double AudioIn, AudioOut, Gain=1; public bool Muted;
  public Clip Copy() { return (Clip)MemberwiseClone(); }
 }
 public sealed class Project {
  public int Version=2; public string Source; public long SourceSize, SourceWrite;
  public List<SourceAsset> Sources=new List<SourceAsset>();
  public List<AudioSpan> OriginalAudio=new List<AudioSpan>();
  public bool Detached, KeepOriginalAudio, OriginalMuted; public double OriginalGain=1;
  public List<Clip> Clips=new List<Clip>();
  public Project Copy() { var p=(Project)MemberwiseClone();p.Clips=Clips.Select(c=>c.Copy()).ToList();p.Sources=(Sources??new List<SourceAsset>()).Select(a=>a.Copy()).ToList();p.OriginalAudio=(OriginalAudio??new List<AudioSpan>()).Select(a=>a.Copy()).ToList();return p; }
 }
 public sealed class AudioSpan {public double Start,Length;public bool Silence;public AudioSpan Copy(){return (AudioSpan)MemberwiseClone();}}
 public sealed class SourceAsset {
  public string Path;public long Size,Write;public int Frames;
  public SourceAsset Copy(){return (SourceAsset)MemberwiseClone();}
  public static SourceAsset From(MediaInfo media){FileInfo fi=new FileInfo(media.Path);return new SourceAsset{Path=media.Path,Size=fi.Length,Write=fi.LastWriteTimeUtc.Ticks,Frames=media.Frames.Count};}
 }
 public static class Editing {
  public static double SoundDuration(Project p,MediaInfo media){return p.OriginalAudio!=null&&p.OriginalAudio.Count>0?p.OriginalAudio.Sum(a=>a.Length):media.Duration;}
  public static bool SoundAt(Project p,MediaInfo media,double time,out double raw){
   raw=time;if(time<0)return false;if(p.OriginalAudio==null||p.OriginalAudio.Count==0)return time<media.Duration;
   double start=0;foreach(AudioSpan a in p.OriginalAudio){if(time<start+a.Length-0.0000001){raw=a.Start+time-start;return !a.Silence;}start+=a.Length;}return false;
  }
  public static List<AudioSpan> SoundForDuration(Project p,MediaInfo media,double length){
   var result=new List<AudioSpan>();var spans=p.OriginalAudio!=null&&p.OriginalAudio.Count>0?p.OriginalAudio:new List<AudioSpan>{new AudioSpan{Start=0,Length=media.Duration}};
   double remaining=length;foreach(AudioSpan a in spans){if(remaining<=0.0000001)break;AudioSpan copy=a.Copy();copy.Length=Math.Min(copy.Length,remaining);if(copy.Length>0)result.Add(copy);remaining-=copy.Length;}if(remaining>0.0000001)result.Add(new AudioSpan{Silence=true,Length=remaining});return result;
  }
  public static string ClipName(Clip clip) {
   int id=Math.Max(1,clip.Id);string label="";while(id>0){id--;label=(char)('A'+id%26)+label;id/=26;}return label;
  }
  public static int NextId(Project project){return project.Clips.Count==0?1:project.Clips.Max(c=>c.Id)+1;}
  public static void AssignIds(Project project){int next=Math.Max(1,NextId(project));HashSet<int> used=new HashSet<int>();foreach(Clip c in project.Clips)if(c.Id<=0||!used.Add(c.Id)){c.Id=next++;used.Add(c.Id);}}
  public static double OutputDuration(Project project,MediaInfo media){return project.Clips.Sum(c=>media.At(c.Out)-media.At(c.In));}
  public static double OffsetOf(Project project,MediaInfo media,int index){return project.Clips.Take(index).Sum(c=>media.At(c.Out)-media.At(c.In));}
  public static int TargetForGap(int count,int from,int gap){if(from<0||from>=count)return from;gap=Math.Max(0,Math.Min(count,gap));return gap>from?gap-1:gap;}
  public static void Move(Project project,int from,int to){if(from<0||from>=project.Clips.Count||to<0||to>=project.Clips.Count)throw new Exception("片段位置无效。");Clip clip=project.Clips[from];project.Clips.RemoveAt(from);project.Clips.Insert(to,clip);}
  public static void Locate(Project project,MediaInfo media,double seconds,out int index,out int frame){
   seconds=Math.Max(0,seconds);double start=0;index=project.Clips.Count-1;frame=project.Clips[index].Out-1;
   for(int i=0;i<project.Clips.Count;i++){Clip c=project.Clips[i];double duration=media.At(c.Out)-media.At(c.In);if(seconds<start+duration-0.0000001){index=i;frame=Math.Max(c.In,Math.Min(c.Out-1,media.FrameAt(media.At(c.In)+seconds-start)));return;}start+=duration;}
  }
  public static void Step(Project project,int selected,int current,int delta,out int index,out int frame){
   long ordered=0,total=0;for(int i=0;i<project.Clips.Count;i++){Clip c=project.Clips[i];if(i<selected)ordered+=c.Out-c.In;total+=c.Out-c.In;}Clip active=project.Clips[selected];ordered+=Math.Max(0,Math.Min(active.Out-active.In-1,current-active.In));ordered=Math.Max(0,Math.Min(total-1,ordered+delta));
   index=0;frame=project.Clips[0].In;for(int i=0;i<project.Clips.Count;i++){Clip c=project.Clips[i];int length=c.Out-c.In;if(ordered<length){index=i;frame=c.In+(int)ordered;return;}ordered-=length;}
  }
  public static void WriteBytes(string path,byte[] bytes) {
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null,true);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public static void Validate(Project project, MediaInfo media) {
   if((project.Version!=1&&project.Version!=2) || project.Clips==null || project.Clips.Count<1 || project.Clips.Count>200||double.IsNaN(project.OriginalGain)||double.IsInfinity(project.OriginalGain)||project.OriginalGain<0||project.OriginalGain>2)throw new Exception("工程格式不受支持，或片段数量无效。");
   foreach(var c in project.Clips) {
    if(c.In<0||c.Out>media.Frames.Count||c.Out<=c.In||double.IsNaN(c.AudioIn)||double.IsInfinity(c.AudioIn)||double.IsNaN(c.AudioOut)||double.IsInfinity(c.AudioOut)||c.AudioIn<0||c.AudioOut>media.Duration+0.001||c.AudioOut<c.AudioIn||double.IsNaN(c.Gain)||double.IsInfinity(c.Gain)||c.Gain<0||c.Gain>2) throw new Exception("工程中的剪辑范围无效。");
    if(media.PartIndex(c.In)!=media.PartIndex(c.Out-1))throw new Exception("单个片段不能跨越两个源视频，请分别保留片段后拼接。");
   }
   if(project.OriginalAudio!=null){if(project.OriginalAudio.Count>1000)throw new Exception("原声音频段数量过多。");foreach(AudioSpan a in project.OriginalAudio)if(a==null||double.IsNaN(a.Start)||double.IsInfinity(a.Start)||double.IsNaN(a.Length)||double.IsInfinity(a.Length)||a.Start<0||a.Length<=0||!a.Silence&&a.Start+a.Length>media.Duration+0.001)throw new Exception("原声拼接范围无效。");}
  }
  public static void Trim(Clip clip,int start,int end,MediaInfo media,bool detached) {
   if(start<0||end>media.Frames.Count||end<=start)throw new Exception("入点必须早于出点，且范围需要在原视频内。");
   if(media.PartIndex(start)!=media.PartIndex(clip.In)||media.PartIndex(end-1)!=media.PartIndex(clip.In))throw new Exception("裁剪范围需要在当前片段所属的视频内。");
   clip.In=start;clip.Out=end;
   if(!detached) {clip.AudioIn=media.At(start);clip.AudioOut=media.At(end);}
  }
  public static Clip Split(Clip clip,int frame,MediaInfo media,bool detached) {
   if(frame<=clip.In||frame>=clip.Out)throw new Exception("请把播放头移到当前片段内部再分割。");
   Clip next=clip.Copy();next.In=frame;
   if(detached) {
    double cut=Math.Min(clip.AudioOut,clip.AudioIn+media.At(frame)-media.At(clip.In));
    // Each side may extend into silence; do not duplicate the last audible sample.
    next.AudioIn=cut; clip.AudioOut=cut;
   } else {clip.AudioOut=media.At(frame);next.AudioIn=media.At(frame);}
   clip.Out=frame;return next;
  }
  public static string Save(Project project,string path) {
   Project saved=project.Copy();
   Uri root=new Uri(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))+System.IO.Path.DirectorySeparatorChar);
   Uri source=new Uri(System.IO.Path.GetFullPath(project.Source));
   if(root.Scheme==source.Scheme) saved.Source=Uri.UnescapeDataString(root.MakeRelativeUri(source).ToString()).Replace('/',System.IO.Path.DirectorySeparatorChar);
   foreach(SourceAsset asset in saved.Sources){Uri uri=new Uri(System.IO.Path.GetFullPath(asset.Path));if(uri.Scheme==root.Scheme)asset.Path=Uri.UnescapeDataString(root.MakeRelativeUri(uri).ToString()).Replace('/',System.IO.Path.DirectorySeparatorChar);}
   string json=new JavaScriptSerializer().Serialize(saved);
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {File.WriteAllText(temp,json,new UTF8Encoding(false)); if(File.Exists(path))File.Replace(temp,path,null,true);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
   return path;
  }
  public static Project Read(string path) {
   if(new FileInfo(path).Length>2*1024*1024)throw new Exception("工程文件过大。");
   Project p=new JavaScriptSerializer().Deserialize<Project>(File.ReadAllText(path,Encoding.UTF8));
   if(p==null||string.IsNullOrWhiteSpace(p.Source))throw new Exception("工程中没有源视频。");
   if(!System.IO.Path.IsPathRooted(p.Source))p.Source=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),p.Source));
   if(p.Sources==null)p.Sources=new List<SourceAsset>();foreach(SourceAsset asset in p.Sources){if(asset==null||string.IsNullOrWhiteSpace(asset.Path))throw new Exception("工程素材路径无效。");if(!System.IO.Path.IsPathRooted(asset.Path))asset.Path=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),asset.Path));}
   return p;
  }
 }
 public sealed partial class Engine {
  sealed class OwnedProcess : Process {
   protected override void Dispose(bool disposing) {if(disposing)try{if(!HasExited)Kill();}catch{}base.Dispose(disposing);}
  }
  readonly string directory;
  public Engine(string directory) {this.directory=directory;}
  public string Tool(string name) {string p=System.IO.Path.Combine(directory,name+".exe");if(!File.Exists(p))throw new Exception("缺少 engine/"+name+".exe。请完整解压绿色版后再运行。");return p;}
  public static string N(double v) {return v.ToString("0.#########",CultureInfo.InvariantCulture);}
  public static string Q(string s) {
   // CommandLineToArgvW quoting; never route media paths through a shell.
   StringBuilder b=new StringBuilder("\"");int slashes=0;
   foreach(char c in s) {if(c=='\\')slashes++;else {if(c=='\"')b.Append('\\',slashes*2+1);else b.Append('\\',slashes);b.Append(c);slashes=0;}}
   b.Append('\\',slashes*2);return b.Append('"').ToString();
  }
  Process Start(string tool,string args) {
   return new OwnedProcess {StartInfo=new ProcessStartInfo(Tool(tool),args) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8,StandardOutputEncoding=Encoding.UTF8,WorkingDirectory=directory}};
  }
  static void Kill(Process p) {try{if(!p.HasExited)p.Kill();}catch{}}
  public async Task<string> Text(string tool,string args,CancellationToken ct) {
   using(Process p=Start(tool,args)) {
    ct.ThrowIfCancellationRequested();p.Start();using(ct.Register(()=>Kill(p))) {
     Task<string> output=p.StandardOutput.ReadToEndAsync(), error=p.StandardError.ReadToEndAsync();
     await Task.WhenAll(output,error);await Task.Run(()=>p.WaitForExit());ct.ThrowIfCancellationRequested();
     if(p.ExitCode!=0)throw new Exception("视频处理失败：\n"+Tail(error.Result));return output.Result;
    }
   }
  }
  static string Tail(string s) {return s.Length>5000?s.Substring(s.Length-5000):s;}
  static Dictionary<string,object> Dict(object value) {return value as Dictionary<string,object>??new Dictionary<string,object>();}
  static string Str(Dictionary<string,object> d,string k,string fallback="") {return d.ContainsKey(k)?Convert.ToString(d[k],CultureInfo.InvariantCulture):fallback;}
  static double Num(Dictionary<string,object>d,string k,double fallback=0) {double v;return double.TryParse(Str(d,k),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;}
  public async Task<MediaInfo> Inspect(string path,CancellationToken ct,Action<double> progress=null) {
   string json=await Text("ffprobe","-v error -show_streams -show_format -of json "+Q(path),ct);
   var root=Dict(new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.DeserializeObject(json));
   object[] streams=root.ContainsKey("streams")?root["streams"] as object[]:null;
   if(streams==null)throw new Exception("无法识别这个媒体文件。");
   var video=streams.Select(Dict).FirstOrDefault(s=>Str(s,"codec_type")=="video"&&Num(Dict(s.ContainsKey("disposition")?s["disposition"]:null),"attached_pic")==0);
   if(video==null)throw new Exception("这个文件没有可剪辑的画面轨。");
   var audio=streams.Select(Dict).FirstOrDefault(s=>Str(s,"codec_type")=="audio");
   var format=Dict(root.ContainsKey("format")?root["format"]:null);
   MediaInfo info=new MediaInfo{Path=path,VideoIndex=(int)Num(video,"index"),AudioIndex=audio==null?-1:(int)Num(audio,"index"),Width=(int)Num(video,"width"),Height=(int)Num(video,"height"),Codec=Str(video,"codec_name"),ContainerStart=Num(format,"start_time")};
   info.AudioStart=audio==null?info.ContainerStart:Num(audio,"start_time",info.ContainerStart);
   info.Rotation=(int)Num(Dict(video.ContainsKey("tags")?video["tags"]:null),"rotate");object[] sides=video.ContainsKey("side_data_list")?video["side_data_list"] as object[]:null;if(sides!=null)foreach(object side in sides){var sd=Dict(side);if(sd.ContainsKey("rotation"))info.Rotation=(int)Num(sd,"rotation");}
   string[] rate=Str(video,"avg_frame_rate","25/1").Split('/');double a,b;
   info.Fps=rate.Length==2&&double.TryParse(rate[0],out a)&&double.TryParse(rate[1],out b)&&b>0?a/b:25;
   if(info.Fps<=0||info.Fps>1000)info.Fps=25;
   double duration=Num(format,"duration",Num(video,"duration",1));
   using(Process p=Start("ffprobe","-v error -threads 2 -select_streams "+info.VideoIndex+" -show_frames -show_entries frame=best_effort_timestamp_time,duration_time,pkt_duration_time,key_frame -of compact=p=0:nk=0 "+Q(path))) {
    ct.ThrowIfCancellationRequested();p.Start();using(ct.Register(()=>Kill(p))) {
     Task<string> errors=p.StandardError.ReadToEndAsync();string line;long last=0;
     while((line=await p.StandardOutput.ReadLineAsync())!=null) {
      double pts=double.NaN,d=0;bool key=false;
      foreach(string token in line.Split('|')) {
       int eq=token.IndexOf('=');if(eq<0)continue;string k=token.Substring(0,eq),v=token.Substring(eq+1);double n;
       if(k=="best_effort_timestamp_time"&&double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out n))pts=n;
       if((k=="duration_time"||k=="pkt_duration_time")&&double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out n))d=n;
       if(k=="key_frame")key=v=="1";
      }
      if(!double.IsNaN(pts)) {
       if(info.Frames.Count>0&&pts<=info.Frames[info.Frames.Count-1].Pts)throw new Exception("视频存在重复或倒退的帧时间戳，请先转换为规范视频后再剪辑。");
       info.Frames.Add(new FrameInfo{Pts=pts,Duration=d,Key=key});
       if(progress!=null&&Environment.TickCount-last>200) {last=Environment.TickCount;progress(Math.Min(0.98,(pts-info.ContainerStart)/Math.Max(duration,0.01)));}
      }
      ct.ThrowIfCancellationRequested();
     }
     string err=await errors;await Task.Run(()=>p.WaitForExit());ct.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception("读取帧索引失败："+Tail(err));
    }
   }
   if(info.Frames.Count==0)throw new Exception("视频中没有可解码的帧。");
   FrameInfo final=info.Frames[info.Frames.Count-1];
   double lastDuration=final.Duration>0?final.Duration:info.Frames.Count>1?final.Pts-info.Frames[info.Frames.Count-2].Pts:1/info.Fps;
   info.EndPts=final.Pts+lastDuration;
   if(progress!=null)progress(1);return info;
  }
  public async Task<byte[]> Frame(MediaInfo info,int index,int width,CancellationToken ct) {
   if(info.Parts!=null){int part=info.PartIndex(index);return await Frame(info.Part(part),index-info.FirstFrame(part),width,ct);}
   double pts=info.Frames[Math.Max(0,Math.Min(index,info.Frames.Count-1))].Pts;
   string scale=width>0?",scale='min("+width+",iw*sar)':-2,setsar=1":"";
   // ffprobe prints microseconds; allow its half-microsecond rounding at rational frame rates.
   string filter="select='gte(t,"+N(pts-0.000001)+")'"+scale;
   string args="-v error -nostdin -threads 2 -filter_threads 1 -copyts -ss "+N(Math.Max(0,pts-info.ContainerStart-2))+" -i "+Q(info.Path)+" -map 0:"+info.VideoIndex+" -vf "+Q(filter)+" -frames:v 1 -fps_mode passthrough -f image2pipe -c:v png -threads 1 pipe:1";
   using(Process p=Start("ffmpeg",args)) {
    ct.ThrowIfCancellationRequested();p.Start();using(ct.Register(()=>Kill(p))) {
     Task<string> errors=p.StandardError.ReadToEndAsync();using(MemoryStream ms=new MemoryStream()) {
      await p.StandardOutput.BaseStream.CopyToAsync(ms,81920,ct);string err=await errors;await Task.Run(()=>p.WaitForExit());ct.ThrowIfCancellationRequested();
      if(p.ExitCode!=0||ms.Length==0)throw new Exception("无法解码这一帧："+Tail(err));return ms.ToArray();
     }
    }
   }
  }
  public async Task<double[]> Waveform(MediaInfo info,CancellationToken ct) {
   if(info.Parts!=null){double[] combined=new double[1200];for(int part=0;part<info.PartCount;part++){ct.ThrowIfCancellationRequested();MediaInfo source=info.Part(part);double[] wave=source.Wave;if(wave.Length==0){wave=await Waveform(source,ct);source.Wave=wave;}for(int i=0;i<wave.Length;i++){int bin=Math.Min(combined.Length-1,(int)((info.FirstTime(part)+(i+0.5)/wave.Length*source.Duration)/info.Duration*combined.Length));combined[bin]=Math.Max(combined[bin],wave[i]);}}return combined;}
   if(info.AudioIndex<0)return new double[0];
   using(Process p=Start("ffmpeg","-v error -nostdin -threads 2 -i "+Q(info.Path)+" -map 0:"+info.AudioIndex+" -vn -ac 1 -ar 48000 -f s16le pipe:1")) {
    p.Start();using(ct.Register(()=>Kill(p))) {
     Task<string> error=p.StandardError.ReadToEndAsync();double[] peaks=new double[1200];byte[] buffer=new byte[8192];long sample=0;int n,carry=-1;
     // Stream into a fixed-size envelope: memory usage is independent of video length.
     while((n=await p.StandardOutput.BaseStream.ReadAsync(buffer,0,buffer.Length,ct))>0) {
      int i=0;if(carry>=0){short s=(short)(carry|(buffer[0]<<8));AddPeak(peaks,sample++,s,info);i=1;carry=-1;}
      for(;i+1<n;i+=2)AddPeak(peaks,sample++,(short)(buffer[i]|buffer[i+1]<<8),info);
      if(i<n)carry=buffer[i];
     }
     await error;await Task.Run(()=>p.WaitForExit());ct.ThrowIfCancellationRequested();return peaks;
    }
   }
  }
  static void AddPeak(double[] peaks,long sample,short value,MediaInfo info) {
   double t=sample/48000.0+info.AudioStart-info.Origin;
   int slot=(int)(t/Math.Max(0.001,info.Duration)*peaks.Length);if(slot>=0&&slot<peaks.Length)peaks[slot]=Math.Max(peaks[slot],Math.Abs((int)value)/32768.0);
  }
  public string ExportArgs(MediaInfo media,Project project,string target,bool video,bool audio) {
   if(media.Parts!=null||project.OriginalAudio!=null&&project.OriginalAudio.Count>0){ExportPlan plan=MultiPlan(media,project,target,video,audio);return plan.Prefix+" -filter_complex "+Q(plan.Graph)+plan.Suffix;}
   Editing.Validate(project,media);audio=audio&&media.AudioIndex>=0;
   if(!video&&!audio)throw new Exception("源视频没有音轨，无法导出音频。");
   StringBuilder f=new StringBuilder();bool original=audio&&project.KeepOriginalAudio;bool clipAudio=audio&&!original;
   for(int i=0;i<project.Clips.Count;i++) {
    Clip c=project.Clips[i];double start=media.Frames[c.In].Pts,end=c.Out==media.Frames.Count?media.EndPts:media.Frames[c.Out].Pts,duration=end-start;
    if(video)f.Append("[0:"+media.VideoIndex+"]trim=start="+N(start)+":end="+N(end)+",setpts=PTS-STARTPTS,scale='trunc(iw*sar/2)*2:trunc(ih/2)*2',setsar=1[v"+i+"];");
    if(clipAudio) {
     double ain=media.Origin+c.AudioIn,aout=media.Origin+c.AudioOut;
     if(c.Muted || aout<=ain+0.0000001)f.Append("anullsrc=r=48000:cl=stereo,atrim=duration="+N(duration)+"[a"+i+"];");
     else f.Append("[0:"+media.AudioIndex+"]atrim=start="+N(ain)+":end="+N(aout)+",asetpts=PTS-"+N(ain)+"/TB,aresample=48000:async=1:first_pts=0,aformat=channel_layouts=stereo,apad=whole_dur="+N(duration)+",atrim=duration="+N(duration)+",volume="+N(c.Gain)+"[a"+i+"];");
    }
   }
   if(video||clipAudio){
    for(int i=0;i<project.Clips.Count;i++){if(video)f.Append("[v"+i+"]");if(clipAudio)f.Append("[a"+i+"]");}
    f.Append("concat=n="+project.Clips.Count+":v="+(video?"1":"0")+":a="+(clipAudio?"1":"0"));if(video)f.Append("[v]");if(clipAudio)f.Append("[a]");
   }
   if(original){
    if(video)f.Append(';');double duration=Editing.OutputDuration(project,media);
    if(project.OriginalMuted)f.Append("anullsrc=r=48000:cl=stereo,atrim=duration="+N(duration)+"[a]");
    else f.Append("[0:"+media.AudioIndex+"]atrim=start="+N(media.Origin)+":end="+N(media.Origin+duration)+",asetpts=PTS-"+N(media.Origin)+"/TB,aresample=48000:async=1:first_pts=0,aformat=channel_layouts=stereo,apad=whole_dur="+N(duration)+",atrim=duration="+N(duration)+",volume="+N(project.OriginalGain)+"[a]");
   }
   string args="-hide_banner -v error -nostdin -y -threads 2 -copyts -i "+Q(media.Path)+" -filter_complex_threads 2 -filter_complex "+Q(f.ToString());
   if(video)args+=" -map "+Q("[v]")+" -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -fps_mode vfr -threads 2";
   if(audio)args+=" -map "+Q("[a]")+(System.IO.Path.GetExtension(target).Equals(".wav",StringComparison.OrdinalIgnoreCase)?" -c:a pcm_s16le":" -c:a aac -b:a 192k");
   if(!System.IO.Path.GetExtension(target).Equals(".wav",StringComparison.OrdinalIgnoreCase))args+=" -movflags +faststart";
   return args+" -map_metadata -1 -progress pipe:1 -nostats "+Q(target);
  }
  public async Task Export(MediaInfo media,Project project,string target,bool video,bool audio,CancellationToken ct,Action<double> progress) {
   if(media.IsSourcePath(target))throw new Exception("请另存为新文件，不能覆盖任一源视频。");
   string temporary=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target),".framecut-"+Guid.NewGuid().ToString("N")+System.IO.Path.GetExtension(target));
   double duration=Editing.OutputDuration(project,media);string graphPath=temporary+".filter.txt";
   try {
    string args;if(media.Parts!=null||project.OriginalAudio!=null&&project.OriginalAudio.Count>0){ExportPlan plan=MultiPlan(media,project,temporary,video,audio);File.WriteAllText(graphPath,plan.Graph,new UTF8Encoding(false));args=plan.Prefix+" -/filter_complex "+Q(graphPath)+plan.Suffix;}else args=ExportArgs(media,project,temporary,video,audio);
    using(Process p=Start("ffmpeg",args)) {
     ct.ThrowIfCancellationRequested();p.Start();using(ct.Register(()=>Kill(p))) {
      Task<string> error=p.StandardError.ReadToEndAsync();string line;
      while((line=await p.StandardOutput.ReadLineAsync())!=null) {
       double value;if(line.StartsWith("out_time_us=")&&double.TryParse(line.Substring(12),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&progress!=null)progress(Math.Min(0.99,value/1000000/Math.Max(0.001,duration)));
      }
      string err=await error;await Task.Run(()=>p.WaitForExit());ct.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception("导出失败：\n"+Tail(err));
     }
    }
    if(!File.Exists(temporary)||new FileInfo(temporary).Length==0)throw new Exception("没有生成有效的输出文件。");
    ct.ThrowIfCancellationRequested();if(File.Exists(target))File.Replace(temporary,target,null,true);else File.Move(temporary,target);if(progress!=null)progress(1);
   }finally {if(File.Exists(temporary))File.Delete(temporary);if(File.Exists(graphPath))File.Delete(graphPath);}
  }
  public async Task Proxy(MediaInfo media,string target,CancellationToken ct,Action<double> progress) {
   // Keep the original container timeline so playback time matches the source frame index.
   string args="-v error -nostdin -y -threads 2 -i "+Q(media.Path)+" -map 0:"+media.VideoIndex+" -map 0:a:0? -vf "+Q("scale='min(960,iw*sar)':-2,setsar=1")+" -c:v libx264 -preset ultrafast -crf 25 -pix_fmt yuv420p -threads 2 -c:a aac -b:a 128k -movflags +faststart -progress pipe:1 "+Q(target);
   using(Process p=Start("ffmpeg",args)) {p.Start();using(ct.Register(()=>Kill(p))){Task<string> error=p.StandardError.ReadToEndAsync();string line;while((line=await p.StandardOutput.ReadLineAsync())!=null){double n;if(line.StartsWith("out_time_us=")&&double.TryParse(line.Substring(12),out n)&&progress!=null)progress(Math.Min(0.99,n/1000000/media.Duration));}string err=await error;await Task.Run(()=>p.WaitForExit());ct.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new Exception("生成兼容预览失败："+Tail(err));}}
  }
 }
}

