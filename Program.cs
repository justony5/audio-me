using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Collections.Generic;

namespace AudioSwitch {
    public class Profile { public string Name; public string Output; public string Input; public override string ToString() { return Name; } }
    public class Settings {
        public List<Profile> Profiles = new List<Profile>(); public bool Pinned;
        public List<string> Favorites = new List<string>();
        public List<string> Deemphasized = new List<string>();
        public int DeviceRank(string id) { return Deemphasized.Contains(id) ? 2 : Favorites.Contains(id) ? 0 : 1; }
        public void ToggleDevice(string id, bool favorite) {
            var target = favorite ? Favorites : Deemphasized;
            var other = favorite ? Deemphasized : Favorites;
            if (target.Contains(id)) target.Remove(id);
            else { other.Remove(id); target.Add(id); }
        }
        public IEnumerable<Endpoint> SortDevices(IEnumerable<Endpoint> devices) {
            return devices.OrderBy(d => DeviceRank(d.Id)).ThenBy(d => d.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id,StringComparer.Ordinal);
        }
    }
    static class Program {
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
            using (var mutex = new Mutex(true,"Local\\AudioSwitch.Widget",out created)) {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Widget(args.Contains("--tray")));
            }
        }
    }
    class Widget : Form {
        readonly Audio audio = new Audio();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly FlowLayoutPanel outputs = new FlowLayoutPanel();
        readonly FlowLayoutPanel inputs = new FlowLayoutPanel();
        readonly ComboBox groups = new ComboBox();
        readonly Label status = new Label();
        readonly ToolTip tips = new ToolTip();
        readonly Icon brandIcon;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AudioSwitch","settings.xml");
        Settings settings = new Settings();
        bool exiting, refreshing, startHidden;
        string fingerprint = "";
        public Widget(bool hidden) {
            startHidden = hidden;
            AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Audio Me"; ClientSize = new Size(440,540); MinimumSize = new Size(456,579);
            using (var resource = typeof(Program).Assembly.GetManifestResourceStream("AudioMe.ico")) brandIcon = new Icon(resource);
            Icon = brandIcon;
            Font = new Font("Segoe UI",10.5f); BackColor = Color.FromArgb(30,30,32); ForeColor = Color.WhiteSmoke;
            StartPosition = FormStartPosition.CenterScreen;
            try { if (File.Exists(settingsFile)) using (var stream = File.OpenRead(settingsFile)) settings = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream); }
            catch { settings = new Settings(); }
            TopMost = settings.Pinned;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 9 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            layout.Controls.Add(new Label { Text = "Audio Me", Font = new Font(Font,FontStyle.Bold), AutoSize = true },0,0);
            layout.Controls.Add(Heading("SPEAKERS / HEADPHONES"),0,1); layout.Controls.Add(outputs,0,2);
            layout.Controls.Add(Heading("MICROPHONE / RECORDING"),0,3); layout.Controls.Add(inputs,0,4);
            layout.Controls.Add(Heading("GROUP"),0,5); layout.Controls.Add(groups,0,6);
            groups.Dock = DockStyle.Top; groups.DropDownStyle = ComboBoxStyle.DropDownList;
            groups.FlatStyle = FlatStyle.Flat; groups.BackColor = Color.FromArgb(43,43,46); groups.ForeColor = ForeColor;
            groups.DropDownWidth = 350;
            groups.DropDown += delegate { RefreshDevices(); };
            groups.SelectionChangeCommitted += delegate {
                var profile = groups.SelectedItem as Profile;
                if (profile == null) return;
                try {
                    var connected = audio.List(0).Concat(audio.List(1)).ToList();
                    var pair = Pair(profile,connected);
                    if (pair.Length != 2) { status.Text = "Group unavailable: a device disconnected."; fingerprint = ""; RefreshDevices(); return; }
                    Apply(pair);
                } catch (Exception error) { status.Text = error.Message; }
            };
            foreach (var panel in new[] { outputs,inputs }) { panel.Dock = DockStyle.Fill; panel.AutoScroll = true; panel.FlowDirection = FlowDirection.TopDown; panel.WrapContents = false; }
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            actions.Controls.Add(Button("Manage groups",ManageGroups));
            var pin = new CheckBox { Text = "Keep on top", AutoSize = true, Checked = TopMost, Margin = new Padding(10,9,0,0) };
            pin.CheckedChanged += delegate { TopMost = settings.Pinned = pin.Checked; Save(); };
            actions.Controls.Add(pin); actions.Controls.Add(Button("Hide",delegate { Hide(); }));
            layout.Controls.Add(actions,0,7);
            status.Text = "Choose a device or group to switch."; status.Dock = DockStyle.Fill; status.ForeColor = Color.Silver;
            layout.Controls.Add(status,0,8); Controls.Add(layout);
            tray.Icon = brandIcon; tray.Text = "Audio Me - click to switch devices";
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show widget",null,delegate { ShowWidget(); });
            menu.Items.Add("Exit",null,delegate { exiting = true; Close(); });
            tray.ContextMenuStrip = menu; tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { if (Visible) Hide(); else ShowWidget(); } };
            tray.Visible = true;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            timer.Interval = 2000; timer.Tick += delegate { RefreshDevices(); }; timer.Start();
            Shown += delegate { RefreshDevices(); if (startHidden) Hide(); };
        }
        Label Heading(string text) { return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(160,165,175), Font = new Font("Segoe UI",9,FontStyle.Bold) }; }
        Button Button(string text, EventHandler action) {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38,38,41), ForeColor = Color.WhiteSmoke, Padding = new Padding(3), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(55,55,59); b.Click += action; return b;
        }
        void ShowWidget() { Show(); WindowState = FormWindowState.Normal; Activate(); RefreshDevices(); }
        void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                string temporary = settingsFile + ".tmp";
                using (var stream = File.Create(temporary)) new XmlSerializer(typeof(Settings)).Serialize(stream,settings);
                if (File.Exists(settingsFile)) File.Replace(temporary,settingsFile,null); else File.Move(temporary,settingsFile);
            } catch (Exception e) { status.Text = "Could not save settings: " + e.Message; }
        }
        void Apply(params Endpoint[] devices) {
            try { audio.Apply(devices); status.Text = "Verified: " + String.Join(" + ",devices.Select(d => d.Name).ToArray()) + ". All three roles set."; }
            catch (Exception e) { status.Text = "Switch failed."; MessageBox.Show(this,e.Message,"Audio Me",MessageBoxButtons.OK,MessageBoxIcon.Error); }
            fingerprint = ""; RefreshDevices();
        }
        void RefreshDevices() {
            if (refreshing) return; refreshing = true;
            try {
                var list = audio.List(0).Concat(audio.List(1)).ToList();
                var roles = list.ToDictionary(d => d.Id,d => audio.Roles(d));
                string next = String.Join("|",list.Select(d => d.Id + d.Name + roles[d.Id]).ToArray());
                if (next == fingerprint) return;
                if (groups.DroppedDown) return;
                fingerprint = next;
                foreach (var panel in new[] { outputs,inputs }) { foreach (Control c in panel.Controls.Cast<Control>().ToArray()) c.Dispose(); panel.Controls.Clear(); }
                tips.RemoveAll();
                foreach (var d in settings.SortDevices(list)) {
                    var device = d; string role = roles[d.Id];
                    bool favorite = settings.Favorites.Contains(d.Id), dimmed = settings.Deemphasized.Contains(d.Id);
                    var row = new TableLayoutPanel { Width = 370, Height = 38, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,30));
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,30));
                    var button = Button((role.Length > 0 ? "✓  " : "    ") + d.Name,delegate { Apply(device); });
                    button.Dock = DockStyle.Fill; button.AutoSize = false; button.TextAlign = ContentAlignment.MiddleLeft;
                    button.AutoEllipsis = true; button.Margin = new Padding(0,0,0,2);
                    if (role.Length > 0) { button.BackColor = Color.FromArgb(40,49,53); button.ForeColor = Color.FromArgb(150,218,218); }
                    if (dimmed) {
                        var fg = button.ForeColor; var bg = button.BackColor;
                        button.ForeColor = Color.FromArgb((fg.R+bg.R)/2,(fg.G+bg.G)/2,(fg.B+bg.B)/2);
                    }
                    tips.SetToolTip(button,d.Name + (role.Length > 0 ? "\n" + role : ""));
                    var star = Button(favorite ? "★" : "☆",delegate { SetPreference(device.Id,true); });
                    star.ForeColor = favorite ? Color.FromArgb(244,202,102) : Color.FromArgb(150,150,155);
                    star.AccessibleName = (favorite ? "Remove favorite: " : "Favorite: ") + d.Name;
                    var dismiss = Button("×",delegate { SetPreference(device.Id,false); });
                    dismiss.ForeColor = Color.FromArgb(225,100,100);
                    dismiss.AccessibleName = (dimmed ? "Restore: " : "Move to bottom: ") + d.Name;
                    tips.SetToolTip(star,favorite ? "Remove from favorites" : "Add to favorites");
                    tips.SetToolTip(dismiss,dimmed ? "Restore device" : "Move to bottom and dim");
                    foreach (var control in new[] { star,dismiss }) { control.AutoSize = false; control.Dock = DockStyle.Fill; control.Margin = new Padding(0,0,0,2); control.Padding = Padding.Empty; }
                    row.Controls.Add(button,0,0); row.Controls.Add(star,1,0); row.Controls.Add(dismiss,2,0);
                    using (var graphics = CreateGraphics()) row.Scale(new SizeF(graphics.DpiX/96f,graphics.DpiY/96f));
                    (d.Flow == 0 ? outputs : inputs).Controls.Add(row);
                }
                if (outputs.Controls.Count == 0) outputs.Controls.Add(Heading("No active playback devices"));
                if (inputs.Controls.Count == 0) inputs.Controls.Add(Heading("No active recording devices"));
                groups.Items.Clear();
                var available = settings.Profiles.Where(p => Pair(p,list).Length == 2).ToArray();
                groups.Items.Add("Choose a group…"); groups.Items.AddRange(available);
                groups.SelectedIndex = 0;
                foreach (var p in available) if (Pair(p,list).All(d => roles[d.Id] == "System / Media / Calls")) { groups.SelectedItem = p; break; }
                groups.Enabled = available.Length > 0;
                if (available.Length == 0) { groups.Items[0] = settings.Profiles.Count == 0 ? "Create a group to get started" : "No connected groups"; }
            } catch (Exception e) { fingerprint = ""; status.Text = "Audio refresh failed: " + e.Message; }
            finally { refreshing = false; }
        }
        internal static Endpoint[] Pair(Profile p, List<Endpoint> list) {
            return list.Where(d => (d.Flow == 0 && d.Id == p.Output) || (d.Flow == 1 && d.Id == p.Input)).ToArray();
        }
        void SetPreference(string id, bool favorite) {
            settings.ToggleDevice(id,favorite); Save(); fingerprint = ""; RefreshDevices();
        }
        void ManageGroups(object sender, EventArgs e) {
            using (var dialog = new Form { Text = "Groups", ClientSize = new Size(440,310), StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false }) {
                var list = new ListBox { Left = 18, Top = 18, Width = 404, Height = 220, BackColor = Color.FromArgb(43,43,46), ForeColor = ForeColor, BorderStyle = BorderStyle.None };
                Action reload = delegate { list.Items.Clear(); list.Items.AddRange(settings.Profiles.ToArray()); };
                reload();
                var actions = new FlowLayoutPanel { Left = 18, Top = 252, Width = 404, Height = 45 };
                actions.Controls.Add(Button("New group",delegate { SavePair(null,dialog); reload(); }));
                actions.Controls.Add(Button("Edit / rename",delegate { var p = list.SelectedItem as Profile; if (p != null) { SavePair(p,dialog); reload(); } }));
                actions.Controls.Add(Button("Delete",delegate { var p = list.SelectedItem as Profile; if (p != null) { settings.Profiles.Remove(p); Save(); reload(); fingerprint = ""; } }));
                var done = Button("Done",delegate { dialog.Close(); }); actions.Controls.Add(done);
                dialog.Controls.Add(list); dialog.Controls.Add(actions); dialog.ShowDialog(this);
            }
            fingerprint = ""; RefreshDevices();
        }
        void SavePair(Profile existing, IWin32Window owner) {
            using (var dialog = new Form { Text = existing == null ? "New group" : "Edit group", ClientSize = new Size(420,285), StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false }) {
                var name = new TextBox { Left = 20, Top = 40, Width = 380, Text = existing == null ? "" : existing.Name };
                var output = new ComboBox { Left = 20, Top = 108, Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 500 };
                var input = new ComboBox { Left = 20, Top = 176, Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 500 };
                dialog.Controls.Add(new Label { Text = "Group name", Left = 20, Top = 18, AutoSize = true });
                dialog.Controls.Add(new Label { Text = "Speakers / headphones", Left = 20, Top = 85, AutoSize = true });
                dialog.Controls.Add(new Label { Text = "Microphone / recording", Left = 20, Top = 153, AutoSize = true });
                try { output.Items.AddRange(audio.List(0).ToArray()); input.Items.AddRange(audio.List(1).ToArray()); }
                catch (Exception error) { MessageBox.Show(this,error.Message); return; }
                foreach (var combo in new[] { output,input }) {
                    int flow = combo == output ? 0 : 1;
                    string id = existing == null ? audio.Default(flow,0) : (flow == 0 ? existing.Output : existing.Input);
                    var selected = combo.Items.Cast<Endpoint>().FirstOrDefault(d => d.Id == id);
                    if (selected == null && existing != null) { selected = new Endpoint { Id = id, Flow = flow, Name = "Disconnected device (saved)" }; combo.Items.Add(selected); }
                    if (selected != null) combo.SelectedItem = selected; else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                    combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Color.FromArgb(43,43,46); combo.ForeColor = ForeColor;
                }
                var save = Button("Save group",delegate {
                    if (String.IsNullOrWhiteSpace(name.Text) || output.SelectedItem == null || input.SelectedItem == null) { MessageBox.Show(dialog,"Enter a name and choose both devices."); return; }
                    if (settings.Profiles.Any(p => p != existing && String.Equals(p.Name,name.Text.Trim(),StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(dialog,"A group already has this name."); return; }
                    var profile = existing ?? new Profile();
                    profile.Name = name.Text.Trim(); profile.Output = ((Endpoint)output.SelectedItem).Id; profile.Input = ((Endpoint)input.SelectedItem).Id;
                    if (existing == null) settings.Profiles.Add(profile);
                    Save(); fingerprint = ""; dialog.DialogResult = DialogResult.OK; dialog.Close();
                });
                save.SetBounds(280,234,120,32); save.AutoSize = false;
                dialog.Controls.AddRange(new Control[] { name,output,input,save }); dialog.AcceptButton = save;
                dialog.ShowDialog(owner);
            }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { timer.Dispose(); tips.Dispose(); tray.Visible = false; tray.Dispose(); audio.Dispose(); brandIcon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
