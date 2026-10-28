using System;
using System.Collections.Generic;
using System.Reflection;
using AudioSwitch;
class ConnectionTests {
    static void Main() {
        var method = typeof(Profile).Assembly.GetType("AudioSwitch.Widget").GetMethod("Pair",BindingFlags.Static|BindingFlags.NonPublic);
        var p = new Profile { Name = "FPS Gaming", Output = "speaker", Input = "mic" };
        var speaker = new Endpoint { Id = "speaker", Flow = 0 };
        var mic = new Endpoint { Id = "mic", Flow = 1 };
        Action<int,Endpoint[]> check = (expected,items) => {
            var pair = (Endpoint[])method.Invoke(null,new object[] { p,new List<Endpoint>(items) });
            if (pair.Length != expected) throw new Exception("Connection filter failed");
        };
        check(2,new[] { speaker,mic });
        check(1,new[] { speaker });
        check(1,new[] { mic });
        check(0,new Endpoint[0]);
        check(1,new[] { speaker,new Endpoint { Id = "mic", Flow = 0 } });
        check(2,new[] { speaker,mic }); // Reconnect makes the saved pair available again.
        var preferences = new Settings();
        var a = new Endpoint { Id = "a", Name = "Alpha" };
        var b = new Endpoint { Id = "b", Name = "bravo" };
        var c = new Endpoint { Id = "c", Name = "Charlie" };
        preferences.ToggleDevice("c",true); preferences.ToggleDevice("b",true);
        Action<string> order = expected => {
            var actual = String.Join("",System.Linq.Enumerable.Select(preferences.SortDevices(new[] { c,a,b }),d => d.Id));
            if (actual != expected) throw new Exception("Preference sorting failed: " + actual);
        };
        order("bca"); // Favorites alphabetically, ahead of unstarred Alpha.
        preferences.ToggleDevice("b",false); order("cab");
        if (preferences.Favorites.Contains("b")) throw new Exception("Conflicting preferences");
        preferences.ToggleDevice("b",false); order("cab"); // Restore to normal tier.
        preferences.ToggleDevice("c",true); order("abc");
        preferences.ToggleDevice("a",false); preferences.ToggleDevice("b",true);
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Settings));
        using (var text = new System.IO.StringWriter()) {
            serializer.Serialize(text,preferences);
            using (var reader = new System.IO.StringReader(text.ToString())) {
                var restored = (Settings)serializer.Deserialize(reader);
                if (restored.DeviceRank("a") != 2 || restored.DeviceRank("b") != 0) throw new Exception("Preferences were not persisted");
            }
        }
        using (var reader = new System.IO.StringReader("<Settings><Profiles/><Pinned>false</Pinned></Settings>")) {
            var legacy = (Settings)serializer.Deserialize(reader);
            if (legacy.DeviceRank("a") != 1) throw new Exception("Old settings incompatible");
        }
        using (var audio = new Audio()) {
            bool rejected = false;
            try { audio.Apply(new Endpoint { Id = "missing-test-endpoint", Name = "Disconnected test", Flow = 0 }); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Disconnected endpoint was not rejected");
        }
        Console.WriteLine("Passed: connected pair, each missing device, both missing, wrong direction, reconnect, and disconnected switch rejection.");
        Console.WriteLine("Passed: alphabetical favorite tiers, demotion, restore, mutual exclusion, persistence and old-settings compatibility.");
    }
}
