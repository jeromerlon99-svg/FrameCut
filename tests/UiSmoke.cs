using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class UiSmoke {
 static string root,report,picture;static int checks;static Window window;static object controller;
 static FrameCut.Project Project{get{return (FrameCut.Project)typeof(FrameCut.Controller).GetField("project",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);}}
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);checks++;File.AppendAllText(report,"PASS "+name+Environment.NewLine);}
 static T Find<T>(string name) where T:class{return window.FindName(name) as T;}
 static string Label(string name){return Find<TextBlock>(name).Text;}
 static void Click(string name){Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
 static void Toggle(string name,bool value){var c=Find<CheckBox>(name);c.IsChecked=value;c.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));}
 static async Task Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(30);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("Timed out waiting for application state");await Task.Delay(60);}}
 static async Task Jump(int frame){Find<TextBox>("JumpFrame").Text=frame.ToString();Click("Jump");await Until(()=>Label("PreviewTag").EndsWith("第 "+frame+" 帧"));}
 static async Task Reload(){typeof(FrameCut.Controller).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,false);Click("Demo");await Until(()=>Find<ListBox>("ClipList").Items.Count==1&&Find<Grid>("BusyOverlay").Visibility==Visibility.Collapsed&&Label("PreviewTag").EndsWith("第 1 帧"));}
 [STAThread] static void Main(string[] args){root=Path.GetFullPath(args[0]);report=Path.GetFullPath(args[1]);picture=Path.GetFullPath(args[2]);File.WriteAllText(report,"");AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name=="FrameCut"?Assembly.LoadFrom(Path.Combine(root,"FrameCut.exe")):null;Start();}
 static void Start(){Application app=new Application();try {var c=new FrameCut.Controller(new string[0],root);controller=c;window=c.Window;Check(window.Width<=SystemParameters.WorkArea.Width&&window.Height<=SystemParameters.WorkArea.Height,"Default window fits work area");window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-20000;window.Top=-20000;window.ShowInTaskbar=false;window.ShowActivated=false;window.Loaded+=async(s,e)=>{try{await Run();File.AppendAllText(report,"ALL "+checks+" UI CHECKS PASSED"+Environment.NewLine);}catch(Exception ex){File.AppendAllText(report,ex+Environment.NewLine);Environment.ExitCode=1;}finally{typeof(FrameCut.Controller).GetField("dirty",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,false);window.Close();}};app.Run(window);}catch(Exception ex){File.AppendAllText(report,ex.ToString());Environment.ExitCode=1;}}
 static async Task CutThree(){await Jump(65);Click("SplitTrack");await Jump(129);Click("SplitTrack");}
 static void Capture(){window.UpdateLayout();var content=(FrameworkElement)window.Content;RenderTargetBitmap bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);PngBitmapEncoder png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream f=File.Create(picture))png.Save(f);}
 static async Task Run(){
  Check(!Find<Button>("Export").IsEnabled,"Export disabled without media");await Reload();Check(Label("FrameLabel").Contains("192"),"Demo imports 192 real frames");
  Check(Project.KeepOriginalAudio&&Find<CheckBox>("KeepOriginal").IsChecked==true,"Picture-only editing with continuous original sound is default");Check(!Find<CheckBox>("Detach").IsEnabled,"Per-clip audio editing disabled while original sound is continuous");
  Click("NextFrame");await Until(()=>Label("PreviewTag").EndsWith("第 2 帧"));Check(Label("FrameLabel").StartsWith("第 2 /"),"One-frame forward decodes correct frame");Click("PrevFrame");await Until(()=>Label("PreviewTag").EndsWith("第 1 帧"));Check(Label("FrameLabel").StartsWith("第 1 /"),"One-frame back decodes correct frame");
  await CutThree();Check(Project.Clips.Count==3&&Project.Clips.Select(c=>c.Id).SequenceEqual(new[]{1,2,3}),"Two picture cuts create persistent A B C clip cards");Check(Project.Clips.Sum(c=>c.Out-c.In)==192,"Picture cuts preserve every source frame");
  var sequence=(FrameCut.SequenceTimeline)Find<Grid>("SequenceHost").Children[0];sequence.DropClip(2,0);Check(Project.Clips.Select(c=>c.Id).SequenceEqual(new[]{3,1,2}),"Card-drop handler reorders C A B");Check(Project.KeepOriginalAudio&&Project.OriginalGain==1&&!Project.OriginalMuted,"Reorder leaves continuous original sound unchanged");
  Click("Undo");Check(Project.Clips.Select(c=>c.Id).SequenceEqual(new[]{1,2,3}),"Undo reverses card reorder");Click("Redo");Check(Project.Clips.Select(c=>c.Id).SequenceEqual(new[]{3,1,2}),"Redo restores C A B");
  sequence.SeekAt(0);Check(Label("FrameLabel").StartsWith("第 129 /"),"Output timeline start maps to reordered source frame 129");sequence.SeekAt(sequence.Media.At(Project.Clips[0].Out)-sequence.Media.At(Project.Clips[0].In));Check(Label("FrameLabel").StartsWith("第 1 /"),"Output boundary maps to next clip A");Click("PrevFrame");Check(Label("FrameLabel").StartsWith("第 192 /"),"Previous frame crosses reordered A-to-C boundary");Click("NextFrame");Check(Label("FrameLabel").StartsWith("第 1 /"),"Next frame crosses reordered C-to-A boundary");
  await Jump(90);Check(Find<ListBox>("ClipList").SelectedIndex==2,"Source frame jump automatically selects matching clip B");sequence.SeekAt(0);Click("MoveDown");Check(Project.Clips.Select(c=>c.Id).SequenceEqual(new[]{1,3,2}),"Forward/back buttons operate on output card order");Click("MoveUp");Check(Project.Clips.Select(c=>c.Id).SequenceEqual(new[]{3,1,2}),"Move-back restores C A B");
  Toggle("Mute",true);Check(Project.OriginalMuted&&Project.Clips.All(c=>!c.Muted),"Master mute affects original track rather than video clips");Toggle("Mute",false);
  Click("SourceView");Check(Find<Grid>("TimelineHost").Visibility==Visibility.Visible&&Find<Grid>("SequenceHost").Visibility==Visibility.Collapsed,"Source-trimming view switches independently");Click("SequenceView");Check(Find<Grid>("SequenceHost").Visibility==Visibility.Visible,"Arrangement view switches back");
  window.Width=1320;window.Height=870;await Jump(145);await Task.Delay(100);Capture();Check(new FileInfo(picture).Length>10000,"C A B layout renders without desktop automation");
  await Reload();await Jump(37);Click("MarkIn");Check(Find<TextBox>("InFrame").Text=="37","Mark-in remains inclusive");await Jump(96);Click("MarkOut");Check(Find<TextBox>("OutFrame").Text=="96","Mark-out remains inclusive");await Jump(61);Click("Split");Check(Project.Clips.Count==2,"Inspector split remains available");Click("Undo");Check(Project.Clips.Count==1,"Undo split restores trimmed clip");Click("Redo");Check(Project.Clips.Count==2,"Redo split restores parts");
  Toggle("KeepOriginal",false);Toggle("Detach",true);Check(Find<StackPanel>("AudioControls").IsEnabled,"Alternate per-clip audio mode remains available");Find<TextBox>("AudioIn").Text="0.5";Find<TextBox>("AudioOut").Text="1";Click("ApplyAudio");Check(Find<TextBox>("AudioIn").Text=="0.5","Alternate audio range applies");Check(Find<Button>("Export").IsEnabled,"Edited project is exportable");
 }
}

