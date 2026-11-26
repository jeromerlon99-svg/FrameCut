using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FrameCut;
class ReorderTests {
 static Engine engine;static string dir;static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);checks++;Console.WriteLine("PASS "+label);}
 static Project Whole(MediaInfo m){var f=new FileInfo(m.Path);return new Project{Source=m.Path,SourceSize=f.Length,SourceWrite=f.LastWriteTimeUtc.Ticks,Clips={new Clip{Id=1,In=0,Out=m.Frames.Count,AudioIn=0,AudioOut=m.Duration}}};}
 static void Main(string[] args){try{dir=Path.GetFullPath(args[0]);engine=new Engine(Path.GetFullPath(args[1]));Run().GetAwaiter().GetResult();Console.WriteLine("ALL "+checks+" REORDER CHECKS PASSED");}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}}
 static async Task Run(){var ct=CancellationToken.None;string input=Path.Combine(dir,"color-chirp.mkv");
  
  MediaInfo media=await engine.Inspect(input,ct);Project whole=Whole(media),p=Whole(media);p.KeepOriginalAudio=true;Clip b=Editing.Split(p.Clips[0],24,media,false);b.Id=2;p.Clips.Add(b);Clip c=Editing.Split(b,48,media,false);c.Id=3;p.Clips.Add(c);Check(p.Clips.Count==3&&p.Clips.Sum(x=>x.Out-x.In)==72,"A B C cuts retain 72 frames");
  Editing.Move(p,2,Editing.TargetForGap(3,2,0));Check(p.Clips.Select(x=>x.Id).SequenceEqual(new[]{3,1,2}),"Insertion-gap reorder produces C A B");int index,frame;Editing.Locate(p,media,1.0,out index,out frame);Check(index==1&&frame==0,"Output time 1s points to source A");Editing.Step(p,1,0,-1,out index,out frame);Check(index==0&&frame==71,"Frame step follows reordered adjacency");
  string output=Path.Combine(dir,"reordered.mp4");await engine.Export(media,p,output,true,true,ct,null);MediaInfo edited=await engine.Inspect(output,ct);Check(edited.Frames.Count==72&&Math.Abs(edited.Duration-3)<0.002,"Reordered MP4 retains 3 seconds and 72 frames");
  int[] ids={2,0,1};for(int n=0;n<3;n++){byte[] image=await engine.Frame(edited,n*24,0,ct);using(var ms=new MemoryStream(image))using(var bitmap=new Bitmap(ms)){Color pixel=bitmap.GetPixel(80,45);int expected=ids[n];bool ok=expected==2?pixel.B>200&&pixel.R<30&&pixel.G<30:expected==0?pixel.R>200&&pixel.G<30&&pixel.B<30:pixel.G>90&&pixel.R<30&&pixel.B<30;Check(ok,"Exported picture segment "+(n+1)+" is "+new[]{"red A","green B","blue C"}[expected]);}}
  string originalAudio=Path.Combine(dir,"original-chirp.wav"),reorderedAudio=Path.Combine(dir,"reordered-chirp.wav");await engine.Export(media,whole,originalAudio,false,true,ct,null);await engine.Export(media,p,reorderedAudio,false,true,ct,null);Check(File.ReadAllBytes(originalAudio).SequenceEqual(File.ReadAllBytes(reorderedAudio)),"Original WAV is byte-identical after C A B reorder");
  string expectedAac=Path.Combine(dir,"original-aac.m4a"),rawExpected=Path.Combine(dir,"expected-aac.raw"),rawActual=Path.Combine(dir,"actual-aac.raw");await engine.Export(media,whole,expectedAac,false,true,ct,null);await engine.Text("ffmpeg","-v error -nostdin -y -i "+Engine.Q(expectedAac)+" -map 0:a -f s16le "+Engine.Q(rawExpected),ct);await engine.Text("ffmpeg","-v error -nostdin -y -i "+Engine.Q(output)+" -map 0:a -f s16le "+Engine.Q(rawActual),ct);Check(File.ReadAllBytes(rawExpected).SequenceEqual(File.ReadAllBytes(rawActual)),"MP4 audio decodes identically to original-order AAC baseline");
  string saved=Path.Combine(dir,"reordered.framecut");Editing.Save(p,saved);Project loaded=Editing.Read(saved);Check(loaded.KeepOriginalAudio&&loaded.Clips.Select(x=>x.Id).SequenceEqual(new[]{3,1,2}),"Project restores continuous sound and stable clip identities");
  p.Clips.RemoveAt(2);string shorter=Path.Combine(dir,"shorter.wav");await engine.Export(media,p,shorter,false,true,ct,null);string shortJson=await engine.Text("ffprobe","-v error -show_entries format=duration -of json "+Engine.Q(shorter),ct);double shorterDuration=double.Parse(System.Text.RegularExpressions.Regex.Match(shortJson,"duration[^0-9]+([0-9.]+)").Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);Check(Math.Abs(shorterDuration-Editing.OutputDuration(p,media))<0.0001,"Removing picture shortens audio at output end");
  p=Whole(media);p.KeepOriginalAudio=true;p.Clips.Add(p.Clips[0].Copy());string longer=Path.Combine(dir,"longer.wav");await engine.Export(media,p,longer,false,true,ct,null);string tail=Path.Combine(dir,"longer-tail.raw");await engine.Text("ffmpeg","-v error -nostdin -y -ss 4 -i "+Engine.Q(longer)+" -f s16le "+Engine.Q(tail),ct);Check(File.ReadAllBytes(tail).Length>0&&File.ReadAllBytes(tail).All(x=>x==0),"Picture longer than original sound has silent tail");
 }
}

