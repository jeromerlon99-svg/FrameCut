using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FrameCut;
class MultiTests {
 static Engine engine;static string dir;static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS "+name);}
 static async Task Raw(string input,string file){await engine.Text("ffmpeg","-v error -nostdin -y -i "+Engine.Q(input)+" -map 0:a:0 -ac 1 -ar 48000 -f s16le "+Engine.Q(file),CancellationToken.None);}
 static double Rms(byte[] raw,int start,int length){double total=0;for(int i=start;i<start+length;i++){short s=(short)(raw[2*i]|raw[2*i+1]<<8);total+=(double)s*s;}return Math.Sqrt(total/length);}
 static double Frequency(byte[] raw,int start,int length){int crosses=0;for(int i=start+1;i<start+length;i++){short a=(short)(raw[2*i-2]|raw[2*i-1]<<8),b=(short)(raw[2*i]|raw[2*i+1]<<8);if(a<0&&b>=0)crosses++;}return crosses*48000.0/length;}
 static async Task Picture(MediaInfo media,int frame,int color,string label){byte[] image=await engine.Frame(media,frame,0,CancellationToken.None);using(var ms=new MemoryStream(image))using(var bitmap=new Bitmap(ms)){Color pixel=bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2);Check(color==0?pixel.R>200&&pixel.G<40&&pixel.B<40:color==1?pixel.G>90&&pixel.R<40&&pixel.B<40:pixel.B>200&&pixel.R<40&&pixel.G<40,label);}}
 static void Main(string[] args){try{dir=Path.GetFullPath(args[0]);Directory.CreateDirectory(dir);engine=new Engine(Path.GetFullPath(args[1]));Run().GetAwaiter().GetResult();Console.WriteLine("ALL "+count+" MULTI-VIDEO CHECKS PASSED");}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}}
 static async Task Run(){var ct=CancellationToken.None;string a=Path.Combine(dir,"红色 A.mp4"),b=Path.Combine(dir,"portrait-B.mp4"),c=Path.Combine(dir,"green-C.mp4");
  
  
  
  MediaInfo[] parts={await engine.Inspect(a,ct),await engine.Inspect(b,ct),await engine.Inspect(c,ct)};MediaInfo media=MediaInfo.Combine(parts);Project p=new Project{Source=a,SourceSize=new FileInfo(a).Length,SourceWrite=new FileInfo(a).LastWriteTimeUtc.Ticks,KeepOriginalAudio=true,Sources=parts.Select(SourceAsset.From).ToList()};
  for(int i=0;i<3;i++)p.Clips.Add(new Clip{Id=i+1,In=media.FirstFrame(i),Out=media.FirstFrame(i)+parts[i].Frames.Count,AudioIn=media.FirstTime(i),AudioOut=media.FirstTime(i)+parts[i].Duration});
  Check(media.Frames.Count==79&&media.PartCount==3&&media.HasAudio,"Three imported files form a 79-frame project");Check(parts[1].AudioIndex<0&&parts[1].Width==90,"Portrait silent material detected");Editing.Validate(p,media);
  Check((await engine.Frame(media,media.FirstFrame(1)+5,0,ct)).SequenceEqual(await engine.Frame(parts[1],5,0,ct)),"Virtual frame maps exactly to second source video");
  string original=Path.Combine(dir,"original-order.mp4");await engine.Export(media,p,original,true,true,ct,null);MediaInfo result=await engine.Inspect(original,ct);Check(result.Frames.Count==79,"Mixed 24/30/25 fps export preserves all 79 frames");Check(result.Width==160&&result.Height==90,"Mixed sizes normalize to first source canvas");Check(Math.Abs(result.Duration-3)<0.002,"Three video join has correct duration");await Picture(result,0,0,"Original-order segment A is red");await Picture(result,24,2,"Original-order segment B is blue");await Picture(result,54,1,"Original-order segment C is green");
  using(var ms=new MemoryStream(await engine.Frame(result,30,0,ct)))using(var bitmap=new Bitmap(ms)){Color edge=bitmap.GetPixel(3,45);Check(edge.R<10&&edge.G<10&&edge.B<10,"Portrait video uses black bars without stretching");}
  string wave=Path.Combine(dir,"continuous.wav"),raw=Path.Combine(dir,"continuous.raw");await engine.Export(media,p,wave,false,true,ct,null);await Raw(wave,raw);byte[] sound=File.ReadAllBytes(raw);Check(Math.Abs(Frequency(sound,9600,28800)-220)<4,"Original audio A starts at 220 Hz");Check(Rms(sound,57600,28800)<1,"Silent source B contributes a full silent section");Check(Math.Abs(Frequency(sound,105600,28800)-880)<4,"Original audio C follows silent source at 880 Hz");
  Editing.Move(p,2,0);string reorder=Path.Combine(dir,"reordered.mp4"),reorderedWav=Path.Combine(dir,"reordered.wav");await engine.Export(media,p,reorder,true,true,ct,null);var reordered=await engine.Inspect(reorder,ct);await Picture(reordered,0,1,"Picture reorder starts with green C");await Picture(reordered,25,0,"Picture reorder then red A");await Picture(reordered,49,2,"Picture reorder ends with blue B");await engine.Export(media,p,reorderedWav,false,true,ct,null);Check(File.ReadAllBytes(wave).SequenceEqual(File.ReadAllBytes(reorderedWav)),"Picture reorder leaves import-order original audio byte-identical");
  p.KeepOriginalAudio=false;string linked=Path.Combine(dir,"linked.wav"),linkedRaw=Path.Combine(dir,"linked.raw");await engine.Export(media,p,linked,false,true,ct,null);await Raw(linked,linkedRaw);byte[] linkedSound=File.ReadAllBytes(linkedRaw);Check(Math.Abs(Frequency(linkedSound,9600,28800)-880)<4&&Math.Abs(Frequency(linkedSound,57600,28800)-220)<4&&Rms(linkedSound,105600,28800)<1,"Optional linked audio follows C A B with silence for B");
  p.KeepOriginalAudio=true;string saved=Path.Combine(dir,"multi.framecut");Editing.Save(p,saved);Project loaded=Editing.Read(saved);Editing.Validate(loaded,media);Check(loaded.Version==2&&loaded.Sources.Count==3&&loaded.Sources[1].Path==b&&loaded.Clips[0].In==54,"Multi-source project saves and resolves relative source paths");Project snapshot=p.Copy();snapshot.Sources.RemoveAt(2);Check(p.Sources.Count==3,"Undo snapshots copy source lists independently");
  bool prevented=false;try{await engine.Export(media,p,b,true,true,ct,null);}catch{prevented=true;}Check(prevented&&(await engine.Inspect(b,ct)).Frames.Count==30,"Overwrite guard protects appended source videos");
  bool trimPrevented=false;try{Editing.Trim(p.Clips[0],0,60,media,false);}catch{trimPrevented=true;}Check(trimPrevented,"Clip trim cannot cross a source-file boundary");Check(Directory.GetFiles(dir,".framecut-*").Length==0,"Exports leave no temporary filter files");
 }
}
