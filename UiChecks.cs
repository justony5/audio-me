using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using AudioSwitch;
class FakeAudio : IAudioService {
    public AudioSnapshot Data=new AudioSnapshot(); public int Writes;
    public ManualResetEvent ReleaseSwitch=new ManualResetEvent(false);
    public AudioSnapshot Read() { return Data; }
    public void Apply(Endpoint[] devices) { Interlocked.Increment(ref Writes); ReleaseSwitch.WaitOne(15000); throw new InvalidOperationException("Simulated failure; no hardware was changed."); }
}
class UiChecks {
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    static object Field(object instance,string name) { return instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance); }
    static void Call(object instance,string name,params object[] args) { instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,args); }
    static void Pump(int milliseconds) { var sw=Stopwatch.StartNew(); while(sw.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); Thread.Sleep(5); } }
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void WaitFor(Func<bool> condition,string message) {
        var elapsed=Stopwatch.StartNew(); while(!condition() && elapsed.ElapsedMilliseconds<4000) Pump(25);
        Check(condition(),message);
    }
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root=Path.GetFullPath(Path.Combine("bin","ui-validation")); Directory.CreateDirectory(root);
        var audio=new FakeAudio();
        for(int i=0;i<8;i++) audio.Data.Devices.Add(new Endpoint {Id="speaker"+i,Flow=0,Name="Test playback device "+i});
        for(int i=0;i<4;i++) audio.Data.Devices.Add(new Endpoint {Id="mic"+i,Flow=1,Name="Test microphone "+i});
        audio.Data.Defaults[0]=new[] {"speaker0","speaker0","speaker1"}; audio.Data.Defaults[1]=new[] {"mic0","mic0","mic0"};
        var settings=new Settings(); settings.Profiles.Add(new Profile {Name="Work",Output="speaker0",Input="mic0"});
        settings.Profiles.Add(new Profile {Name="Disconnected headset",Output="missing",Input="mic0",OutputName="USB headset"}); settings.Deemphasized.Add("speaker7");
        string path=Path.Combine(root,"settings.xml"); new SettingsStore(path).Save(settings);
        var type=typeof(Settings).Assembly.GetType("AudioSwitch.Widget");
        using(var form=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] {false,audio,new SettingsStore(path)},null)) {
            form.Text="Audio Me · simulated UI validation"; form.Show(); Pump(300);
            var profiles=(FlowLayoutPanel)Field(form,"profileList"); Check(profiles.Controls.Count==2,"Disconnected profiles hidden");
            Check(((Label)Field(form,"profileState")).Text.Contains("Custom"),"Split role setup marked active");
            var activeName=(TextBox)Field(form,"activeProfileName");
            Check(activeName.Text=="","Custom setup retained an active profile name");
            var playback=(FlowLayoutPanel)Field(form,"outputList");
            var row=(TableLayoutPanel)playback.Controls[0];
            var roles=row.Controls.OfType<RoleIndicator>().OrderBy(i=>i.Role).ToArray();
            Check(row.RowCount==1 && roles.Length==3,"Role indicators are not a single compact row");
            Check(roles[0].IsActive && roles[1].IsActive && !roles[2].IsActive,"Split role indicators incorrect");
            Check(roles.All(i=>!i.TabStop && i.AccessibleName.Length>0),"Role state accessibility missing");
            audio.Data.Defaults[0][2]="speaker0"; Call(form,"RefreshDevices");
            WaitFor(()=>activeName.Text=="Work","Matching profile name not shown: "+activeName.Text);
            audio.Data.Defaults[0]=new[] {"speaker2","speaker2","speaker2"}; Call(form,"RefreshDevices");
            WaitFor(()=>activeName.Text=="","Custom setup did not clear active profile");
            WaitFor(()=>activeName.Text=="" && profiles.Controls.Count==2,"Manual switch retained stale profile name or deleted saved profiles: active="+activeName.Text+", rows="+profiles.Controls.Count+", status="+((Label)Field(form,"status")).Text);
            Check(((TableLayoutPanel)profiles.Controls[0]).Controls.OfType<Button>().First().Text=="Use Work","Inactive saved profile still looks selected");
            Check(Field(form,"lastRenderError")==null,"Redraw failed: "+Field(form,"lastRenderError"));
            audio.Data.Defaults[0]=new[] {"speaker0","speaker0","speaker1"}; Call(form,"RefreshDevices"); Pump(200);
            int dark; int result=DwmGetWindowAttribute(form.Handle,20,out dark,4);
            var theme=typeof(Settings).Assembly.GetType("AudioSwitch.WindowTheme");
            bool expectedDark=(bool)theme.GetMethod("IsDark").Invoke(null,null);
            if(result==0) Check(dark==(expectedDark?1:0),"Title bar does not match current Windows app theme");
            Check(((FlowLayoutPanel)Field(form,"otherList")).Controls.Count==1,"Other devices migration failed");
            foreach(var size in new[] {new Size(480,660),new Size(440,480),new Size(720,900)}) {
                form.ClientSize=size; Pump(100);
                using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"layout-"+size.Width+".png")); }
            }
            // Simulate larger logical UI scales without changing system settings.
            form.ClientSize=new Size(480,660);
            var loadedSettings=(Settings)Field(form,"settings");
            Call(form,"BeginEdit",loadedSettings.Profiles[1]); Pump(100);
            var editorPanel=(TableLayoutPanel)Field(form,"editor"); var contentPanel=(FlowLayoutPanel)Field(form,"content");
            Check(editorPanel.Width<=contentPanel.ClientSize.Width-contentPanel.Padding.Horizontal,"Inline editor overflows horizontally");
            using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"inline-editor.png")); }
            editorPanel.Visible=false;
            form.Scale(new SizeF(1.5f,1.5f)); Call(form,"Render"); Pump(100);
            using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"scale-150.png")); }
            form.Scale(new SizeF(4f/3f,4f/3f)); Call(form,"Render"); Pump(100);
            using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"scale-200.png")); }
            Call(form,"BeginEdit",loadedSettings.Profiles[1]);
            var input=(ComboBox)Field(form,"profileInput"); var output=(ComboBox)Field(form,"profileOutput");
            Check(((Endpoint)output.SelectedItem).Id=="missing" && ((Endpoint)input.SelectedItem).Id=="mic0","Offline editor substituted a device");
            Check(input.AccessibleName=="Profile microphone" && output.TabStop,"Editor keyboard access missing");
            var nameField=(TextBox)Field(form,"profileName"); nameField.Focus();
            form.SelectNextControl(nameField,true,true,true,true); Pump(30);
            Check(output.Focused,"Tab did not move from profile name to playback selector");
            ((TextBox)Field(form,"profileName")).Text="Renamed offline profile"; Call(form,"SaveProfile");
            var reloaded=new SettingsStore(path).Load(); Check(reloaded.Profiles[1].Output=="missing" && reloaded.Profiles[1].Name=="Renamed offline profile","Offline rename lost reference");
            int ticks=0; using(var timer=new System.Windows.Forms.Timer {Interval=20}) {
                timer.Tick+=delegate {ticks++;}; timer.Start();
                Call(form,"ApplyDevices",(object)new[] {audio.Data.Devices[0]}); Pump(500);
                Check(ticks>=2 && (bool)Field(form,"busy"),"UI blocked during background switching; ticks="+ticks+", busy="+Field(form,"busy")); audio.ReleaseSwitch.Set(); Pump(700);
                Check(!(bool)Field(form,"busy") && ((Label)Field(form,"status")).Text.Contains("Simulated failure"),"Failure recovery not reported");
            }
            Check(audio.Writes==1,"Unexpected simulated switch count");
            Console.WriteLine("Passed: offline profiles, active-profile clearing after custom selection, compact/split role icons, native title-bar preference, inline edits, keyboard focus, responsive switching and failure feedback. Rendered three sizes and simulated 150%/200% control scaling in bin/ui-validation.");
        }
    }
}
