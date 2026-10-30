using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using AudioSwitch;
class FakeAudio : IAudioService {
    public AudioSnapshot Data=new AudioSnapshot(); public int Writes; public Endpoint[] LastApplied; public bool Succeed;
    public ManualResetEvent ReleaseSwitch=new ManualResetEvent(false);
    public AudioSnapshot Read() { return Data; }
    public void Apply(Endpoint[] devices) {
        LastApplied=devices; Interlocked.Increment(ref Writes);
        if(Succeed) { foreach(var d in devices) Data.Defaults[d.Flow]=new[] { d.Id,d.Id,d.Id }; return; }
        ReleaseSwitch.WaitOne(15000); throw new InvalidOperationException("Simulated failure; no hardware was changed.");
    }
}
class UiChecks {
    static readonly Assembly App=typeof(Settings).Assembly;
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
            Dispatcher.PushFrame(frame); Thread.Sleep(5);
        }
    }
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void WaitFor(Func<bool> condition,string message) {
        var elapsed=Stopwatch.StartNew(); while(!condition() && elapsed.ElapsedMilliseconds<4000) Pump(25);
        Check(condition(),message);
    }
    static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,b)); }
    static IEnumerable<DependencyObject> Tree(DependencyObject root) {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach(var d in Tree(child)) yield return d;
    }
    static Window Create(FakeAudio audio,string path) {
        var type=App.GetType("AudioSwitch.Widget");
        return (Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] { false,audio,new SettingsStore(path) },null);
    }
    static void Snapshot(Window window,string file) {
        var root=(FrameworkElement)window.Content;
        int width=(int)Math.Ceiling(root.ActualWidth), height=(int)Math.Ceiling(root.ActualHeight);
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) {
            dc.DrawRectangle((Brush)App.GetType("AudioSwitch.Theme").GetMethod("Surface").Invoke(null,new object[] { false }),null,new Rect(0,0,width,height));
            dc.DrawRectangle(new VisualBrush(root),null,new Rect(0,0,width,height));
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(file)) encoder.Save(stream);
    }
    [STAThread] static void Main(string[] args) {
        App.GetType("AudioSwitch.Theme").GetMethod("EnsureApp").Invoke(null,null);
        if(args.Length>0 && args[0]=="--screenshots") { Screenshots(args.Length>1?args[1]:Path.Combine("docs","screenshots")); return; }
        string root=Path.GetFullPath(Path.Combine("bin","ui-validation")); Directory.CreateDirectory(root);
        var audio=new FakeAudio();
        for(int i=0;i<8;i++) audio.Data.Devices.Add(new Endpoint { Id="speaker"+i,Flow=0,Name="Test playback device "+i });
        for(int i=0;i<4;i++) audio.Data.Devices.Add(new Endpoint { Id="mic"+i,Flow=1,Name="Test microphone "+i });
        audio.Data.Defaults[0]=new[] { "speaker0","speaker0","speaker1" }; audio.Data.Defaults[1]=new[] { "mic0","mic0","mic0" };
        var settings=new Settings(); settings.Profiles.Add(new Profile { Name="Work",Output="speaker0",Input="mic0" });
        settings.Profiles.Add(new Profile { Name="Disconnected headset",Output="missing",Input="mic0",OutputName="USB headset" }); settings.Deemphasized.Add("speaker7");
        string path=Path.Combine(root,"settings.xml"); new SettingsStore(path).Save(settings);
        var form=Create(audio,path);
        form.Title="Audio Me · simulated UI validation"; form.Show(); Pump(400);
        var strip=(StackPanel)Field(form,"profileStrip");
        Check(strip.Children.Count==3,"Profiles or New tile missing: "+strip.Children.Count);
        var heroName=(TextBlock)Field(form,"heroName");
        Check(heroName.Text=="Custom setup","Split role setup marked active: "+heroName.Text);
        var offlineTile=(Button)strip.Children[1];
        Check(!offlineTile.IsEnabled && AutomationProperties.GetName(offlineTile).Contains("unavailable"),"Disconnected profile looks available");
        Call(form,"BuildTrayMenu");
        var trayMenu=(Forms.ContextMenuStrip)Field(form,"trayMenu");
        var trayProfiles=trayMenu.Items.OfType<Forms.ToolStripMenuItem>().Where(i=>i.Tag is Profile).ToArray();
        Check(trayProfiles.Length==2 && trayProfiles[0].Enabled && !trayProfiles[1].Enabled,"Tray profile availability incorrect");
        Check(trayProfiles[1].ToolTipText.Contains("Playback missing"),"Tray profile lacks missing-device reason");
        var playback=(StackPanel)Field(form,"deviceList");
        var chips=Tree(playback.Children[0]).OfType<Border>().Where(b=>b.Tag is int[]).Select(b=>(int[])b.Tag).ToArray();
        Check(chips.Length==3,"Role indicators missing");
        Check(chips[0][1]==1 && chips[1][1]==1 && chips[2][1]==0,"Split role indicators incorrect");
        Check(Tree(playback.Children[0]).OfType<Border>().Where(b=>b.Tag is int[]).All(b=>!b.Focusable && AutomationProperties.GetName(b).Length>0),"Role state accessibility missing");
        audio.Data.Defaults[0][2]="speaker0"; Call(form,"RefreshDevices");
        WaitFor(()=>heroName.Text=="Work","Matching profile name not shown: "+heroName.Text);
        Check(trayProfiles[0].Checked,"Tray active profile is not checked");
        Check(AutomationProperties.GetName((Button)strip.Children[0]).Contains("active"),"Active tile not marked");
        audio.Data.Defaults[0]=new[] { "speaker2","speaker2","speaker2" }; Call(form,"RefreshDevices");
        WaitFor(()=>heroName.Text=="Custom setup","Custom setup did not clear active profile");
        Check(strip.Children.Count==3 && !AutomationProperties.GetName((Button)strip.Children[0]).Contains("active"),"Inactive saved profile still looks selected");
        Check(!trayProfiles[0].Checked,"Tray checkmark retained for a custom setup");
        Check(Field(form,"lastRenderError")==null,"Redraw failed: "+Field(form,"lastRenderError"));
        audio.Data.Defaults[0]=new[] { "speaker0","speaker0","speaker1" }; Call(form,"RefreshDevices"); Pump(200);
        Check(((StackPanel)Field(form,"otherList")).Children.Count==1,"Other devices migration failed");

        // Accent: Windows accent on request, teal by default, persisted either way.
        var theme=App.GetType("AudioSwitch.Theme");
        Func<Color> current=()=>(Color)theme.GetProperty("Current").GetValue(null,null);
        Check(current()==(Color)theme.GetField("Teal").GetValue(null),"Teal is not the default accent");
        var choices=(StackPanel)Field(form,"accentChoices");
        Click((Button)choices.Children[1]); Pump(50);
        var windows=(Color?)theme.GetMethod("WindowsAccent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        if(windows.HasValue) Check(current()==(Color)theme.GetMethod("Readable",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] { windows.Value }),"Windows accent not applied");
        Check(new SettingsStore(path).Load().Accent=="Windows","Accent preference not saved");
        Click((Button)((StackPanel)Field(form,"accentChoices")).Children[0]); Pump(50);
        Check(current()==(Color)theme.GetField("Teal").GetValue(null),"Teal accent not restored");

        // Profile row: up to three share the width; more scroll horizontally with equal tiles.
        var scroller=(ScrollViewer)Field(form,"profileScroller");
        Check(scroller.ScrollableWidth<1,"Profile row scrolls with two profiles");
        var loaded=(Settings)Field(form,"settings");
        for(int i=0;i<3;i++) loaded.Profiles.Add(new Profile { Name="Extra "+i,Output="speaker1",Input="mic1" });
        Call(form,"Render"); Pump(150);
        Check(scroller.ScrollableWidth>20,"Profile row does not scroll with five profiles");
        Check(strip.Children.OfType<FrameworkElement>().Select(t=>t.Width).Distinct().Count()==1,"Scrolling tiles differ in width");
        foreach(var size in new[] { new Size(480,800),new Size(400,560),new Size(720,900) }) {
            form.Width=size.Width; form.Height=size.Height; Pump(200);
            Snapshot(form,Path.Combine(root,"layout-"+size.Width+".png"));
        }
        loaded.Profiles.RemoveRange(2,3); Call(form,"Render"); form.Width=480; form.Height=800; Pump(150);

        // Flyout lists the same profiles and closes cleanly.
        Call(form,"ToggleFlyout"); Pump(300);
        var flyout=(Window)Field(form,"flyout");
        Check(flyout.IsVisible && ((StackPanel)Field(flyout,"strip")).Children.Count==2,"Tray flyout profiles missing");
        Snapshot(flyout,Path.Combine(root,"flyout.png"));
        Call(flyout,"HideFlyout"); WaitFor(()=>!flyout.IsVisible,"Flyout did not close");

        // Settings page and About identity.
        Call(form,"ShowPage",Field(form,"settingsPage"),false); Pump(100);
        var texts=Tree((DependencyObject)Field(form,"settingsPage")).OfType<TextBlock>().Select(t=>t.Text).ToArray();
        Check(texts.Contains("Developer: Justin Miller") && texts.Any(t=>t.Contains(App.GetName().Version.ToString())),"About identity or version incorrect");
        Check(texts.Contains("Planned"),"Planned premium features not labeled");
        Snapshot(form,Path.Combine(root,"settings.png"));

        // Offline profile editing keeps the saved references.
        Call(form,"BeginEdit",loaded.Profiles[1]); Pump(150);
        Check(((Endpoint)Field(form,"editOutput")).Id=="missing" && ((Endpoint)Field(form,"editInput")).Id=="mic0","Offline editor substituted a device");
        Check(((TextBlock)Field(form,"editorMessage")).Text.Contains("Disconnected"),"Offline explanation missing");
        var nameField=(TextBox)Field(form,"profileName");
        Check(FocusManager.GetFocusedElement(form)==nameField,"Profile name does not receive focus");
        Check(((StackPanel)Field(form,"outputChoices")).Children.OfType<Button>().All(b=>AutomationProperties.GetName(b).StartsWith("Profile playback device")),"Editor device choices lack accessible names");
        Snapshot(form,Path.Combine(root,"inline-editor.png"));
        nameField.Text="Renamed offline profile"; Call(form,"SaveProfile");
        var reloaded=new SettingsStore(path).Load(); Check(reloaded.Profiles[1].Output=="missing" && reloaded.Profiles[1].Name=="Renamed offline profile","Offline rename lost reference");

        // Switching runs in the background and failures are reported.
        int ticks=0; var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
        timer.Tick+=delegate { ticks++; }; timer.Start();
        Call(form,"BuildTrayMenu"); trayProfiles=trayMenu.Items.OfType<Forms.ToolStripMenuItem>().Where(i=>i.Tag is Profile).ToArray();
        trayProfiles[0].PerformClick(); Pump(500);
        Check(!trayProfiles[0].Enabled && !((Button)strip.Children[0]).IsEnabled,"Selections were not disabled during switching");
        Check(ticks>=2 && (bool)Field(form,"busy"),"UI blocked during background switching; ticks="+ticks);
        audio.ReleaseSwitch.Set(); Pump(700); timer.Stop();
        Check(!(bool)Field(form,"busy") && ((TextBlock)Field(form,"status")).Text.Contains("Simulated failure"),"Failure recovery not reported");
        Check(audio.Writes==1,"Unexpected simulated switch count");
        Check(audio.LastApplied.Length==2 && audio.LastApplied.Any(d=>d.Id=="speaker0") && audio.LastApplied.Any(d=>d.Id=="mic0"),"Tray switch did not apply the saved pair");

        Call(form,"BeginEdit",(object)null); Pump(100);
        ((TextBox)Field(form,"profileName")).Text="Persist after exit";
        Click(((StackPanel)Field(form,"outputChoices")).Children.OfType<Button>().First());
        Click(((StackPanel)Field(form,"inputChoices")).Children.OfType<Button>().First());
        Call(form,"SaveProfile");
        form.GetType().GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
        form.Close(); Pump(100);
        Console.WriteLine("Passed: offline profiles, active-profile clearing after custom selection, role chips, accent default/Windows/restore, horizontal profile scrolling, tray flyout, settings/About, offline edits, focus, responsive switching and failure feedback. Rendered layouts in bin/ui-validation.");
        var reopened=Create(audio,path); reopened.Show(); Pump(300);
        var restored=(Settings)Field(reopened,"settings");
        Check(restored.Profiles.Count==3 && restored.Profiles.Any(p=>p.Name=="Persist after exit" && p.Output=="speaker0"),"Created profile lost after full exit/reopen");
        reopened.GetType().GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(reopened,true); reopened.Close();
        Console.WriteLine("Passed: newly created profile survives full window exit, disposal and fresh startup.");
    }

    // README screenshots: real windows with the live Windows backdrop, over a colorful stand-in wallpaper.
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
    static void Screenshots(string folder) {
        Directory.CreateDirectory(folder);
        var audio=new FakeAudio { Succeed=true };
        string[] outputs={"Studio Headphones","Desk Speakers","LG Monitor (HDMI)","Bluetooth Earbuds","Realtek Digital Output"};
        string[] inputs={"USB Microphone","Headset Microphone","Webcam Microphone"};
        for(int i=0;i<outputs.Length;i++) audio.Data.Devices.Add(new Endpoint { Id="out"+i,Flow=0,Name=outputs[i] });
        for(int i=0;i<inputs.Length;i++) audio.Data.Devices.Add(new Endpoint { Id="in"+i,Flow=1,Name=inputs[i] });
        audio.Data.Defaults[0]=new[] { "out0","out0","out0" }; audio.Data.Defaults[1]=new[] { "in0","in0","in0" };
        var settings=new Settings();
        settings.Profiles.Add(new Profile { Name="Gaming",Output="out0",Input="in0",Icon="game" });
        settings.Profiles.Add(new Profile { Name="Work",Output="out1",Input="in2",Icon="work" });
        settings.Profiles.Add(new Profile { Name="Music",Output="out1",Input="in0",Icon="music" });
        settings.Profiles.Add(new Profile { Name="Movies",Output="out2",Input="in2",Icon="movie" });
        settings.Profiles.Add(new Profile { Name="Calls",Output="out3",Input="in1",Icon="call" });
        settings.Favorites.Add("out0"); settings.Favorites.Add("in0"); settings.Deemphasized.Add("out4");
        string path=Path.Combine(Path.GetTempPath(),"AudioMe-screens-"+Guid.NewGuid().ToString("N")+".xml"); new SettingsStore(path).Save(settings);

        var wallpaper=new Window { WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=0,Top=0,Width=SystemParameters.PrimaryScreenWidth,Height=SystemParameters.PrimaryScreenHeight,Topmost=true,ShowInTaskbar=false };
        var canvas=new Grid { Background=new LinearGradientBrush(Color.FromRgb(0x18,0x1B,0x42),Color.FromRgb(0x0C,0x10,0x20),45) };
        Action<double,double,double,Color> blob=(x,y,size,color)=>canvas.Children.Add(new Ellipse { Width=size,Height=size,Fill=new SolidColorBrush(color),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(x,y,0,0),Effect=new BlurEffect { Radius=120 } });
        blob(-80,-60,620,Color.FromRgb(0x4A,0x34,0xB0)); blob(520,260,520,Color.FromRgb(0x0E,0x9C,0x9A)); blob(160,600,560,Color.FromRgb(0xC2,0x3D,0x8E)); blob(900,-40,460,Color.FromRgb(0xF0,0x9A,0x3E)); blob(1000,560,480,Color.FromRgb(0x2B,0x5C,0xD8));
        wallpaper.Content=canvas; wallpaper.Show(); wallpaper.Activate(); Pump(400);

        var form=Create(audio,path); form.Topmost=true; form.Width=480; form.Height=840; form.Left=120; form.Top=60;
        form.Show(); form.Activate(); Pump(1200);
        Capture(form,Path.Combine(folder,"main.png"));
        Call(form,"ShowPage",Field(form,"settingsPage"),false); form.Activate(); Pump(600);
        Capture(form,Path.Combine(folder,"settings.png"));
        Call(form,"BeginEdit",((Settings)Field(form,"settings")).Profiles[0]); form.Activate(); Pump(600);
        Capture(form,Path.Combine(folder,"editor.png"));
        Call(form,"ShowPage",Field(form,"home"),false); Pump(100);
        Call(form,"ToggleFlyout"); Pump(400);
        var flyout=(Window)Field(form,"flyout"); flyout.Left=700; flyout.Top=300; flyout.Activate(); Pump(900);
        Capture(flyout,Path.Combine(folder,"flyout.png"));
        form.GetType().GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true); form.Close(); wallpaper.Close();
        File.Delete(path); if(File.Exists(path+".bak")) File.Delete(path+".bak");
        Console.WriteLine("Screenshots written to "+Path.GetFullPath(folder));
    }
    // Captures the window as composited on screen, with a margin of the stand-in wallpaper around it.
    static void Capture(Window window,string file) {
        // Park the pointer on the stand-in wallpaper so no hover state or tooltip is captured.
        Forms.Cursor.Position=new Drawing.Point(Forms.Screen.PrimaryScreen.Bounds.Right-4,Forms.Screen.PrimaryScreen.Bounds.Bottom/2);
        foreach(Window open in Application.Current.Windows) open.UpdateLayout();
        Pump(500);
        RECT r; DwmGetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(window).Handle,9,out r,Marshal.SizeOf(typeof(RECT)));
        int pad=36, x=r.Left-pad, y=r.Top-pad, w=r.Right-r.Left+pad*2, h=r.Bottom-r.Top+pad*2;
        using(var bitmap=new Drawing.Bitmap(w,h))
        using(var g=Drawing.Graphics.FromImage(bitmap)) { g.CopyFromScreen(x,y,0,0,new Drawing.Size(w,h)); bitmap.Save(file,Drawing.Imaging.ImageFormat.Png); }
    }
}
