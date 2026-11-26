using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FrameCut {
 public static class App {
  public static void Trace(string s){string path=Environment.GetEnvironmentVariable("FRAMECUT_TRACE");if(!string.IsNullOrEmpty(path))File.AppendAllText(path,DateTime.UtcNow.ToString("O")+" "+s+Environment.NewLine);}
  [STAThread] public static void Main(string[] args) {
   Trace("Main");
   Application app=new Application();
   app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"帧剪",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
   try{Trace("Controller start");Controller controller=new Controller(args);Trace("Run");app.Run(controller.Window);}catch(Exception ex){Trace(ex.ToString());MessageBox.Show(ex.Message,"帧剪无法启动",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
 }
 public sealed class Controller {
  public Window Window;
  readonly Engine engine; readonly string root;
  MediaInfo media; Project project; string projectPath; bool dirty,refreshing,playing,busy,closed,volumeEditing,mediaFailed;
  int current,renderVersion;bool sequenceView=true; string playbackPath;MediaInfo previewSource,audioInfo,failedAudioSource;string audioSourceKey;double pendingAudioPosition;bool audioShouldPlay;
  readonly Dictionary<string,MediaInfo> catalog=new Dictionary<string,MediaInfo>(StringComparer.OrdinalIgnoreCase);
  readonly Dictionary<string,string> proxies=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  Clip Active {get{return project==null?null:project.Clips[Math.Max(0,Math.Min(Selected,project.Clips.Count-1))];}}
  readonly Stack<Project> undo=new Stack<Project>(),redo=new Stack<Project>();
  CancellationTokenSource busyCts,frameCts,waveCts,lifetime=new CancellationTokenSource();
  MediaPlayer detachedAudio=new MediaPlayer(); DispatcherTimer timer=new DispatcherTimer(),decodeTimer=new DispatcherTimer();
  Timeline timeline=new Timeline();SequenceTimeline sequence=new SequenceTimeline(); ListBox list; MediaElement player; Image still;
  int Selected {get{return Math.Max(0,list.SelectedIndex);}}
  T Get<T>(string name) where T:class {return Window.FindName(name) as T;}
  void Click(string name,Action action){Get<Button>(name).Click+=(s,e)=>Guard(action);}
  void AsyncClick(string name,Func<Task> action){Get<Button>(name).Click+=async(s,e)=>await GuardAsync(action);}
  void Guard(Action action){try{action();}catch(Exception ex){Error(ex);}}
  async void Background(Func<Task> action){await GuardAsync(action);}
  async Task GuardAsync(Func<Task> action){try{await action();}catch(OperationCanceledException){SetStatus("操作已取消。");}catch(Exception ex){Error(ex);}}
  void Error(Exception ex){if(closed)return;SetStatus(ex.Message.Split('\n')[0]);MessageBox.Show(Window,ex.Message,"帧剪",MessageBoxButton.OK,MessageBoxImage.Warning);}
  void SetStatus(string s){if(!closed)Get<TextBlock>("Status").Text=s;}
  public Controller(string[] args,string baseDirectory=null) {
   root=baseDirectory??AppDomain.CurrentDomain.BaseDirectory;engine=new Engine(Path.Combine(root,"engine"));
   App.Trace("Xaml start");using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))Window=(Window)XamlReader.Load(stream);App.Trace("Xaml loaded");
   Rect desktop=SystemParameters.WorkArea;Window.MinWidth=Math.Min(1040,desktop.Width);Window.MinHeight=Math.Min(620,desktop.Height);Window.Width=Math.Min(1320,desktop.Width-20);Window.Height=Math.Min(870,desktop.Height-20);
   list=Get<ListBox>("ClipList");player=Get<MediaElement>("Player");still=Get<Image>("Still");Get<Grid>("TimelineHost").Children.Add(timeline);Get<Grid>("SequenceHost").Children.Add(sequence);Get<Grid>("TimelineHost").Visibility=Visibility.Collapsed;
   sequence.SelectFrame=(index,frame)=>SelectFrame(index,frame);sequence.Reorder=(from,gap)=>MoveToGap(from,gap);
   timeline.Seek=f=>Seek(f,false);timeline.BeginChange=Record;timeline.EndChange=()=>{Refresh();};
   timeline.Trim=(a,b)=>{if(Active!=null){int first=CurrentFirst,last=first+CurrentPart.Frames.Count;a=Math.Max(first,Math.Min(last-1,a));b=Math.Max(a+1,Math.Min(last,b));Editing.Trim(Active,a,b,media,project.Detached);Refresh(false);}};
   timeline.AudioTrim=(a,b)=>{if(Active!=null){Active.AudioIn=Math.Max(0,a);Active.AudioOut=Math.Min(media.Duration,b);Refresh(false);}};
   Click("Import",Import);Click("AddVideo",AddVideo);Click("OpenProject",OpenProject);Click("SaveProject",()=>SaveProject());Click("Undo",Undo);Click("Redo",Redo);
   Click("MoveUp",()=>Move(-1));Click("MoveDown",()=>Move(1));Click("Duplicate",Duplicate);Click("Remove",Remove);
   Click("PrevTen",()=>Step(-10));Click("NextTen",()=>Step(10));Click("PrevFrame",()=>Step(-1));Click("NextFrame",()=>Step(1));Click("Play",TogglePlay);
   Click("SequenceView",()=>SetView(true));Click("SourceView",()=>SetView(false));Click("SplitTrack",Split);Click("PlayAll",PlayFromStart);
   Click("MarkIn",()=>Trim(current,Active.Out));Click("MarkOut",()=>Trim(Active.In,current+1));Click("ApplyTrim",ApplyTrim);Click("Split",Split);Click("Jump",Jump);
   AsyncClick("Snapshot",Snapshot);AsyncClick("Export",Export);AsyncClick("Demo",()=>OpenPath(Path.Combine(root,"示例视频.mp4")));
   Click("Fit",()=>{Get<Slider>("Zoom").Value=1;Get<System.Windows.Controls.Primitives.ScrollBar>("Pan").Value=0;});
   Click("Cancel",()=>{if(busyCts!=null){Get<Button>("Cancel").IsEnabled=false;busyCts.Cancel();}});
   Click("Help",()=>MessageBox.Show(Window,"空格  播放 / 暂停当前片段序列\n← / →  后退 / 前进 1 帧\nShift + ← / →  后退 / 前进 10 帧\nI / O  当前帧设为入点 / 出点（包含该帧）\nS  在当前帧前分割\nHome / End  当前片段首帧 / 尾帧\nCtrl + O  新建视频    Ctrl + S  保存工程\nCtrl + Shift + O  添加视频到当前工程（可多选）\nCtrl + Z / Ctrl + Y  撤销 / 重做\n\n成片编排：点击画面定位，按 S 分割画轨。\n拖动 A、B、C 卡片左右换位，或点前移 / 后移。\n默认只剪画轨；声音按素材加入顺序连续播放。\n新视频可多选追加，不会清空现有剪辑。\n原片定位 / 裁剪：可设置入点、出点或拖动绿色手柄。\n取消只剪画轨后，可恢复片段音频跟随或独立音频范围。\n\n常见 MP4 可直接播放。如格式不兼容，会提示生成本地兼容预览。\n逐帧画面和导出始终读取原文件。","快捷键与使用说明",MessageBoxButton.OK,MessageBoxImage.Information));
   list.SelectionChanged+=(s,e)=>{if(!refreshing&&media!=null){Stop(false);Refresh(false);Seek(Active.In,true);}};
   Get<CheckBox>("KeepOriginal").Click+=(s,e)=>Guard(()=>{if(project==null)return;Record();project.KeepOriginalAudio=Get<CheckBox>("KeepOriginal").IsChecked==true;Refresh();SetStatus(project.KeepOriginalAudio?"只剪画轨：切割、移动画面不会改变原声顺序。":"画面和对应片段音频一起移动。");});
   Get<CheckBox>("Detach").Click+=(s,e)=>Guard(()=>{if(media==null)return;Record();project.Detached=Get<CheckBox>("Detach").IsChecked==true;if(!project.Detached)foreach(var c in project.Clips){c.AudioIn=media.At(c.In);c.AudioOut=media.At(c.Out);}Refresh();});
   Click("ApplyAudio",ApplyAudio);
   Get<CheckBox>("Mute").Click+=(s,e)=>Guard(()=>{if(Active==null)return;Record();bool mute=Get<CheckBox>("Mute").IsChecked==true;if(project.KeepOriginalAudio)project.OriginalMuted=mute;else Active.Muted=mute;Refresh(false);});
   Get<Slider>("Volume").ValueChanged+=(s,e)=>{if(refreshing||Active==null)return;if(!volumeEditing){Record();volumeEditing=true;}if(project.KeepOriginalAudio)project.OriginalGain=e.NewValue;else Active.Gain=e.NewValue;Get<TextBlock>("VolumeLabel").Text=Math.Round(e.NewValue*100)+"%";};
   Get<Slider>("Volume").PreviewMouseUp+=(s,e)=>{volumeEditing=false;};Get<Slider>("Volume").KeyUp+=(s,e)=>{volumeEditing=false;};Get<Slider>("Volume").LostKeyboardFocus+=(s,e)=>{volumeEditing=false;};
   Get<TextBox>("JumpFrame").KeyDown+=(s,e)=>{if(e.Key==Key.Enter){Guard(Jump);e.Handled=true;}};
   Get<Slider>("Zoom").ValueChanged+=(s,e)=>{timeline.Zoom=e.NewValue;sequence.Zoom=e.NewValue;UpdatePan();};
   Get<System.Windows.Controls.Primitives.ScrollBar>("Pan").ValueChanged+=(s,e)=>{if(sequenceView)sequence.Offset=e.NewValue;else timeline.Offset=e.NewValue;timeline.InvalidateVisual();sequence.InvalidateVisual();};
   Window.PreviewKeyDown+=KeyDown;Window.Drop+=async(s,e)=>{if(busy)return;string[] files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&files.Length>0)await GuardAsync(()=>Path.GetExtension(files[0]).Equals(".framecut",StringComparison.OrdinalIgnoreCase)?OpenPath(files[0]):AppendVideos(files));};
   Window.DragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
   Window.Closing+=Closing;Window.Closed+=(s,e)=>{closed=true;lifetime.Cancel();if(frameCts!=null)frameCts.Cancel();if(busyCts!=null)busyCts.Cancel();timer.Stop();decodeTimer.Stop();player.Close();detachedAudio.Close();CleanupProxy();};
   player.MediaFailed+=(s,e)=>{failedAudioSource=null;mediaFailed=true;Stop(false);SetStatus("当前格式无法直接连续播放；逐帧查看仍可用。点击播放可生成兼容预览。");};
   player.MediaOpened+=(s,e)=>{if(media==null)return;player.Position=TimeSpan.FromSeconds(VideoPosition(current));if(playing)player.Play();else player.Pause();};player.MediaEnded+=(s,e)=>AdvancePlayback();
   detachedAudio.MediaOpened+=(s,e)=>{if(closed)return;detachedAudio.Position=TimeSpan.FromSeconds(pendingAudioPosition);if(playing&&audioShouldPlay)detachedAudio.Play();else detachedAudio.Pause();};
   detachedAudio.MediaFailed+=(s,e)=>{if(playing&&project!=null&&(project.Detached||project.KeepOriginalAudio)){Stop(true);failedAudioSource=audioInfo;mediaFailed=true;SetStatus("音轨无法连续预览；再次点击播放可生成兼容预览。");}};
   timer.Interval=TimeSpan.FromMilliseconds(35);timer.Tick+=(s,e)=>Tick();
   decodeTimer.Interval=TimeSpan.FromMilliseconds(75);decodeTimer.Tick+=async(s,e)=>{decodeTimer.Stop();await Decode();};
   App.Trace("Refresh start");Refresh();App.Trace("Refresh done");
   if(args.Length>0)Window.Loaded+=async(s,e)=>await GuardAsync(()=>OpenPath(args[0]));
  }
  void CleanupProxy(){foreach(string path in proxies.Values.Distinct())try{if(File.Exists(path))File.Delete(path);}catch{}proxies.Clear();string dir=Path.Combine(root,".cache");try{if(Directory.Exists(dir)&&Directory.GetFileSystemEntries(dir).Length==0)Directory.Delete(dir);}catch{}}
  bool CanDiscard(){if(!dirty)return true;MessageBoxResult r=MessageBox.Show(Window,"当前工程有未保存的修改，是否保存？","保存工程",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);if(r==MessageBoxResult.Cancel)return false;return r!=MessageBoxResult.Yes||SaveProject();}
  void Closing(object sender,CancelEventArgs e){if(busy){e.Cancel=true;SetStatus("请先取消当前操作，再关闭窗口。");return;}Stop(false);if(!CanDiscard())e.Cancel=true;}
  const string VideoFilter="视频文件|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.webm;*.m4v;*.mts;*.m2ts;*.ts;*.flv;*.mpg;*.mpeg|所有文件|*.*";
  void Import(){if(busy)return;OpenFileDialog d=new OpenFileDialog{Title="新建工程并打开视频",Filter=VideoFilter};if(d.ShowDialog(Window)==true)Background(()=>OpenPath(d.FileName));}
  void AddVideo(){if(busy)return;OpenFileDialog d=new OpenFileDialog{Title="添加视频到当前工程（可多选）",Filter=VideoFilter,Multiselect=true};if(d.ShowDialog(Window)==true)Background(()=>AppendVideos(d.FileNames));}
  void OpenProject(){if(busy)return;OpenFileDialog d=new OpenFileDialog{Title="打开帧剪工程",Filter="帧剪工程|*.framecut"};if(d.ShowDialog(Window)==true)Background(()=>OpenPath(d.FileName));}
  async Task OpenPath(string path) {
   if(busy||!CanDiscard())return;
   if(Path.GetExtension(path).Equals(".framecut",StringComparison.OrdinalIgnoreCase)){
    Project loaded=Editing.Read(path);List<SourceAsset> assets=Assets(loaded);
    foreach(SourceAsset asset in assets){if(!File.Exists(asset.Path)){MessageBox.Show(Window,"找不到素材："+Path.GetFileName(asset.Path)+"。请选择原视频重新关联。","重新关联素材");OpenFileDialog d=new OpenFileDialog{Title="选择原视频："+Path.GetFileName(asset.Path),Filter=VideoFilter};if(d.ShowDialog(Window)!=true)return;asset.Path=d.FileName;}if(asset.Size>0&&new FileInfo(asset.Path).Length!=asset.Size)throw new Exception("素材大小与工程记录不符："+Path.GetFileName(asset.Path));}
    loaded.Sources=assets;loaded.Source=assets[0].Path;await LoadSource(loaded.Source,loaded,path);
   }else await LoadSource(path,null,null);
  }
  async Task Busy(string title,string detail,Func<CancellationToken,Action<double>,Task> action) {
   if(busy)return;Stop(false);busy=true;busyCts=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
   Get<Grid>("BusyOverlay").Visibility=Visibility.Visible;Get<Grid>("Editor").IsEnabled=false;Get<TextBlock>("BusyTitle").Text=title;Get<TextBlock>("BusyDetail").Text=detail;Get<Button>("Cancel").IsEnabled=true;
   Get<ProgressBar>("Progress").Value=0;Get<TextBlock>("ProgressText").Text="0%";
   var progress=new Progress<double>(p=>{if(!closed&&busy){Get<ProgressBar>("Progress").Value=p*100;Get<TextBlock>("ProgressText").Text=Math.Round(p*100)+"%";}});
   try{await action(busyCts.Token,p=>((IProgress<double>)progress).Report(p));}
   finally {busy=false;busyCts.Dispose();busyCts=null;if(!closed){Get<Grid>("BusyOverlay").Visibility=Visibility.Collapsed;Get<Grid>("Editor").IsEnabled=true;Refresh(false);}}
  }
  List<SourceAsset> Assets(Project p){return p.Sources!=null&&p.Sources.Count>0?p.Sources:new List<SourceAsset>{new SourceAsset{Path=p.Source,Size=p.SourceSize,Write=p.SourceWrite}};}
  async Task LoadSource(string path,Project loaded,string loadedPath,bool initialize=false) {
   List<SourceAsset> assets=loaded==null?new List<SourceAsset>{new SourceAsset{Path=path}}:Assets(loaded);if(assets.Count<1||assets.Count>200)throw new Exception("每个工程支持 1–200 个视频素材。");
   List<MediaInfo> parts=new List<MediaInfo>();
   await Busy("读取视频素材","按素材加入顺序建立帧索引，可随时取消。",async(ct,progress)=>{for(int i=0;i<assets.Count;i++){int part=i;SourceAsset asset=assets[i];asset.Path=Path.GetFullPath(asset.Path);if(!File.Exists(asset.Path))throw new Exception("找不到视频："+asset.Path);MediaInfo info=await Task.Run(()=>engine.Inspect(asset.Path,ct,v=>progress((part+v)/assets.Count)));if(asset.Frames>0&&asset.Frames!=info.Frames.Count)throw new Exception("素材帧数已经改变："+Path.GetFileName(asset.Path));parts.Add(info);}if(loaded!=null&&!initialize)Editing.Validate(loaded,MediaInfo.Combine(parts));});
   if(frameCts!=null)frameCts.Cancel();if(waveCts!=null)waveCts.Cancel();player.Close();detachedAudio.Close();audioSourceKey=null;audioShouldPlay=false;previewSource=null;failedAudioSource=null;CleanupProxy();catalog.Clear();foreach(MediaInfo part in parts)catalog[part.Path]=part;
   media=MediaInfo.Combine(parts);project=loaded??new Project{KeepOriginalAudio=true};project.Sources=parts.Select(SourceAsset.From).ToList();project.Source=parts[0].Path;project.SourceSize=project.Sources[0].Size;project.SourceWrite=project.Sources[0].Write;project.Version=2;
   if(loaded==null||initialize){int first=0;double start=0;foreach(MediaInfo part in parts){project.Clips.Add(new Clip{Id=Editing.NextId(project),In=first,Out=first+part.Frames.Count,AudioIn=start,AudioOut=start+part.Duration});first+=part.Frames.Count;start+=part.Duration;}}
   Editing.AssignIds(project);projectPath=loadedPath;dirty=false;undo.Clear();redo.Clear();current=0;Get<StackPanel>("Empty").Visibility=Visibility.Collapsed;SetView(true);Refresh();SelectFrame(0,project.Clips[0].In);StartWave();SetStatus("点击“添加视频到工程”可继续拼接；画面卡片可分割和拖动排序。");
  }
  public async Task AppendVideos(string[] paths){
   if(busy||closed)return;if(paths==null||paths.Length==0)return;if(paths.Any(p=>Path.GetExtension(p).Equals(".framecut",StringComparison.OrdinalIgnoreCase)))throw new Exception("请添加视频文件；工程文件请使用“打开工程”。");
   if(project==null){Project initial=new Project{Source=paths[0],KeepOriginalAudio=true,Sources=paths.Select(path=>new SourceAsset{Path=path}).ToList()};await LoadSource(paths[0],initial,null,true);return;}
   if(project.Clips.Count+paths.Length>200||project.Sources.Count+paths.Length>200)throw new Exception("每个工程最多支持 200 个素材和 200 个画面片段。");
   List<MediaInfo> added=new List<MediaInfo>();
   await Busy("添加视频到当前工程","新视频将接在现有成片末尾；原有剪辑保留。",async(ct,progress)=>{for(int i=0;i<paths.Length;i++){int n=i;string path=Path.GetFullPath(paths[i]);if(!File.Exists(path))throw new Exception("找不到视频："+path);MediaInfo info=await Task.Run(()=>engine.Inspect(path,ct,v=>progress((n+v)/paths.Length)));added.Add(info);}});
   int select=project.Clips.Count,first=media.Frames.Count;double start=media.Duration;List<AudioSpan> sound=Editing.SoundForDuration(project,media,Editing.OutputDuration(project,media));Record();project.OriginalAudio=sound;
   foreach(MediaInfo part in added){catalog[part.Path]=part;project.Sources.Add(SourceAsset.From(part));project.Clips.Add(new Clip{Id=Editing.NextId(project),In=first,Out=first+part.Frames.Count,AudioIn=start,AudioOut=start+part.Duration});project.OriginalAudio.Add(new AudioSpan{Start=start,Length=part.Duration});first+=part.Frames.Count;start+=part.Duration;}
   RebuildMedia();Refresh();list.SelectedIndex=select;SelectFrame(select,project.Clips[select].In);SetStatus("已追加 "+added.Count+" 个视频，共 "+project.Sources.Count+" 个素材；可继续分割、重排并拼接导出。");
  }
  void RebuildMedia(){if(frameCts!=null)frameCts.Cancel();media=MediaInfo.Combine(Assets(project).Select(a=>catalog[a.Path]));current=Math.Max(0,Math.Min(current,media.Frames.Count-1));StartWave();}
  void StartWave(){if(waveCts!=null)waveCts.Cancel();MediaInfo expected=media;waveCts=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var own=waveCts;Background(async()=>{try{double[] wave=await Task.Run(()=>engine.Waveform(expected,own.Token));if(media==expected&&!closed){media.Wave=wave;timeline.InvalidateVisual();sequence.InvalidateVisual();}}catch(OperationCanceledException){}catch{if(media==expected)SetStatus("波形暂不可用，剪辑和导出不受影响。");}finally{if(waveCts==own)waveCts=null;own.Dispose();}});}
  MediaInfo CurrentPart{get{return media.Part(media.PartIndex(Active.In));}}
  int CurrentFirst{get{return media.FirstFrame(media.PartIndex(Active.In));}}
  double VideoPosition(int frame){int i=media.PartIndex(frame);MediaInfo part=media.Part(i);return Math.Max(0,media.At(frame)-media.FirstTime(i)+part.Origin-part.ContainerStart);}
  double VideoGlobalTime(){int i=media.PartIndex(Active.In);MediaInfo part=media.Part(i);return media.FirstTime(i)+player.Position.TotalSeconds-part.Origin+part.ContainerStart;}
  string PreviewPath(MediaInfo part){string path;return proxies.TryGetValue(part.Path,out path)?path:part.Path;}
  void EnsurePreview(int frame){MediaInfo part=media.Part(media.PartIndex(frame));string path=PreviewPath(part);if(previewSource==part&&playbackPath==path)return;player.Close();previewSource=part;playbackPath=path;mediaFailed=false;player.Source=new Uri(path);player.Play();player.Pause();}
  void Record(){if(project==null)return;Stop(true);undo.Push(project.Copy());while(undo.Count>100){Project[] items=undo.ToArray().Take(100).Reverse().ToArray();undo.Clear();foreach(var p in items)undo.Push(p);}redo.Clear();dirty=true;UpdateTitle();}
  void Undo(){if(undo.Count==0)return;Stop(false);redo.Push(project.Copy());project=undo.Pop();dirty=true;RebuildMedia();Refresh();Seek(Math.Max(Active.In,Math.Min(current,Active.Out-1)),true);}
  void Redo(){if(redo.Count==0)return;Stop(false);undo.Push(project.Copy());project=redo.Pop();dirty=true;RebuildMedia();Refresh();Seek(Math.Max(Active.In,Math.Min(current,Active.Out-1)),true);}
  bool SaveProject(){if(project==null)return false;try{if(projectPath==null){SaveFileDialog d=new SaveFileDialog{Title="保存剪辑工程",Filter="帧剪工程|*.framecut",FileName=Path.GetFileNameWithoutExtension(media.Path)+".framecut"};if(d.ShowDialog(Window)!=true)return false;projectPath=d.FileName;}if(!Path.GetExtension(projectPath).Equals(".framecut",StringComparison.OrdinalIgnoreCase)||media.IsSourcePath(projectPath)){projectPath=null;throw new Exception("请将工程保存为 .framecut 文件。");}Editing.Save(project,projectPath);dirty=false;UpdateTitle();SetStatus("工程已保存；再次打开需要保留原视频。");return true;}catch(Exception ex){Error(ex);return false;}}
  string SourceRange(Clip clip){double offset=media.FirstTime(media.PartIndex(clip.In));return "源 "+Timeline.Clock(media.At(clip.In)-offset)+" → "+Timeline.Clock(media.At(clip.Out)-offset);}
  void UpdateTitle(){Window.Title="帧剪 FrameCut · "+(media==null?"绿色版":Path.GetFileName(media.Path)+(media.PartCount>1?" · "+media.PartCount+" 个视频":""))+(dirty?" *":"");}
  void Refresh(bool rebuild=true) {
   refreshing=true;
   try {
    bool ready=project!=null;
    foreach(string n in new[]{"SaveProject","MoveUp","MoveDown","Duplicate","Remove","PrevTen","NextTen","PrevFrame","NextFrame","Play","MarkIn","MarkOut","ApplyTrim","Split","SplitTrack","PlayAll","Jump","Snapshot","Export"})Get<Button>(n).IsEnabled=ready;
    Get<Button>("Undo").IsEnabled=undo.Count>0;Get<Button>("Redo").IsEnabled=redo.Count>0;
    foreach(string n in new[]{"InFrame","OutFrame","JumpFrame"})Get<TextBox>(n).IsEnabled=ready;
    if(ready) {
     int selected=Selected;
     if(rebuild){list.Items.Clear();for(int i=0;i<project.Clips.Count;i++){Clip c=project.Clips[i];StackPanel content=new StackPanel();content.Children.Add(new TextBlock{Text=(i+1).ToString("00")+"   片段 "+Editing.ClipName(c)+(media.PartCount>1?" · "+Path.GetFileName(media.Part(media.PartIndex(c.In)).Path):""),TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=FontWeights.SemiBold});content.Children.Add(new TextBlock{Text=SourceRange(c),FontSize=10,Foreground=Brushes.LightSlateGray,Margin=new Thickness(0,6,0,0)});content.Children.Add(new TextBlock{Text=(c.Out-c.In)+" 帧  /  "+Timeline.Clock(media.At(c.Out)-media.At(c.In)),FontSize=10,Foreground=Brushes.LightGray,Margin=new Thickness(0,4,0,0)});list.Items.Add(new ListBoxItem{Content=content});}list.SelectedIndex=Math.Min(selected,project.Clips.Count-1);}
     Clip c0=Active;Get<TextBox>("InFrame").Text=(c0.In-CurrentFirst+1).ToString();Get<TextBox>("OutFrame").Text=(c0.Out-CurrentFirst).ToString();Get<TextBox>("AudioIn").Text=Engine.N(c0.AudioIn);Get<TextBox>("AudioOut").Text=Engine.N(c0.AudioOut);
     Get<CheckBox>("KeepOriginal").IsEnabled=media.AudioIndex>=0;Get<CheckBox>("KeepOriginal").IsChecked=project.KeepOriginalAudio;
     Get<CheckBox>("Detach").IsChecked=project.Detached;Get<CheckBox>("Detach").IsEnabled=media.AudioIndex>=0&&!project.KeepOriginalAudio;Get<StackPanel>("AudioControls").IsEnabled=project.Detached&&media.AudioIndex>=0&&!project.KeepOriginalAudio;Get<StackPanel>("AudioControls").Visibility=project.Detached&&!project.KeepOriginalAudio?Visibility.Visible:Visibility.Collapsed;
     double gain=project.KeepOriginalAudio?project.OriginalGain:c0.Gain;Get<CheckBox>("Mute").IsEnabled=media.AudioIndex>=0;Get<CheckBox>("Mute").Content=project.KeepOriginalAudio?"静音整条原声":"静音此片段";Get<CheckBox>("Mute").IsChecked=project.KeepOriginalAudio?project.OriginalMuted:c0.Muted;Get<Slider>("Volume").IsEnabled=media.AudioIndex>=0;Get<Slider>("Volume").Value=gain;Get<TextBlock>("VolumeLabel").Text=Math.Round(gain*100)+"%";
     Get<TextBlock>("AudioHint").Text=media.AudioIndex<0?"此视频没有音轨，可正常剪辑画面。":project.KeepOriginalAudio?"声音按素材加入顺序连续拼接，不随画面重排；无声视频补静音。按成片长度截取，过长部分补静音。":project.Detached?"声音从片段开头播放；过短补静音，过长截断。":"音画联动：裁剪画面时同步裁剪声音";
     Get<TextBlock>("LinkLabel").Text=project.KeepOriginalAudio?"● 画面可重排 · 原声连续":project.Detached?"● 音画已分离":"● 音画联动";
     Get<TextBlock>("ClipCount").Text=project.Clips.Count.ToString("00");Get<TextBlock>("SequenceDuration").Text="成片时长  "+Timeline.Clock(project.Clips.Sum(c=>media.At(c.Out)-media.At(c.In)));
     MediaInfo native=CurrentPart;Get<TextBlock>("SourceName").Text=Path.GetFileName(native.Path);Get<TextBlock>("SourceMeta").Text="素材 "+(media.PartIndex(Active.In)+1)+" / "+media.PartCount+"  ·  "+native.DisplayWidth+" × "+native.DisplayHeight+"\n"+Timeline.Clock(native.Duration)+"  /  "+native.Fps.ToString("0.###",CultureInfo.InvariantCulture)+" fps\n"+native.Frames.Count.ToString("N0")+" 帧 · "+(native.HasAudio?"含音轨":"无音轨");
    } else {Get<CheckBox>("KeepOriginal").IsEnabled=false;Get<CheckBox>("Detach").IsEnabled=false;Get<StackPanel>("AudioControls").IsEnabled=false;Get<StackPanel>("AudioControls").Visibility=Visibility.Collapsed;Get<CheckBox>("Mute").IsEnabled=false;Get<Slider>("Volume").IsEnabled=false;}
    timeline.Media=media;timeline.Clip=Active;timeline.KeepOriginal=ready&&project.KeepOriginalAudio;timeline.Detached=ready&&project.Detached&&!project.KeepOriginalAudio;timeline.Current=current;timeline.InvalidateVisual();sequence.Media=media;sequence.Project=project;sequence.Selected=Selected;sequence.Current=current;sequence.InvalidateVisual();UpdatePan();UpdateTitle();if(ready)UpdatePosition();
   }finally{refreshing=false;}
  }
  void SetView(bool edited){sequenceView=edited;Get<Grid>("SequenceHost").Visibility=edited?Visibility.Visible:Visibility.Collapsed;Get<Grid>("TimelineHost").Visibility=edited?Visibility.Collapsed:Visibility.Visible;Get<Button>("SequenceView").Style=(Style)Window.FindResource(edited?"Accent":(object)typeof(Button));Get<Button>("SourceView").Style=(Style)Window.FindResource(edited?(object)typeof(Button):"Accent");Get<System.Windows.Controls.Primitives.ScrollBar>("Pan").Value=0;UpdatePan();}
  void UpdatePan(){var pan=Get<System.Windows.Controls.Primitives.ScrollBar>("Pan");double span=sequenceView?sequence.Span:timeline.Span,total=media==null?1:sequenceView?sequence.Total:media.Duration;pan.Maximum=Math.Max(0,total-span);pan.ViewportSize=span;pan.LargeChange=span/2;pan.SmallChange=span/10;if(sequenceView)sequence.Offset=pan.Value;else timeline.Offset=pan.Value;Get<TextBlock>("ZoomLabel").Text=timeline.Zoom.ToString("0.0")+"×";timeline.InvalidateVisual();sequence.InvalidateVisual();}
  void SelectFrame(int index,int frame){if(project==null)return;Stop(false);if(list.SelectedIndex!=index){refreshing=true;list.SelectedIndex=index;refreshing=false;Refresh(false);}Seek(frame,true);}
  void Step(int delta){if(project==null)return;if(!sequenceView){Seek(current+delta,true);return;}int index,frame;Editing.Step(project,Selected,current,delta,out index,out frame);SelectFrame(index,frame);}
  void MoveToGap(int from,int gap){if(project==null)return;int to=Editing.TargetForGap(project.Clips.Count,from,gap);if(from==to||from<0||from>=project.Clips.Count)return;Record();Editing.Move(project,from,to);Refresh();list.SelectedIndex=to;Seek(Active.In,true);SetStatus("画面顺序："+string.Join(" → ",project.Clips.Select(Editing.ClipName))+(project.KeepOriginalAudio?"；原音轨保持连续。":""));}
  void PlayFromStart(){if(project==null)return;Stop(false);SelectFrame(0,project.Clips[0].In);TogglePlay();}
  void Trim(int a,int b){if(Active==null)return;Clip check=Active.Copy();Editing.Trim(check,a,b,media,project.Detached);Record();Editing.Trim(Active,a,b,media,project.Detached);Refresh();SetStatus("已保留第 "+(a+1)+"–"+b+" 帧，共 "+(b-a)+" 帧。");}
  void ApplyTrim(){int a,b;if(!int.TryParse(Get<TextBox>("InFrame").Text,out a)||!int.TryParse(Get<TextBox>("OutFrame").Text,out b))throw new Exception("请输入当前素材的整数帧号。");Trim(CurrentFirst+a-1,CurrentFirst+b);}
  void ApplyAudio(){if(Active==null||!project.Detached)return;double a,b;if(!double.TryParse(Get<TextBox>("AudioIn").Text,NumberStyles.Float,CultureInfo.InvariantCulture,out a)||!double.TryParse(Get<TextBox>("AudioOut").Text,NumberStyles.Float,CultureInfo.InvariantCulture,out b)||double.IsNaN(a)||double.IsInfinity(a)||double.IsNaN(b)||double.IsInfinity(b)||a<0||b>media.Duration||b<=a)throw new Exception("请输入有效的音频秒数，入点应早于出点，且不超过源视频时长。");Record();Active.AudioIn=a;Active.AudioOut=b;Refresh();SetStatus("已独立调整此片段的音频范围。");}
  void Split(){if(Active==null)return;if(project.Clips.Count>=200)throw new Exception("最多支持 200 个片段。");if(current<=Active.In||current>=Active.Out)throw new Exception("请把播放头移到所选画面片段的内部，再点击分割画轨。");int index=Selected,id=Editing.NextId(project);Record();Clip next=Editing.Split(Active,current,media,project.Detached);next.Id=id;project.Clips.Insert(index+1,next);Refresh();list.SelectedIndex=index+1;SetStatus("已切成 "+project.Clips.Count+" 段；在下方成片编排轨拖动画面卡片即可重排。"+(project.KeepOriginalAudio?"原声没有切割。":""));}
  void Duplicate(){if(Active==null)return;if(project.Clips.Count>=200)throw new Exception("最多支持 200 个片段。");int i=Selected;Clip copy=Active.Copy();copy.Id=Editing.NextId(project);Record();project.Clips.Insert(i+1,copy);Refresh();list.SelectedIndex=i+1;}
  void Move(int delta){if(project==null)return;int i=Selected,j=i+delta;if(j<0||j>=project.Clips.Count)return;MoveToGap(i,j>i?j+1:j);}
  void Remove(){if(project==null)return;if(project.Clips.Count==1)throw new Exception("请至少保留一个片段。可以修改它的入点和出点。");int i=Selected;Record();project.Clips.RemoveAt(i);Refresh();list.SelectedIndex=Math.Min(i,project.Clips.Count-1);SetStatus("已移除片段，可用 Ctrl+Z 撤销。");}
  void Jump(){if(media==null)return;int n;if(!int.TryParse(Get<TextBox>("JumpFrame").Text,out n)||n<1||n>CurrentPart.Frames.Count)throw new Exception("当前视频的帧号应在 1 到 "+CurrentPart.Frames.Count+" 之间。");Seek(CurrentFirst+n-1,true);}
  void UpdatePosition(){if(media==null)return;int index=media.PartIndex(current),local=current-media.FirstFrame(index);MediaInfo part=media.Part(index);Get<TextBlock>("Timecode").Text=TimeSpan.FromSeconds(media.At(current)-media.FirstTime(index)).ToString(@"hh\:mm\:ss\.fff");Get<TextBlock>("FrameLabel").Text="第 "+(local+1).ToString("N0")+" / "+part.Frames.Count.ToString("N0")+" 源帧";Get<TextBox>("JumpFrame").Text=(local+1).ToString();double elapsed=Editing.OffsetOf(project,media,Selected)+Math.Max(0,Math.Min(media.At(Active.Out)-media.At(Active.In),media.At(current)-media.At(Active.In)));Get<TextBlock>("OutputTimecode").Text="成片 "+Timeline.Clock(elapsed)+" / "+Timeline.Clock(Editing.OutputDuration(project,media));timeline.Current=current;timeline.InvalidateVisual();sequence.Selected=Selected;sequence.Current=current;sequence.InvalidateVisual();}
  void Seek(int frame,bool immediate){if(media==null)return;Stop(false);int requested=Math.Max(0,Math.Min(frame,media.Frames.Count-1));if(requested<Active.In||requested>=Active.Out){int match=project.Clips.FindIndex(c=>requested>=c.In&&requested<c.Out);if(match>=0){refreshing=true;list.SelectedIndex=match;refreshing=false;Refresh(false);}}current=requested;EnsurePreview(current);UpdatePosition();if(immediate||timeline.Zoom>1){double t=sequenceView?Editing.OffsetOf(project,media,Selected)+Math.Max(0,media.At(current)-media.At(Active.In)):media.At(current),offset=sequenceView?sequence.Offset:timeline.Offset,span=sequenceView?sequence.Span:timeline.Span;if(t<offset||t>offset+span)Get<System.Windows.Controls.Primitives.ScrollBar>("Pan").Value=Math.Max(0,t-span/2);}renderVersion++;if(frameCts!=null)frameCts.Cancel();Get<Border>("DecodeBadge").Visibility=Visibility.Visible;decodeTimer.Stop();decodeTimer.Interval=TimeSpan.FromMilliseconds(immediate?1:75);decodeTimer.Start();}
  async Task Decode(){if(media==null||closed)return;int version=renderVersion,requestedFrame=current;MediaInfo expected=media;frameCts=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var own=frameCts;
   try {byte[] bytes=await Task.Run(()=>engine.Frame(expected,requestedFrame,1440,own.Token));if(closed||version!=renderVersion||media!=expected)return;BitmapImage bitmap=new BitmapImage();using(var ms=new MemoryStream(bytes)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=ms;bitmap.EndInit();bitmap.Freeze();}still.Source=bitmap;still.Visibility=Visibility.Visible;Get<TextBlock>("PreviewTag").Text="逐帧 · 第 "+(current-media.FirstFrame(media.PartIndex(current))+1)+" 帧";}
   catch(OperationCanceledException){}catch(Exception ex){if(version==renderVersion)SetStatus(ex.Message.Split('\n')[0]);}
   finally{if(version==renderVersion&&!closed)Get<Border>("DecodeBadge").Visibility=Visibility.Collapsed;if(frameCts==own)frameCts=null;own.Dispose();}
  }
  void Stop(bool showFrame){if(!playing)return;playing=false;audioShouldPlay=false;timer.Stop();player.Pause();detachedAudio.Pause();Get<Button>("Play").Content="▶";if(showFrame)Seek(current,true);}
  void TogglePlay(){if(media==null||busy)return;if(playing){Stop(true);return;}if(mediaFailed){Background(PrepareProxy);return;}StartPlay();}
  void StartPlay(bool continueOriginal=false){if(current<Active.In||current>=Active.Out-1)current=Active.In;EnsurePreview(current);if(frameCts!=null)frameCts.Cancel();decodeTimer.Stop();renderVersion++;Get<Border>("DecodeBadge").Visibility=Visibility.Collapsed;still.Visibility=Visibility.Collapsed;playing=true;player.Position=TimeSpan.FromSeconds(VideoPosition(current));player.IsMuted=project.KeepOriginalAudio||project.Detached||Active.Muted;player.Volume=Math.Min(1,Active.Gain);player.Play();SyncAudio(!continueOriginal,media.At(current)-media.At(Active.In));timer.Start();Get<Button>("Play").Content="Ⅱ";Get<TextBlock>("PreviewTag").Text="播放 · 片段 "+Editing.ClipName(Active);}
  void SyncAudio(bool force,double? elapsedOverride=null){
   if((!project.Detached&&!project.KeepOriginalAudio)||!media.HasAudio){audioShouldPlay=false;detachedAudio.Pause();return;}
   double elapsed=elapsedOverride??VideoGlobalTime()-media.At(Active.In),desired=(project.KeepOriginalAudio?Editing.OffsetOf(project,media,Selected):Active.AudioIn)+elapsed,end=project.KeepOriginalAudio?Editing.SoundDuration(project,media):Active.AudioOut;
   double raw=desired;bool audible=!project.KeepOriginalAudio||Editing.SoundAt(project,media,desired,out raw);bool mute=(project.KeepOriginalAudio?project.OriginalMuted:Active.Muted)||!audible;int index=media.PartAtTime(raw);MediaInfo part=media.Part(index);
   if(desired>=end||mute||!part.HasAudio){audioShouldPlay=false;detachedAudio.Pause();return;}
   double position=Math.Max(0,raw-media.FirstTime(index)+part.Origin-part.ContainerStart);string path=PreviewPath(part),key=index+"|"+path;bool changed=audioSourceKey!=key;
   pendingAudioPosition=position;audioShouldPlay=true;detachedAudio.Volume=Math.Min(1,project.KeepOriginalAudio?project.OriginalGain:Active.Gain);
   if(changed){audioSourceKey=key;audioInfo=part;detachedAudio.Close();detachedAudio.Open(new Uri(path));}
   if(force||changed||Math.Abs(detachedAudio.Position.TotalSeconds-position)>0.16)detachedAudio.Position=TimeSpan.FromSeconds(position);detachedAudio.Play();
  }
  void Tick(){if(!playing||media==null)return;double t=VideoGlobalTime();if(t>=media.At(Active.Out)-0.005){AdvancePlayback();return;}current=Math.Max(Active.In,Math.Min(Active.Out-1,media.FrameAt(t)));UpdatePosition();SyncAudio(false);}
  void AdvancePlayback(){if(!playing||project==null)return;int next=Selected+1;if(next>=project.Clips.Count){Stop(false);current=Active.Out-1;Seek(current,true);SetStatus("片段序列播放结束。");return;}bool continuous=project.KeepOriginalAudio;if(continuous){playing=false;timer.Stop();player.Pause();refreshing=true;list.SelectedIndex=next;refreshing=false;Refresh(false);}else{Stop(false);list.SelectedIndex=next;}current=Active.In;StartPlay(continuous);}
  async Task PrepareProxy(){if(MessageBox.Show(Window,"当前素材无法直接连续播放。是否为它生成本地兼容预览？\n逐帧查看和导出仍使用原文件。","兼容预览",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;MediaInfo source=failedAudioSource??CurrentPart;string dir=Path.Combine(root,".cache");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"preview-"+Guid.NewGuid().ToString("N")+".mp4");try{await Busy("生成兼容预览","仅转换当前素材，不改动原文件。",async(ct,p)=>await Task.Run(()=>engine.Proxy(source,path,ct,p)));string old;if(proxies.TryGetValue(source.Path,out old))try{File.Delete(old);}catch{}proxies[source.Path]=path;failedAudioSource=null;previewSource=null;EnsurePreview(current);mediaFailed=false;audioSourceKey=null;SetStatus("当前素材的兼容预览已就绪，点击播放。");}catch{try{if(File.Exists(path))File.Delete(path);}catch{}throw;}}
  async Task Snapshot(){if(media==null)return;Stop(false);SaveFileDialog d=new SaveFileDialog{Title="保存当前原尺寸画面",Filter="PNG 图像|*.png",FileName=Path.GetFileNameWithoutExtension(CurrentPart.Path)+"_frame_"+(current-CurrentFirst+1)+".png"};if(d.ShowDialog(Window)!=true)return;if(!Path.GetExtension(d.FileName).Equals(".png",StringComparison.OrdinalIgnoreCase)||media.IsSourcePath(d.FileName))throw new Exception("请将画面另存为新的 .png 文件。");await Busy("保存原尺寸画面","正在从原视频读取第 "+(current+1)+" 帧。",async(ct,p)=>{byte[] image=await Task.Run(()=>engine.Frame(media,current,0,ct));ct.ThrowIfCancellationRequested();Editing.WriteBytes(d.FileName,image);p(1);});SetStatus("已保存第 "+(current+1)+" 帧："+d.FileName);}
  void KeyDown(object sender,KeyEventArgs e){if(busy)return;bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0,shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
   if(ctrl){if(e.Key==Key.O){Guard(shift?(Action)AddVideo:Import);e.Handled=true;}if(e.Key==Key.S){SaveProject();e.Handled=true;}if(e.Key==Key.Z){Undo();e.Handled=true;}if(e.Key==Key.Y){Redo();e.Handled=true;}return;}
   if(Keyboard.FocusedElement is TextBox||Keyboard.FocusedElement is Slider||project==null)return;
   switch(e.Key){case Key.Space:Guard(TogglePlay);break;case Key.Left:Step(-(shift?10:1));break;case Key.Right:Step(shift?10:1);break;case Key.I:Guard(()=>Trim(current,Active.Out));break;case Key.O:Guard(()=>Trim(Active.In,current+1));break;case Key.S:Guard(Split);break;case Key.Home:Seek(Active.In,true);break;case Key.End:Seek(Active.Out-1,true);break;default:return;}e.Handled=true;
  }
  int lastExportMode;
  async Task Export(){if(media==null)return;Stop(false);int mode=ExportChoice();if(mode<0)return;lastExportMode=mode;bool audioOnly=mode==2,split=mode==3;
   SaveFileDialog d=new SaveFileDialog{Title=split?"选择分离文件的保存位置和基础名称":"导出剪辑",Filter=audioOnly?"M4A 音频|*.m4a|无损 WAV 音频|*.wav":"MP4 视频|*.mp4",FileName=Path.GetFileNameWithoutExtension(media.Path)+"_剪辑"+(audioOnly?".m4a":".mp4")};if(d.ShowDialog(Window)!=true)return;
   List<string> targets=new List<string>();
   if(split){string stem=Path.Combine(Path.GetDirectoryName(d.FileName),Path.GetFileNameWithoutExtension(d.FileName));targets.Add(Unique(stem+"_画面.mp4"));targets.Add(Unique(stem+"_音轨.m4a"));}else targets.Add(d.FileName);
   foreach(string target in targets)if(media.IsSourcePath(target))throw new Exception("请另存为新文件，不能覆盖源视频。");
   List<string> completed=new List<string>();
   try{await Busy(split?"分别导出画面与音轨":"导出剪辑","按照左侧片段顺序生成新文件。精确裁剪会重新编码，请保持窗口打开。",async(ct,p)=>{Project snapshot=project.Copy();for(int i=0;i<targets.Count;i++){int part=i;await Task.Run(()=>engine.Export(media,snapshot,targets[part],split?part==0:!audioOnly,split?part==1:mode!=1,ct,value=>p((part+value)/targets.Count)));completed.Add(targets[part]);}});SetStatus("导出完成："+string.Join("；",targets));MessageBox.Show(Window,"已保存：\n\n"+string.Join("\n\n",targets),"导出完成",MessageBoxButton.OK,MessageBoxImage.Information);}
   catch(OperationCanceledException){SetStatus("导出已取消。"+(completed.Count>0?"已完成文件保留："+string.Join("；",completed):""));}
  }
  static string Unique(string path){if(!File.Exists(path))return path;string stem=Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)),ext=Path.GetExtension(path);int i=2;while(File.Exists(stem+"_"+i+ext))i++;return stem+"_"+i+ext;}
  int ExportChoice(){Window dialog=new Window{Title="导出设置",Width=440,Height=405,ResizeMode=ResizeMode.NoResize,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(25,32,40)),Foreground=Brushes.White,FontFamily=Window.FontFamily,ShowInTaskbar=false};StackPanel p=new StackPanel{Margin=new Thickness(26)};p.Children.Add(new TextBlock{Text="把剪辑带出去",FontSize=22,FontWeight=FontWeights.SemiBold});p.Children.Add(new TextBlock{Text=media.PartCount>1?"按画面编排导出 · 画幅以首段视频为准":"按画面编排导出 · 原尺寸 · 精确到帧",Foreground=Brushes.LightSlateGray,Margin=new Thickness(0,10,0,18)});ComboBox choices=new ComboBox{FontSize=14,Padding=new Thickness(9),Foreground=Brushes.Black};choices.Items.Add("成片 MP4（画面 + 声音）");choices.Items.Add("仅画面 MP4（无音轨）");choices.Items.Add("仅音轨 M4A / WAV");choices.Items.Add("音画分离（MP4 画面 + M4A 音轨）");choices.SelectedIndex=lastExportMode;p.Children.Add(choices);TextBlock detail=new TextBlock{Text="视频：H.264 / 高画质\n音频：AAC 192 kbps（或无损 WAV）\n\n"+(project.KeepOriginalAudio?"原声保持连续，不随画面片段重排。":"音频按片段设置导出。")+"\n输出为新文件，原素材不变。",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brushes.LightGray,Margin=new Thickness(0,18,0,16)};p.Children.Add(detail);Button save=new Button{Content="选择位置并导出",Padding=new Thickness(14,9,14,9),Background=new SolidColorBrush(Color.FromRgb(116,224,179)),Foreground=Brushes.Black,BorderThickness=new Thickness(0)};int result=-1;save.Click+=(s,e)=>{if(media.AudioIndex<0&&(choices.SelectedIndex==2||choices.SelectedIndex==3)){MessageBox.Show(dialog,"源视频没有音轨，请选择成片或仅画面。");return;}result=choices.SelectedIndex;dialog.DialogResult=true;};p.Children.Add(save);dialog.Content=p;dialog.ShowDialog();return result;}
 }
}









