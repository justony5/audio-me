using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using AudioSwitch;

// Records promo footage: drives the real Audio Me window with demo devices (no hardware is changed)
// and captures 30 fps frames of a screen region, plus cursor positions and click times for the video.
class FakeAudio : IAudioService {
    public AudioSnapshot Data=new AudioSnapshot();
    public AudioSnapshot Read() { return Data; }
    public void Apply(Endpoint[] devices) { Thread.Sleep(350); foreach(var d in devices) Data.Defaults[d.Flow]=new[] { d.Id,d.Id,d.Id }; }
}

class Recorder {
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Drawing.Point p);
    class Frame { public long T; public byte[] Jpeg; public Drawing.Point Cursor; }
    readonly Drawing.Rectangle area; readonly string folder;
    readonly Stopwatch clock=new Stopwatch();
    readonly BlockingCollection<Tuple<long,Drawing.Bitmap,Drawing.Point>> pending=new BlockingCollection<Tuple<long,Drawing.Bitmap,Drawing.Point>>(64);
    readonly ConcurrentBag<Frame> frames=new ConcurrentBag<Frame>();
    readonly List<long> clicks=new List<long>();
    readonly List<string> marks=new List<string>();
    Thread grabber; Thread[] encoders; volatile bool running;
    public Recorder(Drawing.Rectangle area,string folder) { this.area=area; this.folder=folder; }
    public long Now { get { return clock.ElapsedMilliseconds; } }
    public Drawing.Rectangle Area { get { return area; } }

    public void Start() {
        // Clear old frames (files may be read-only after being copied around).
        if(Directory.Exists(folder)) foreach(var f in Directory.GetFiles(folder)) { File.SetAttributes(f,FileAttributes.Normal); File.Delete(f); }
        Directory.CreateDirectory(folder);
        var codec=Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");
        var quality=new Drawing.Imaging.EncoderParameters(1); quality.Param[0]=new Drawing.Imaging.EncoderParameter(Drawing.Imaging.Encoder.Quality,94L);
        encoders=Enumerable.Range(0,3).Select(i=>new Thread(()=>{
            foreach(var item in pending.GetConsumingEnumerable()) {
                using(var bitmap=item.Item2) using(var stream=new MemoryStream()) { bitmap.Save(stream,codec,quality); frames.Add(new Frame { T=item.Item1,Jpeg=stream.ToArray(),Cursor=item.Item3 }); }
            }
        }) { IsBackground=true }).ToArray();
        foreach(var e in encoders) e.Start();
        running=true; clock.Start();
        grabber=new Thread(()=>{
            long next=0;
            while(running) {
                long t=clock.ElapsedMilliseconds; if(t<next) { Thread.Sleep(1); continue; }
                var bitmap=new Drawing.Bitmap(area.Width,area.Height,Drawing.Imaging.PixelFormat.Format24bppRgb);
                using(var g=Drawing.Graphics.FromImage(bitmap)) g.CopyFromScreen(area.X,area.Y,0,0,area.Size);
                Drawing.Point cursor; GetCursorPos(out cursor);
                pending.Add(Tuple.Create(t,bitmap,new Drawing.Point(cursor.X-area.X,cursor.Y-area.Y)));
                next+=1000/60;
            }
        }) { IsBackground=true, Priority=ThreadPriority.AboveNormal };
        grabber.Start();
    }
    public void Click() { clicks.Add(Now); }
    public void Mark(string name) { marks.Add("\""+name+"\":"+Now); }
    public void Rect(string name,FrameworkElement e) {
        var a=e.PointToScreen(new Point(0,0)); var b=e.PointToScreen(new Point(e.ActualWidth,e.ActualHeight));
        marks.Add(String.Format("\"{0}\":[{1},{2},{3},{4}]",name,(int)a.X-area.X,(int)a.Y-area.Y,(int)(b.X-a.X),(int)(b.Y-a.Y)));
    }
    // Resamples to an exact 30 fps sequence (nearest earlier capture) and writes meta.json beside the frames.
    public void Stop() {
        running=false; grabber.Join(); pending.CompleteAdding(); foreach(var e in encoders) e.Join(); clock.Stop();
        var list=frames.OrderBy(f=>f.T).ToList(); long end=list.Last().T;
        int count=(int)(end*30/1000)+1; var cursor=new StringBuilder(); int j=0;
        for(int i=0;i<count;i++) {
            long t=i*1000L/30; while(j+1<list.Count && list[j+1].T<=t) j++;
            File.WriteAllBytes(Path.Combine(folder,i.ToString("D4")+".jpg"),list[j].Jpeg);
            cursor.Append(i==0?"":",").Append("["+list[j].Cursor.X+","+list[j].Cursor.Y+"]");
        }
        var meta=String.Format("{{\"width\":{0},\"height\":{1},\"frames\":{2},\"fps\":30,\"cursor\":[{3}],\"clicks\":[{4}],\"marks\":{{{5}}}}}",
            area.Width,area.Height,count,cursor,String.Join(",",clicks.Select(c=>(c*30/1000).ToString())),String.Join(",",marks.Select(m=>{
                // Convert time marks (ms) to frame numbers; rect marks are left as pixels.
                var parts=m.Split(':'); long ms; return parts.Length==2 && Int64.TryParse(parts[1],out ms)?parts[0]+":"+(ms*30/1000):m; })));
        File.WriteAllText(Path.Combine(folder,"meta.json"),meta);
        Console.WriteLine("Recorded "+count+" frames ("+list.Count+" captured) to "+folder);
    }
}

