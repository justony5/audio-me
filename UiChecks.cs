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
    public AudioSnapshot Data=new AudioSnapshot(); public int Writes; public Endpoint[] LastApplied;
    public ManualResetEvent ReleaseSwitch=new ManualResetEvent(false);
    public AudioSnapshot Read() { return Data; }
    public void Apply(Endpoint[] devices) { LastApplied=devices; Interlocked.Increment(ref Writes); ReleaseSwitch.WaitOne(15000); throw new InvalidOperationException("Simulated failure; no hardware was changed."); }
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
            var mainMenu=(MenuStrip)Field(form,"mainMenu");
            Check(mainMenu.Items.Count==3 && mainMenu.Items[2].Text=="&Help","Top menus missing");
            var preferences=(ToolStripMenuItem)mainMenu.Items[1];
            Check(preferences.DropDownItems.Cast<ToolStripItem>().All(i=>!i.Enabled && i.Text.Contains("Planned")),"Placeholder looks implemented");
            using(var about=(Form)form.GetType().GetMethod("CreateAboutDialog",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null)) {
                var labels=about.Controls[0].Controls.OfType<Label>().Select(l=>l.Text).ToArray();
                Check(labels.Any(t=>t=="Developer: Justin Miller") && labels.Any(t=>t.Contains(typeof(Settings).Assembly.GetName().Version.ToString())),"About identity or version incorrect");
                about.Show(form); Pump(50);
                using(var bitmap=new Bitmap(about.Width,about.Height)) { about.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(root,"about.png")); }
                about.Close();
            }
            var profiles=(FlowLayoutPanel)Field(form,"profileList"); Check(profiles.Controls.Count==2,"Disconnected profiles hidden");
            Check(((Label)Field(form,"profileState")).Text.Contains("Custom"),"Split role setup marked active");
            var activeName=(TextBox)Field(form,"activeProfileName");
            Check(activeName.Text=="","Custom setup retained an active profile name");
            Call(form,"BuildTrayMenu");
            var trayMenu=(ContextMenuStrip)Field(form,"trayMenu");
            var trayProfiles=trayMenu.Items.OfType<ToolStripMenuItem>().Where(i=>i.Tag is Profile).ToArray();
            Check(trayProfiles.Length==2 && trayProfiles[0].Enabled && !trayProfiles[1].Enabled,"Tray profile availability incorrect");
            Check(trayProfiles[1].ToolTipText.Contains("Playback missing"),"Tray profile lacks missing-device reason");
            var playback=(FlowLayoutPanel)Field(form,"outputList");
            var row=(TableLayoutPanel)playback.Controls[0];
            var roles=row.Controls.OfType<RoleIndicator>().OrderBy(i=>i.Role).ToArray();
            Check(row.RowCount==1 && roles.Length==3,"Role indicators are not a single compact row");
            Check(roles[0].IsActive && roles[1].IsActive && !roles[2].IsActive,"Split role indicators incorrect");
            Check(roles.All(i=>!i.TabStop && i.AccessibleName.Length>0),"Role state accessibility missing");
            audio.Data.Defaults[0][2]="speaker0"; Call(form,"RefreshDevices");
            WaitFor(()=>activeName.Text=="Work","Matching profile name not shown: "+activeName.Text);
            Check(trayProfiles[0].Checked,"Tray active profile is not checked");
            audio.Data.Defaults[0]=new[] {"speaker2","speaker2","speaker2"}; Call(form,"RefreshDevices");
            WaitFor(()=>activeName.Text=="","Custom setup did not clear active profile");
            WaitFor(()=>activeName.Text=="" && profiles.Controls.Count==2,"Manual switch retained stale profile name or deleted saved profiles: active="+activeName.Text+", rows="+profiles.Controls.Count+", status="+((Label)Field(form,"status")).Text);
            Check(((TableLayoutPanel)profiles.Controls[0]).Controls.OfType<Button>().First().Text=="Use Work","Inactive saved profile still looks selected");
            Check(!trayProfiles[0].Checked,"Tray checkmark retained for a custom setup");
            Check(Field(form,"lastRenderError")==null,"Redraw failed: "+Field(form,"lastRenderError"));
            audio.Data.Defaults[0]=new[] {"speaker0","speaker0","speaker1"}; Call(form,"RefreshDevices"); Pump(200);
            int dark; int result=DwmGetWindowAttribute(form.Handle,20,out dark,4);
            var theme=typeof(Settings).Assembly.GetType("AudioSwitch.WindowTheme");
            bool expectedDark=(bool)theme.GetMethod("IsDark").Invoke(null,null);
            if(result==0) Check(dark==(expectedDark?1:0),"Title bar does not match current Windows app theme");
            Check(((FlowLayoutPanel)Field(form,"otherList")).Controls.Count==1,"Other devices migration failed");
            foreach(var size in new[] {new Size(480,660),new Size(440,480),new Size(720,900)}) {
                form.ClientSize=size; Pump(100);
                var bottomPanel=(TableLayoutPanel)Field(form,"bottom");
                Check(bottomPanel.Bottom==form.ClientSize.Height,"Disclaimer is not anchored to bottom");
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
                trayProfiles[0].PerformClick(); Pump(500);
                Check(!trayProfiles[0].Enabled,"Tray selection was not disabled during switching");
                Check(ticks>=2 && (bool)Field(form,"busy"),"UI blocked during background switching; ticks="+ticks+", busy="+Field(form,"busy")); audio.ReleaseSwitch.Set(); Pump(700);
                Check(!(bool)Field(form,"busy") && ((Label)Field(form,"status")).Text.Contains("Simulated failure"),"Failure recovery not reported");
            }
            Check(audio.Writes==1,"Unexpected simulated switch count");
            Check(audio.LastApplied.Length==2 && audio.LastApplied.Any(d=>d.Id=="speaker0") && audio.LastApplied.Any(d=>d.Id=="mic0"),"Tray switch did not apply the saved pair");
            Call(form,"BeginEdit",(object)null);
            ((TextBox)Field(form,"profileName")).Text="Persist after exit";
            ((ComboBox)Field(form,"profileOutput")).SelectedIndex=1;
            ((ComboBox)Field(form,"profileInput")).SelectedIndex=1;
            Call(form,"SaveProfile");
            form.GetType().GetField("exiting",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
            form.Close();
            Console.WriteLine("Passed: offline profiles, active-profile clearing after custom selection, compact/split role icons, native title-bar preference, inline edits, keyboard focus, responsive switching and failure feedback. Rendered three sizes and simulated 150%/200% control scaling in bin/ui-validation.");
        }
        using(var reopened=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] {false,audio,new SettingsStore(path)},null)) {
            reopened.Show(); Pump(300);
            var restored=(Settings)Field(reopened,"settings");
            Check(restored.Profiles.Count==3 && restored.Profiles.Any(p=>p.Name=="Persist after exit"),"Created profile lost after full exit/reopen");
            Console.WriteLine("Passed: newly created profile survives full form exit, disposal and fresh startup.");
        }
    }
}
