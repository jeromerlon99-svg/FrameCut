using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FrameCut;
class Tests {
 static Engine engine;static string dir;static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS "+label);}
 static string Hash(byte[] b){using(var s=SHA256.Create())return Convert.ToBase64String(s.ComputeHash(b));}
 static Project One(MediaInfo m,int a,int b){return new Project{Source=m.Path,SourceSize=new FileInfo(m.Path).Length,SourceWrite=new FileInfo(m.Path).LastWriteTimeUtc.Ticks,Clips={new Clip{In=a,Out=b,AudioIn=m.At(a),AudioOut=m.At(b)}}};}
 static void Main(string[] args){try{dir=Path.GetFullPath(args[0]);engine=new Engine(Path.GetFullPath(args[1]));Run().GetAwaiter().GetResult();Console.WriteLine("ALL "+count+" CHECKS PASSED");}catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}}
 static async Task Run(){var ct=CancellationToken.None;string input=Path.Combine(dir,"测试 source.mp4");
  MediaInfo m=await engine.Inspect(input,ct);Check(m.Frames.Count==96&&m.AudioIndex>=0,"CFR + Unicode path: 96 indexed frames, audio detected");
  Check(Math.Abs(m.Duration-4)<0.001,"CFR duration accurate");
  foreach(int f in new[]{0,1,23,24,50,94,95}){string png=Path.Combine(dir,"reference.png");await engine.Text("ffmpeg","-v error -nostdin -y -i "+Engine.Q(input)+" -vf "+Engine.Q("select='eq(n,"+f+")'")+" -frames:v 1 -fps_mode passthrough -c:v png -threads 1 "+Engine.Q(png),ct);byte[] got=await engine.Frame(m,f,0,ct);Check(Hash(got)==Hash(File.ReadAllBytes(png)),"Exact CFR frame pixels #"+(f+1));}
  double[] wave=await engine.Waveform(m,ct);Check(wave.Length==1200&&wave.Max()>0.05,"Real audio waveform extraction");
  string trimmed=Path.Combine(dir,"trim.mp4");Project p=One(m,25,61);await engine.Export(m,p,trimmed,true,true,ct,null);MediaInfo result=await engine.Inspect(trimmed,ct);Check(result.Frames.Count==36&&result.AudioIndex>=0,"Precise non-keyframe trim: 36 video frames + audio");Check(Math.Abs(result.Duration-1.5)<0.002,"Trimmed duration 1.500 seconds");
  string single=Path.Combine(dir,"single.mp4");await engine.Export(m,One(m,95,96),single,true,false,ct,null);var sm=await engine.Inspect(single,ct);Check(sm.Frames.Count==1&&sm.AudioIndex<0,"Last-frame-only export, no audio stream");
  p=One(m,0,12);p.Clips.Add(new Clip{In=48,Out=72,AudioIn=m.At(48),AudioOut=m.At(72),Muted=true});string join=Path.Combine(dir,"join.mp4");await engine.Export(m,p,join,true,true,ct,null);var jm=await engine.Inspect(join,ct);Check(jm.Frames.Count==36&&Math.Abs(jm.Duration-1.5)<0.002,"Two-clip concatenate preserves frame count and duration");
  string vfr=Path.Combine(dir,"vfr.mkv");MediaInfo v=await engine.Inspect(vfr,ct);Check(v.Frames.Count==75&&v.AudioIndex<0,"VFR video / no audio detected");Check(Math.Abs((v.At(1)-v.At(0))-(v.At(25)-v.At(24)))>0.01,"VFR uses per-frame timestamps, not average FPS");
  foreach(int f in new[]{0,14,15,16,40,74}){string png=Path.Combine(dir,"reference.png");await engine.Text("ffmpeg","-v error -nostdin -y -i "+Engine.Q(vfr)+" -vf "+Engine.Q("select='eq(n,"+f+")'")+" -frames:v 1 -fps_mode passthrough -c:v png -threads 1 "+Engine.Q(png),ct);Check(Hash(await engine.Frame(v,f,0,ct))==Hash(File.ReadAllBytes(png)),"Exact VFR frame pixels #"+(f+1));}
  string vfout=Path.Combine(dir,"vfr-trim.mp4");await engine.Export(v,One(v,10,39),vfout,true,true,ct,null);var vm=await engine.Inspect(vfout,ct);Check(vm.Frames.Count==29&&vm.AudioIndex<0,"VFR precise trim keeps exactly 29 frames");
  p=One(m,24,72);p.Detached=true;p.Clips[0].AudioIn=0;p.Clips[0].AudioOut=0.5;string wav=Path.Combine(dir,"detached.wav");await engine.Export(m,p,wav,false,true,ct,null);string audioJson=await engine.Text("ffprobe","-v error -show_entries format=duration:stream=codec_type -of json "+Engine.Q(wav),ct);Check(audioJson.Contains("2.000000")&&!audioJson.Contains("video"),"Detached audio-only WAV pads to 2 seconds");
  string samples=Path.Combine(dir,"tail.raw");await engine.Text("ffmpeg","-v error -nostdin -y -ss 1 -i "+Engine.Q(wav)+" -ac 1 -ar 8000 -f s16le "+Engine.Q(samples),ct);Check(File.ReadAllBytes(samples).All(x=>x==0),"Short detached audio padded with actual digital silence");
  Clip tail=Editing.Split(p.Clips[0],60,m,true);p.Clips.Add(tail);Editing.Validate(p,m);Check(tail.AudioIn==tail.AudioOut,"Splitting after detached audio ends produces silent tail");string silence=Path.Combine(dir,"silent-tail.mp4");await engine.Export(m,p,silence,true,true,ct,null);Check((await engine.Inspect(silence,ct)).Frames.Count==48,"Detached split with silent tail exports correctly");
  string saved=Path.Combine(dir,"roundtrip.framecut");Editing.Save(p,saved);Project loaded=Editing.Read(saved);Editing.Validate(loaded,m);Check(loaded.Source==input&&loaded.Clips.Count==2&&loaded.Detached,"Project roundtrip: relative path + audio edits");
  bool invalid=false;try{Editing.Trim(p.Clips[0],5,5,m,false);}catch{invalid=true;}Check(invalid,"Empty frame ranges rejected");
  bool protectedSource=false;try{await engine.Export(m,One(m,0,10),input,true,true,ct,null);}catch{protectedSource=true;}Check(protectedSource&&(await engine.Inspect(input,ct)).Frames.Count==96,"Source overwrite prevented");
  string cancel=Path.Combine(dir,"cancel.mp4");using(var cts=new CancellationTokenSource()){cts.Cancel();try{await engine.Export(m,One(m,0,96),cancel,true,true,cts.Token,null);}catch(OperationCanceledException){}}Check(!File.Exists(cancel)&&Directory.GetFiles(dir,".framecut-*").Length==0,"Cancel leaves no partial output");
  string offset=Path.Combine(dir,"offset.mkv");var om=await engine.Inspect(offset,ct);string offsetWav=Path.Combine(dir,"offset.wav");await engine.Export(om,One(om,0,10),offsetWav,false,true,ct,null);string raw=Path.Combine(dir,"offset.raw");await engine.Text("ffmpeg","-v error -nostdin -y -i "+Engine.Q(offsetWav)+" -ac 1 -ar 8000 -f s16le "+Engine.Q(raw),ct);byte[] sound=File.ReadAllBytes(raw);Check(sound.Take(4000).All(x=>x==0)&&sound.Skip(8000).Any(x=>x!=0),"Source audio delay preserved with leading silence");
 }
}
