using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace AudioSwitch {
    static class Program {
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
        [STAThread] static void Main(string[] args) {
            if (args.Length == 2 && args[0] == "--diagnose") {
                try {
                    using (var audio = new Audio()) {
                        var lines = new List<string>();
                        for (int f = 0; f < 2; f++) {
                            lines.Add(f == 0 ? "PLAYBACK" : "RECORDING");
                            foreach (var d in audio.List(f)) lines.Add(d.Name + " | " + audio.Roles(d) + " | " + d.Id);
                        }
                        File.WriteAllLines(args[1],lines);
                    }
                } catch (Exception e) { File.WriteAllText(args[1],e.ToString()); Environment.ExitCode = 1; }
                return;
            }
            bool created;
            using (var activation = new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\AudioSwitch.ShowWidget"))
            using (var mutex = new Mutex(true,"Local\\AudioSwitch.Widget",out created)) {
                if (!created) {
                    // The taskbar starts a second process. Give the existing process foreground
                    // permission, then signal it even if its window is hidden or still starting.
                    foreach (var name in new[] { "AudioMe", "AudioSwitch" }) foreach (var process in Process.GetProcessesByName(name)) {
                        using (process) {
                            try {
                                if (process.Id != Process.GetCurrentProcess().Id && process.SessionId == Process.GetCurrentProcess().SessionId &&
                                    Path.GetDirectoryName(process.MainModule.FileName) == AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
                                    AllowSetForegroundWindow((uint)process.Id);
                            } catch { }
                        }
                    }
                    activation.Set(); return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var widget = new Widget(args.Contains("--tray")))
                using (var activationTimer = new System.Windows.Forms.Timer { Interval = 150 }) {
                    activationTimer.Tick += delegate { if (activation.WaitOne(0)) widget.ShowWidget(); };
                    activationTimer.Start(); Application.Run(widget);
                }
            }
        }
    }
}
