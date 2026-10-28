using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using AudioSwitch;
class FakeAudio : IAudioService {
    public AudioSnapshot Data=new AudioSnapshot(); public int Writes;
    public ManualResetEvent ReleaseSwitch=new ManualResetEvent(false);
    public AudioSnapshot Read() { return Data; }
    public void Apply(Endpoint[] devices) { Interlocked.Increment(ref Writes); ReleaseSwitch.WaitOne(15000); throw new InvalidOperationException("Simulated failure; no hardware was changed."); }
}
class UiChecks {
    static object Field(object instance,string name) { return instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance); }
    static void Call(object instance,string name,params object[] args) { instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance,args); }
    static void Pump(int milliseconds) { var sw=Stopwatch.StartNew(); while(sw.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); Thread.Sleep(5); } }
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
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
                Call(form,"ApplyDevices",(object)new[] {audio.Data.Devices[0]}); Pump(180);
                Check(ticks>=2 && (bool)Field(form,"busy"),"UI blocked during background switching; ticks="+ticks+", busy="+Field(form,"busy")); audio.ReleaseSwitch.Set(); Pump(700);
                Check(!(bool)Field(form,"busy") && ((Label)Field(form,"status")).Text.Contains("Simulated failure"),"Failure recovery not reported");
            }
            Check(audio.Writes==1,"Unexpected simulated switch count");
            Console.WriteLine("Passed: visible offline profiles, inline rename/reference preservation, role status, accessibility labels, responsive simulated switching and failure feedback. Rendered three window sizes and simulated 150%/200% scales in bin/ui-validation.");
        }
    }
}
