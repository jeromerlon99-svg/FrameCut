using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FrameCut {
 public sealed class SequenceTimeline : FrameworkElement {
  public SequenceTimeline(){Focusable=true;FocusVisualStyle=null;ToolTip="点击画面定位，按 S 分割；拖动彩色画面卡片到新的位置。下方原声不随画面重排。";}
  public MediaInfo Media;public Project Project;public int Selected,Current;public double Zoom=1,Offset;
  public Action<int,int> SelectFrame;public Action<int,int> Reorder;
  int pressed=-1,gap=-1;bool moving;Point down;
  static Brush B(string s){return (Brush)new BrushConverter().ConvertFromString(s);}
  static readonly Brush Green=B("#74E0B3"),Purple=B("#B1A0F4"),Muted=B("#8995A5"),White=B("#E9EEF5");
  static readonly string[] Colors={"#345E50","#354C6B","#5B436A","#695838","#355C65","#684951"};
  double Left{get{return 92;}}double W{get{return Math.Max(1,ActualWidth-Left-9);}}
  public double Total{get{return Media==null||Project==null?1:Editing.OutputDuration(Project,Media);}}
  public double Span{get{return Total/Zoom;}}
  double X(double t){return Left+(t-Offset)/Span*W;}
  double T(double x){return Math.Max(0,Math.Min(Total,Offset+(x-Left)/W*Span));}
  void Text(DrawingContext dc,string s,double x,double y,Brush color,int size=10){dc.DrawText(new FormattedText(s,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));}
  protected override void OnRender(DrawingContext dc){
   dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
   Text(dc,"V1  画面",0,41,Green,11);Text(dc,Project!=null&&Project.KeepOriginalAudio?"A1  原声":"A1  声音",0,88,Purple,11);
   dc.DrawRoundedRectangle(B("#1D2830"),null,new Rect(Left,29,W,40),4,4);dc.DrawRoundedRectangle(B("#252335"),null,new Rect(Left,78,W,36),4,4);
   if(Media==null||Project==null){Text(dc,"导入视频 → 点击定位 → 分割画轨 → 拖动片段重新排序",Left+15,41,Muted);return;}
   dc.PushClip(new RectangleGeometry(new Rect(Left-4,0,W+8,Math.Max(125,ActualHeight))));
   double rough=Span/8,step=Math.Pow(10,Math.Floor(Math.Log10(rough)));double u=rough/step;step*=u>5?10:u>2?5:u>1?2:1;
   for(double t=Math.Ceiling(Offset/step)*step;t<=Math.Min(Total,Offset+Span)+0.000001;t+=step){double x=X(t);dc.DrawLine(new Pen(B("#33404D"),1),new Point(x,22),new Point(x,119));Text(dc,Timeline.Clock(t),Math.Min(x+3,Left+W-67),2,Muted);}
   double start=0;
   for(int i=0;i<Project.Clips.Count;i++){
    Clip c=Project.Clips[i];double length=Media.At(c.Out)-Media.At(c.In),a=X(start),b=X(start+length);Rect rect=new Rect(a+1,29,Math.Max(1,b-a-2),40);
    dc.DrawRoundedRectangle(B(Colors[Math.Max(0,c.Id-1)%Colors.Length]),new Pen(i==Selected?Green:B("#526575"),i==Selected?2:1),rect,4,4);
    if(b-a>22){dc.PushClip(new RectangleGeometry(rect));Text(dc,(i+1).ToString("00")+" · "+Editing.ClipName(c),a+9,33,White,12);if(b-a>115)Text(dc,Media.PartCount>1?System.IO.Path.GetFileName(Media.Part(Media.PartIndex(c.In)).Path):"源 "+Timeline.Clock(Media.At(c.In))+" – "+Timeline.Clock(Media.At(c.Out)),a+9,51,Muted,9);dc.Pop();}
    if(!Project.KeepOriginalAudio)dc.DrawRectangle(B("#493B5D"),new Pen(Purple,0.5),new Rect(a+1,78,Math.Max(1,b-a-2),36));
    start+=length;
   }
   if(Media.AudioIndex>=0){
    for(double x=Left;x<Left+W;x+=3){double t=T(x),source=t;bool mute=Project.OriginalMuted;
     if(Project.KeepOriginalAudio){if(!Editing.SoundAt(Project,Media,t,out source))mute=true;}else{int i,f;Editing.Locate(Project,Media,t,out i,out f);Clip c=Project.Clips[i];source=c.AudioIn+t-Editing.OffsetOf(Project,Media,i);mute=c.Muted||source>=c.AudioOut;}
     int slot=(int)(source/Media.Duration*Media.Wave.Length);double v=!mute&&slot>=0&&slot<Media.Wave.Length?Media.Wave[slot]:0;if(v>0){double h=Math.Max(1,Math.Sqrt(v)*13);dc.DrawLine(new Pen(Purple,1.3),new Point(x,96-h),new Point(x,96+h));}
    }
    dc.DrawRoundedRectangle(B("#D0252335"),null,new Rect(Left+7,82,Project.KeepOriginalAudio?185:143,24),3,3);Text(dc,Project.KeepOriginalAudio?"连续原声 · 不随画面切割或重排":"声音跟随对应画面片段",Left+14,87,Purple,10);
   }else Text(dc,"源视频没有音轨",Left+14,88,Muted);
   Clip active=Project.Clips[Math.Max(0,Math.Min(Selected,Project.Clips.Count-1))];double pos=Editing.OffsetOf(Project,Media,Selected)+Math.Max(0,Math.Min(Media.At(active.Out)-Media.At(active.In),Media.At(Current)-Media.At(active.In)));double px=X(pos);
   dc.DrawLine(new Pen(Green,1.5),new Point(px,19),new Point(px,119));dc.DrawRectangle(Green,null,new Rect(px-3,16,6,7));
   if(moving&&gap>=0){double drop=X(Editing.OffsetOf(Project,Media,gap));dc.DrawLine(new Pen(White,3),new Point(drop,25),new Point(drop,73));dc.DrawRectangle(Green,null,new Rect(drop-5,23,10,4));}
   dc.Pop();
  }
  public void SeekAt(double time){if(Project==null||Media==null)return;int index,frame;Editing.Locate(Project,Media,time,out index,out frame);if(SelectFrame!=null)SelectFrame(index,frame);}
  public void DropClip(int from,int insertionGap){if(Reorder!=null)Reorder(from,insertionGap);}
  public int GapAt(double time){double start=0;for(int i=0;i<Project.Clips.Count;i++){Clip c=Project.Clips[i];double d=Media.At(c.Out)-Media.At(c.In);if(time<start+d/2)return i;start+=d;}return Project.Clips.Count;}
  protected override void OnMouseDown(MouseButtonEventArgs e){if(Project==null||Media==null||e.ChangedButton!=MouseButton.Left)return;Point p=e.GetPosition(this);if(p.X<Left)return;double t=T(p.X);int index,frame;Editing.Locate(Project,Media,t,out index,out frame);pressed=p.Y>=29&&p.Y<=69?index:-1;down=p;moving=false;gap=-1;Focus();CaptureMouse();SeekAt(t);e.Handled=true;}
  protected override void OnMouseMove(MouseEventArgs e){if(Project==null||Media==null)return;Point p=e.GetPosition(this);Cursor=p.Y>=29&&p.Y<=69?Cursors.SizeAll:Cursors.Hand;if(!IsMouseCaptured)return;if(pressed>=0&&(moving||Math.Abs(p.X-down.X)>8)){moving=true;gap=GapAt(T(p.X));InvalidateVisual();}else if(pressed<0)SeekAt(T(p.X));}
  protected override void OnMouseUp(MouseButtonEventArgs e){if(!IsMouseCaptured)return;int from=pressed,to=gap;bool drop=moving;ReleaseMouseCapture();pressed=-1;gap=-1;moving=false;if(drop&&from>=0&&to>=0)DropClip(from,to);InvalidateVisual();e.Handled=true;}
  protected override void OnLostMouseCapture(MouseEventArgs e){pressed=-1;gap=-1;moving=false;InvalidateVisual();base.OnLostMouseCapture(e);}
 }
}


