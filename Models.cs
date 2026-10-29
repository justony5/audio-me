using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace AudioSwitch {
    public class Profile {
        public string Name; public string Output; public string Input;
        public string OutputName; public string InputName;
        [XmlAnyElement] public XmlElement[] Extra;
        public override string ToString() { return Name; }
    }
    public class DeviceAlias { public string Id; public string Name; }
    public class Settings {
        public List<Profile> Profiles = new List<Profile>(); public bool Pinned;
        public List<string> Favorites = new List<string>();
        // Preserve the original XML field so older preferences migrate without data loss.
        public List<string> Deemphasized = new List<string>();
        public List<DeviceAlias> Aliases = new List<DeviceAlias>();
        public int WindowWidth; public int WindowHeight;
        [XmlAnyElement] public XmlElement[] Extra;
        public int DeviceRank(string id) { return Deemphasized.Contains(id) ? 2 : Favorites.Contains(id) ? 0 : 1; }
        public void ToggleDevice(string id, bool favorite) {
            var target = favorite ? Favorites : Deemphasized;
            var other = favorite ? Deemphasized : Favorites;
            if (target.Contains(id)) target.Remove(id); else { other.Remove(id); target.Add(id); }
        }
        public string DisplayName(Endpoint d) {
            var alias = Aliases.FirstOrDefault(a => a.Id == d.Id);
            return alias == null || String.IsNullOrWhiteSpace(alias.Name) ? d.Name : alias.Name;
        }
        public IEnumerable<Endpoint> SortDevices(IEnumerable<Endpoint> devices) {
            return devices.OrderBy(d => DeviceRank(d.Id)).ThenBy(d => DisplayName(d),StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id,StringComparer.Ordinal);
        }
    }
    public sealed class SettingsStore {
        public readonly string Path;
        public bool RecoveryRequired { get; private set; }
        public string RecoveryMessage { get; private set; }
        public string BackupPath { get; private set; }
        public SettingsStore(string path) { Path = path; }
        public Settings Load() {
            try {
                if (!File.Exists(Path)) return new Settings();
                using (var stream = File.OpenRead(Path)) {
                    var value = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
                    if (value == null || value.Profiles == null || value.Favorites == null || value.Deemphasized == null || value.Aliases == null ||
                        value.Profiles.Any(p => p == null || String.IsNullOrWhiteSpace(p.Name) || String.IsNullOrWhiteSpace(p.Output) || String.IsNullOrWhiteSpace(p.Input)) ||
                        value.Aliases.Any(a => a == null || String.IsNullOrWhiteSpace(a.Id))) throw new InvalidDataException("Invalid settings entries.");
                    return value;
                }
            } catch (Exception e) {
                RecoveryRequired = true;
                RecoveryMessage = "Settings could not be loaded. The original file is untouched. Saving is paused. " + e.GetBaseException().Message;
                return new Settings();
            }
        }
        // Explicit recovery must succeed before any replacement is permitted.
        public void BackupAndReset() {
            if (!RecoveryRequired) return;
            BackupPath = Path + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
            File.Copy(Path,BackupPath,false);
            RecoveryRequired = false;
        }
        public void Save(Settings value) {
            if (RecoveryRequired) throw new InvalidOperationException("Saving is paused to protect unreadable settings. Use the recovery action first.");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
            var temporary = Path + "."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    new XmlSerializer(typeof(Settings)).Serialize(stream,value); stream.Flush(true);
                }
                using(var stream=File.OpenRead(temporary)) {
                    var verified=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
                    if(verified.Profiles.Count!=value.Profiles.Count) throw new IOException("Profile persistence verification failed.");
                }
                if (File.Exists(Path)) File.Replace(temporary,Path,Path+".bak"); else File.Move(temporary,Path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public sealed class AudioSnapshot {
        public List<Endpoint> Devices = new List<Endpoint>();
        public string[][] Defaults = { new string[3], new string[3] };
        public Endpoint[] Pair(Profile p) { return Devices.Where(d => (d.Flow == 0 && d.Id == p.Output) || (d.Flow == 1 && d.Id == p.Input)).ToArray(); }
        public string Missing(Profile p) {
            var missing = new List<string>();
            if (!Devices.Any(d => d.Flow == 0 && d.Id == p.Output)) missing.Add("Playback missing: " + (p.OutputName ?? p.Output));
            if (!Devices.Any(d => d.Flow == 1 && d.Id == p.Input)) missing.Add("Microphone missing: " + (p.InputName ?? p.Input));
            return String.Join("; ",missing.ToArray());
        }
        public bool IsActive(Profile p) { return Missing(p).Length == 0 && Defaults[0].All(id => id == p.Output) && Defaults[1].All(id => id == p.Input); }
        public string Roles(Endpoint d) {
            string[] names = { "System", "Media", "Calls" };
            return String.Join(" / ",Enumerable.Range(0,3).Where(r => Defaults[d.Flow][r] == d.Id).Select(r => names[r]).ToArray());
        }
    }
    // Manual UI and future premium triggers share this boundary. No premium automation is active.
    public interface IAudioService {
        AudioSnapshot Read();
        void Apply(Endpoint[] devices);
    }
    public sealed class WindowsAudioService : IAudioService {
        public AudioSnapshot Read() {
            using (var audio = new Audio()) {
                var snapshot = new AudioSnapshot { Devices = audio.List(0).Concat(audio.List(1)).ToList() };
                for (int f=0;f<2;f++) for (int r=0;r<3;r++) snapshot.Defaults[f][r]=audio.Default(f,r);
                return snapshot;
            }
        }
        public void Apply(Endpoint[] devices) { using (var audio = new Audio()) audio.Apply(devices); }
    }
}
