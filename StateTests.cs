using System;
using System.IO;
using System.Linq;
using AudioSwitch;
class StateTests {
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void Main() {
        string root=Path.Combine(Path.GetTempPath(),"AudioMe-tests-"+Guid.NewGuid()); Directory.CreateDirectory(root);
        try {
            var path=Path.Combine(root,"settings.xml");
            string legacy="<Settings><Profiles><Profile><Name>FPS Gaming</Name><Output>speaker</Output><Input>mic</Input></Profile></Profiles><Pinned>true</Pinned><Favorites><string>speaker</string></Favorites><Deemphasized><string>other</string></Deemphasized><WindowWidth>700</WindowWidth><WindowHeight>800</WindowHeight><FutureSetting>keep</FutureSetting></Settings>";
            File.WriteAllText(path,legacy); var store=new SettingsStore(path); var settings=store.Load();
            Check(!store.RecoveryRequired && settings.Profiles.Count==1,"Legacy profiles failed to migrate");
            Check(settings.Profiles[0].Output=="speaker" && settings.Profiles[0].Input=="mic" && settings.Pinned,"References or pin lost");
            Check(settings.Favorites.Contains("speaker") && settings.Deemphasized.Contains("other") && settings.WindowWidth==700,"Preferences lost");
            store.Save(settings); Check(File.ReadAllText(path).Contains("FutureSetting"),"Unknown settings lost");
            var snapshot=new AudioSnapshot(); var p=settings.Profiles[0];
            Check(snapshot.Missing(p).Contains("Playback missing") && snapshot.Missing(p).Contains("Microphone missing"),"Missing reasons incomplete");
            snapshot.Devices.Add(new Endpoint { Id="speaker",Name="Speakers",Flow=0 });
            Check(!snapshot.Missing(p).Contains("Playback missing") && snapshot.Missing(p).Contains("Microphone missing"),"One missing device wrong");
            snapshot.Devices.Add(new Endpoint { Id="mic",Name="Microphone",Flow=1 });
            snapshot.Defaults[0]=new[] {"speaker","speaker","speaker"}; snapshot.Defaults[1]=new[] {"mic","mic","mic"};
            Check(snapshot.IsActive(p),"Profile not active with six matching defaults"); snapshot.Defaults[1][2]="other";
            Check(!snapshot.IsActive(p),"Split communications default incorrectly shown active");
            snapshot.Devices.RemoveAt(1); Check(p.Input=="mic" && snapshot.Missing(p).Length>0,"Disconnect changed saved reference");
            snapshot.Devices.Add(new Endpoint { Id="mic",Name="Microphone",Flow=1 }); Check(snapshot.Missing(p)=="","Reconnect failed");
            File.WriteAllText(path,"<Settings>broken"); var damaged=File.ReadAllBytes(path); store=new SettingsStore(path); settings=store.Load();
            Check(store.RecoveryRequired,"Corruption not detected"); bool blocked=false;
            try { store.Save(settings); } catch(InvalidOperationException) { blocked=true; }
            Check(blocked && damaged.SequenceEqual(File.ReadAllBytes(path)),"Corrupt original overwritten");
            store.BackupAndReset(); Check(damaged.SequenceEqual(File.ReadAllBytes(store.BackupPath)),"Recovery backup differs");
            store.Save(settings); Check(!new SettingsStore(path).Load().Profiles.Any(),"Reset did not save valid settings");
            File.WriteAllText(path,"<Settings><Profiles><Profile><Name>Bad</Name></Profile></Profiles></Settings>");
            store=new SettingsStore(path); store.Load(); Check(store.RecoveryRequired,"Missing device references accepted");
            File.WriteAllText(path,legacy); store=new SettingsStore(path); settings=store.Load(); store.Save(settings);
            Check(File.Exists(path+".bak") && File.ReadAllText(path+".bak")==legacy,"Last valid backup not retained");
            Console.WriteLine("Passed: legacy migration, profile availability and six-role matching, reconnect, corrupt/invalid settings protection, explicit recovery and backups.");
        } finally { Directory.Delete(root,true); }
    }
}