class Capture {
    static readonly Assembly App=typeof(Settings).Assembly;
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,int dx,int dy,uint data,UIntPtr extra);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    static Drawing.Point mouse;
    static Recorder rec;
    static Color teal;

    static object Field(object instance,string name) { return instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance); }
    static object Call(object instance,string name,params object[] args) {
        var method=instance.GetType().GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).First(m=>m.Name==name && m.GetParameters().Length==args.Length);
        return method.Invoke(instance,args);
    }
    static void Pump(int milliseconds) {
        var sw=Stopwatch.StartNew();
        while(sw.ElapsedMilliseconds<milliseconds) {
            var frame=new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate { frame.Continue=false; }));
            Dispatcher.PushFrame(frame); Thread.Sleep(4);
        }
    }
    static IEnumerable<DependencyObject> Tree(DependencyObject root) {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach(var d in Tree(child)) yield return d;
    }
    static Button Named(DependencyObject root,string name) { return Tree(root).OfType<Button>().First(b=>AutomationProperties.GetName(b).StartsWith(name)); }
    static Drawing.Point Center(FrameworkElement e) { var p=e.PointToScreen(new Point(e.ActualWidth/2,e.ActualHeight/2)); return new Drawing.Point((int)p.X,(int)p.Y); }
    static Drawing.Rectangle Bounds(Window w,int padX,int padY) {
        RECT r; DwmGetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(w).Handle,9,out r,Marshal.SizeOf(typeof(RECT)));
        return new Drawing.Rectangle(r.Left-padX,r.Top-padY,r.Right-r.Left+padX*2,r.Bottom-r.Top+padY*2);
    }
    // Glides the real pointer (for genuine hover states) with an ease-in-out curve.
    static void Glide(Drawing.Point to,int ms) {
        var from=mouse; var sw=Stopwatch.StartNew();
        while(true) {
            double t=Math.Min(1,sw.ElapsedMilliseconds/(double)ms), k=t<0.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2;
            mouse=new Drawing.Point((int)(from.X+(to.X-from.X)*k),(int)(from.Y+(to.Y-from.Y)*k)); SetCursorPos(mouse.X,mouse.Y);
            Pump(8); if(t>=1) break;
        }
    }
    static void ClickAt(FrameworkElement e,int glideMs) {
        Glide(Center(e),glideMs); Pump(120);
        if(rec!=null) rec.Click();
        mouse_event(0x0002,0,0,0,UIntPtr.Zero); Pump(110); mouse_event(0x0004,0,0,0,UIntPtr.Zero);
    }
    static void Park(Window w) { mouse=new Drawing.Point(w==null?40:(int)(Bounds(w,0,0).Right+260),Forms.Screen.PrimaryScreen.Bounds.Height/2); SetCursorPos(mouse.X,mouse.Y); }

    [STAThread] static void Main(string[] args) {
        string root=args.Length>0?args[0]:"footage";
        string only=args.Length>1?args[1]:null;
        var theme=App.GetType("AudioSwitch.Theme"); theme.GetMethod("EnsureApp").Invoke(null,null);
        teal=(Color)theme.GetField("Teal").GetValue(null);
        // Hover tooltips would cover the UI in footage.
        var hidden=new Style(typeof(ToolTip)); hidden.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed)); Application.Current.Resources[typeof(ToolTip)]=hidden;
        var audio=new FakeAudio();
        string[] outputs={"Studio Headphones","Desk Speakers","LG Monitor (HDMI)","Bluetooth Earbuds","Realtek Digital Output"};
        string[] inputs={"USB Microphone","Headset Microphone","Webcam Microphone"};
        for(int i=0;i<outputs.Length;i++) audio.Data.Devices.Add(new Endpoint { Id="out"+i,Flow=0,Name=outputs[i] });
        for(int i=0;i<inputs.Length;i++) audio.Data.Devices.Add(new Endpoint { Id="in"+i,Flow=1,Name=inputs[i] });
        // Start on a mix that matches no profile, so the first switch visibly changes "Custom setup".
        audio.Data.Defaults[0]=new[] { "out2","out2","out2" }; audio.Data.Defaults[1]=new[] { "in0","in0","in0" };
        var settings=new Settings();
        settings.Profiles.Add(new Profile { Name="Gaming",Output="out0",Input="in0",Icon="game" });
        settings.Profiles.Add(new Profile { Name="Work",Output="out1",Input="in2",Icon="work" });
        settings.Profiles.Add(new Profile { Name="Music",Output="out1",Input="in0",Icon="music" });
        settings.Profiles.Add(new Profile { Name="Movies",Output="out2",Input="in2",Icon="movie" });
        settings.Profiles.Add(new Profile { Name="Calls",Output="out3",Input="in1",Icon="call" });
        settings.Favorites.Add("out0"); settings.Favorites.Add("in0"); settings.Deemphasized.Add("out4");
        string path=Path.Combine(Path.GetTempPath(),"AudioMe-promo-"+Guid.NewGuid().ToString("N")+".xml"); new SettingsStore(path).Save(settings);

        var screen=Forms.Screen.PrimaryScreen;
        var wallpaper=new Window { WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=0,Top=0,Width=SystemParameters.PrimaryScreenWidth,Height=SystemParameters.PrimaryScreenHeight,Topmost=true,ShowInTaskbar=false };
        var canvas=new Grid { Background=new LinearGradientBrush(Color.FromRgb(0x18,0x1B,0x42),Color.FromRgb(0x0C,0x10,0x20),45) };
        var blobs=new Canvas { ClipToBounds=true }; canvas.Children.Add(blobs);
        Action<double,double,double,Color> blob=(x,y,size,color)=>{ var soft=new RadialGradientBrush(color,Color.FromArgb(0,color.R,color.G,color.B)); soft.GradientStops.Insert(1,new GradientStop(color,0.45));
            var e=new Ellipse { Width=size*1.5,Height=size*1.5,Fill=soft }; Canvas.SetLeft(e,x-size/4); Canvas.SetTop(e,y-size/4); blobs.Children.Add(e); };
        blob(-80,-60,620,Color.FromRgb(0x4A,0x34,0xB0)); blob(520,260,520,Color.FromRgb(0x0E,0x9C,0x9A)); blob(160,600,560,Color.FromRgb(0xC2,0x3D,0x8E)); blob(1100,-40,460,Color.FromRgb(0xF0,0x9A,0x3E)); blob(1200,560,480,Color.FromRgb(0x2B,0x5C,0xD8)); blob(1180,180,560,Color.FromRgb(0x6A,0x3C,0xC8)); blob(1380,560,420,Color.FromRgb(0x0E,0x9C,0x9A));
        // Corner glows anchored to the bottom-right, behind the tray flyout.
        Action<double,double,double,Color> corner=(right,bottom,size,color)=>{ var soft=new RadialGradientBrush(color,Color.FromArgb(0,color.R,color.G,color.B)); soft.GradientStops.Insert(1,new GradientStop(color,0.4));
            canvas.Children.Add(new Ellipse { Width=size,Height=size,Fill=soft,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,right,bottom) }); };
        corner(-120,260,720,Color.FromRgb(0x6A,0x3C,0xC8)); corner(260,-160,640,Color.FromRgb(0x0E,0x9C,0x9A)); corner(-200,-260,600,Color.FromRgb(0xC2,0x3D,0x8E));
        // A plain stand-in taskbar with the Audio Me tray icon, so the flyout has something to open from.
        double dpi=screen.Bounds.Width/SystemParameters.PrimaryScreenWidth;
        double barHeight=Math.Max(48,(screen.Bounds.Bottom-screen.WorkingArea.Bottom)/dpi);
        var bar=new Grid { Height=barHeight,VerticalAlignment=VerticalAlignment.Bottom,Background=new SolidColorBrush(Color.FromArgb(0xD8,0x14,0x16,0x22)) };
        var trayIcon=new Image { Source=new BitmapImage(new Uri(Path.GetFullPath(Path.Combine("Assets","AudioMe.png")))),Width=22,Height=22,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,120,0) };
        RenderOptions.SetBitmapScalingMode(trayIcon,BitmapScalingMode.HighQuality);
        bar.Children.Add(trayIcon);
        var clockText=new TextBlock { Text="9:41 PM",Foreground=Brushes.White,FontSize=12,FontFamily=new FontFamily("Segoe UI Variable Text"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,24,0) };
        bar.Children.Add(clockText);
        canvas.Children.Add(bar);
        wallpaper.Content=canvas; wallpaper.Show(); wallpaper.Activate(); Pump(500);

        var type=App.GetType("AudioSwitch.Widget");
        var form=(Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] { false,audio,new SettingsStore(path) },null);
        form.Topmost=true; form.Width=480; form.Height=860; form.Top=(SystemParameters.PrimaryScreenHeight-barHeight-860)/2; form.Left=(SystemParameters.PrimaryScreenWidth-480)/2;
        form.Show(); form.Activate(); Park(form); Pump(1500);
        var strip=(StackPanel)Field(form,"profileStrip");
        Func<StackPanel,string,Button> tile=(s,name)=>s.Children.OfType<Button>().First(b=>b.Tag is Profile && ((Profile)b.Tag).Name==name);

        // Shot 1: home window, switch Custom setup → Gaming → Music.
        if(only==null || only=="main") {
            rec=new Recorder(Bounds(form,40,40),Path.Combine(root,"main")); rec.Start(); Pump(700);
            rec.Rect("strip",(FrameworkElement)Field(form,"profileScroller")); rec.Rect("now",(FrameworkElement)((FrameworkElement)Field(form,"heroName")).Parent);
            rec.Rect("devices",(FrameworkElement)Field(form,"deviceList"));
            ClickAt(tile(strip,"Gaming"),700); rec.Mark("gaming"); Pump(1700);
            ClickAt(tile(strip,"Music"),650); rec.Mark("music"); Pump(1800);
            Glide(new Drawing.Point(mouse.X+40,mouse.Y+260),500); Pump(600);
            rec.Stop(); rec=null;
        }
        // Shot 2: tray flyout opens from the stand-in taskbar, switch to Work.
        if(only==null || only=="flyout") {
            form.Hide(); Pump(300);
            var tray=trayIcon.PointToScreen(new Point(11,11));
            var area=new Drawing.Rectangle(screen.Bounds.Right-760,screen.Bounds.Bottom-1180,760,1180);
            mouse=new Drawing.Point(area.X+200,area.Y+300); SetCursorPos(mouse.X,mouse.Y);
            rec=new Recorder(area,Path.Combine(root,"flyout")); rec.Start(); Pump(500);
            Glide(new Drawing.Point((int)tray.X,(int)tray.Y),700); Pump(120); rec.Click(); rec.Mark("open");
            Call(form,"ToggleFlyout"); Pump(900);
            var flyout=(Window)Field(form,"flyout"); var fstrip=(StackPanel)Field(flyout,"strip");
            rec.Rect("flyout",(FrameworkElement)flyout.Content);
            ClickAt(tile(fstrip,"Work"),650); rec.Mark("work"); Pump(1500);
            var stack=(StackPanel)Field(flyout,"stack");
            ClickAt(Named(stack,"Choose playback device"),550); rec.Mark("picker"); Pump(1100);
            ClickAt(Named(stack,"Switch playback to Studio Headphones"),600); rec.Mark("headphones"); Pump(1400);
            rec.Stop(); rec=null;
            flyout.Hide(); Pump(200);
        }
        // Shot 3: create a new "Streaming" profile in the editor.
        if(only==null || only=="editor") {
            Call(form,"ShowWidget"); form.Activate(); Pump(600); Park(form);
            rec=new Recorder(Bounds(form,40,40),Path.Combine(root,"editor")); rec.Start(); Pump(400);
            Call(form,"BeginEdit",new object[] { null }); Pump(700);
            var name=(TextBox)Field(form,"profileName"); rec.Mark("typing");
            foreach(var c in "Streaming") { name.Text+=c; name.CaretIndex=name.Text.Length; Pump(85); }
            Pump(300);
            var icons=(WrapPanel)Field(form,"editorIcons");
            ClickAt(Named(icons,"Icon headset"),600); rec.Mark("icon"); Pump(500);
            ClickAt(Named((StackPanel)Field(form,"outputChoices"),"Profile playback device Studio Headphones"),600); Pump(450);
            var page=(ScrollViewer)Field(form,"editorPage");
            for(int i=0;i<=24;i++) { page.ScrollToVerticalOffset(page.ScrollableHeight*(1-Math.Cos(Math.PI*i/24))/2); Pump(16); }
            Pump(250);
            ClickAt(Named((StackPanel)Field(form,"inputChoices"),"Profile microphone USB Microphone"),550); Pump(450);
            ClickAt(Named((DependencyObject)page.Content,"Save profile"),600); rec.Mark("save"); Pump(1900);
            rec.Stop(); rec=null;
        }
        // Shot 4: the accent recolors live (simulating a Windows accent change; Match Windows follows it).
        if(only==null || only=="accent") {
            Call(form,"ShowPage",Field(form,"home"),false); form.Activate(); Park(form);
            Call(form,"ApplyProfile",((Settings)Field(form,"settings")).Profiles[0]); Pump(1400);
            rec=new Recorder(Bounds(form,40,40),Path.Combine(root,"accent")); rec.Start(); Pump(500);
            var stops=new[] { teal,Color.FromRgb(0x4C,0xC2,0xFF),Color.FromRgb(0xB1,0x8C,0xFF),Color.FromRgb(0xFF,0x8A,0xB8),Color.FromRgb(0xFF,0xB0,0x4C),teal };
            for(int s=0;s+1<stops.Length;s++) {
                rec.Mark("color"+s);
                for(int i=0;i<=18;i++) { double k=i/18.0; SetAccent(Lerp(stops[s],stops[s+1],k)); Call(form,"Render"); Pump(14); }
                Pump(450);
            }
            rec.Stop(); rec=null;
        }
        form.GetType().GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true); form.Close(); wallpaper.Close();
        File.Delete(path); if(File.Exists(path+".bak")) File.Delete(path+".bak");
    }
    static Color Lerp(Color a,Color b,double k) { return Color.FromRgb((byte)(a.R+(b.R-a.R)*k),(byte)(a.G+(b.G-a.G)*k),(byte)(a.B+(b.B-a.B)*k)); }
    static SolidColorBrush Frozen(Color c) { var b=new SolidColorBrush(c); b.Freeze(); return b; }
    static void SetAccent(Color color) {
        var r=Application.Current.Resources;
        r["Accent"]=Frozen(color); r["AccentText"]=Frozen(Color.FromRgb(0x08,0x14,0x16));
        r["AccentSoft"]=Frozen(Color.FromArgb(0x26,color.R,color.G,color.B)); r["AccentLine"]=Frozen(Color.FromArgb(0x80,color.R,color.G,color.B));
    }
}
