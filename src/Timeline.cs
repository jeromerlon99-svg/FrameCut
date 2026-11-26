using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FrameCut {
 public sealed class Timeline : FrameworkElement {
  public Timeline(){Focusable=true;FocusVisualStyle=null;}
  public MediaInfo Media; public new Clip Clip; public bool Detached,KeepOriginal; public int Current; public double Zoom=1,Offset;
  public Action<int> Seek; public Action<int,int> Trim; public Action<double,double> AudioTrim; public Action BeginChange,EndChange;
  int drag; double origA,origB; int origIn,origOut;
  static Brush B(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
  static readonly Brush Green=B("#74E0B3"),Purple=B("#B1A0F4"),Muted=B("#8291A3"),White=B("#E9EEF5");
  double Left {get{return 92;}} double W {get{return Math.Max(1,ActualWidth-Left-9);}}
  public double Span {get{return Media==null?1:Media.Duration/Zoom;}}
  double X(double t){return Left+(t-Offset)/Span*W;}
  double T(double x){return Math.Max(0,Math.Min(Media.Duration,Offset+(x-Left)/W*Span));}
  void Text(DrawingContext dc,string s,double x,double y,Brush brush,int size=10){dc.DrawText(new FormattedText(s,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));}
  protected override void OnRender(DrawingContext dc) {
   dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
   double videoY=29,audioY=78;
   Text(dc,"V1  画面",0,videoY+12,Green,11);Text(dc,"A1  声音",0,audioY+12,Purple,11);
   dc.DrawRoundedRectangle(B("#1D2830"),null,new Rect(Left,videoY,W,40),5,5);
   dc.DrawRoundedRectangle(B("#252335"),null,new Rect(Left,audioY,W,40),5,5);
   if(Media==null){Text(dc,"导入视频后，画面与声音将在这里显示",Left+20,videoY+14,Muted);return;}
   dc.PushClip(new RectangleGeometry(new Rect(Left-6,0,W+12,Math.Max(130,ActualHeight))));
   double rough=Span/8,step=Math.Pow(10,Math.Floor(Math.Log10(rough)));double unit=rough/step;step*=unit>5?10:unit>2?5:unit>1?2:1;
   for(double t=Math.Ceiling(Offset/step)*step;t<=Math.Min(Media.Duration,Offset+Span)+step/10;t+=step) {
    double x=X(t);dc.DrawLine(new Pen(B("#33404D"),1),new Point(x,21),new Point(x,122));Text(dc,Clock(t),Math.Min(x+4,Left+W-67),2,Muted);
   }
   if(Clip!=null) {
    double xs=X(Media.At(Clip.In)),xe=X(Media.At(Clip.Out));
    dc.DrawRectangle(B("#375F50"),new Pen(Green,1),new Rect(xs,videoY,Math.Max(1,xe-xs),40));
    if(xe-xs>90)Text(dc,"保留画面 · "+(Clip.Out-Clip.In)+" 帧",Math.Max(xs+12,Left+8),videoY+13,White);
    Handle(dc,xs,videoY,Green,true);Handle(dc,xe,videoY,Green,false);
    double a=X(KeepOriginal?0:Clip.AudioIn),b=X(KeepOriginal?Media.Duration:Clip.AudioOut);
    dc.DrawRectangle(B(Clip.Muted?"#302B39":"#514369"),new Pen(Purple,1),new Rect(a,audioY,Math.Max(1,b-a),40));
    if(Detached){Handle(dc,a,audioY,Purple,true);Handle(dc,b,audioY,Purple,false);}
   }
   if(Media.AudioIndex>=0) {
    for(double x=Left;x<Left+W;x+=3) {
     int i=(int)(T(x)/Media.Duration*Media.Wave.Length);double v=i>=0&&i<Media.Wave.Length?Media.Wave[i]:0;
     if(v>0) {double h=Math.Max(1,Math.Sqrt(v)*16);dc.DrawLine(new Pen(Purple,1.4),new Point(x,audioY+20-h),new Point(x,audioY+20+h));}
    }
   }else Text(dc,"源视频没有音轨",Left+14,audioY+13,Muted);
   double px=X(Media.At(Current));dc.DrawLine(new Pen(Green,1.5),new Point(px,18),new Point(px,125));
   StreamGeometry marker=new StreamGeometry();using(var c=marker.Open()){c.BeginFigure(new Point(px-5,17),true,true);c.LineTo(new Point(px+5,17),true,false);c.LineTo(new Point(px,24),true,false);}dc.DrawGeometry(Green,null,marker);
   dc.Pop();
  }
  void Handle(DrawingContext dc,double x,double y,Brush color,bool start) {
   dc.DrawRoundedRectangle(color,null,new Rect(x-(start?0:6),y,6,40),2,2);
   dc.DrawLine(new Pen(B("#14241F"),1),new Point(x+(start?3:-3),y+14),new Point(x+(start?3:-3),y+26));
  }
  public static string Clock(double seconds) {return TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(seconds>=3600?@"hh\:mm\:ss\.fff":@"mm\:ss\.fff");}
  protected override void OnMouseDown(MouseButtonEventArgs e) {
   if(Media==null||Clip==null||e.ChangedButton!=MouseButton.Left)return;Focus();Point p=e.GetPosition(this);if(p.X<Left)return;
   origIn=Clip.In;origOut=Clip.Out;origA=Clip.AudioIn;origB=Clip.AudioOut;drag=1;
   if(p.Y>=29&&p.Y<=69) {if(Math.Abs(p.X-X(Media.At(Clip.In)))<=9)drag=2;else if(Math.Abs(p.X-X(Media.At(Clip.Out)))<=9)drag=3;}
   if(Detached&&Media.AudioIndex>=0&&p.Y>=78&&p.Y<=118){if(Math.Abs(p.X-X(Clip.AudioIn))<=9)drag=4;else if(Math.Abs(p.X-X(Clip.AudioOut))<=9)drag=5;}
   if(drag>1&&BeginChange!=null)BeginChange();CaptureMouse();Update(p);e.Handled=true;
  }
  protected override void OnMouseMove(MouseEventArgs e) {
   if(Media==null||Clip==null)return;Point p=e.GetPosition(this);
   Cursor=drag>1||p.Y>=29&&p.Y<=69&&(Math.Abs(p.X-X(Media.At(Clip.In)))<=9||Math.Abs(p.X-X(Media.At(Clip.Out)))<=9)||Detached&&p.Y>=78&&p.Y<=118&&(Math.Abs(p.X-X(Clip.AudioIn))<=9||Math.Abs(p.X-X(Clip.AudioOut))<=9)?Cursors.SizeWE:Cursors.Hand;
   if(IsMouseCaptured&&drag>0)Update(p);
  }
  void Update(Point p) {
   double t=T(p.X);int boundary=Media.BoundaryAt(t);
   if(drag==1&&Seek!=null)Seek(Media.FrameAt(t));
   if(drag==2&&Trim!=null)Trim(Math.Min(boundary,origOut-1),origOut);
   if(drag==3&&Trim!=null)Trim(origIn,Math.Max(origIn+1,boundary));
   if(drag==4&&AudioTrim!=null)AudioTrim(Math.Min(t,origB-0.001),origB);
   if(drag==5&&AudioTrim!=null)AudioTrim(origA,Math.Max(origA+0.001,t));
   InvalidateVisual();
  }
  protected override void OnMouseUp(MouseButtonEventArgs e) {if(drag>1&&EndChange!=null)EndChange();drag=0;ReleaseMouseCapture();}
 }
}
