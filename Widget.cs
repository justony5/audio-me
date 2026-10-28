using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioSwitch {
    class ThemeButton : Button {
        protected override void OnPaint(PaintEventArgs e) {
            if(Enabled) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor);
            var bounds=ClientRectangle; bounds.Inflate(-6,-3);
            TextRenderer.DrawText(e.Graphics,Text,Font,bounds,Color.FromArgb(170,174,182),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
    }
    class Widget : Form {
        readonly SettingsStore store;
        readonly IAudioService service;
        readonly Settings settings;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        readonly ToolTip tips = new ToolTip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly FlowLayoutPanel content = new FlowLayoutPanel();
        readonly FlowLayoutPanel profileList = new FlowLayoutPanel();
        readonly FlowLayoutPanel outputList = new DeviceList();
        readonly FlowLayoutPanel inputList = new DeviceList();
        readonly FlowLayoutPanel otherList = new DeviceList();
        readonly TableLayoutPanel editor = new TableLayoutPanel();
        readonly Label profileState = new Label(), status = new Label(), recovery = new Label();
        readonly TextBox activeProfileName = new TextBox();
        readonly TextBox profileName = new TextBox();
        readonly ComboBox profileOutput = new ComboBox(), profileInput = new ComboBox(), editChoice = new ComboBox();
        readonly Label editorMessage = new Label();
        readonly FlowLayoutPanel recoveryBox = new FlowLayoutPanel();
        readonly Button otherToggle;
        readonly Icon brandIcon;
        readonly FlowLayoutPanel footer;
        readonly List<Control> switchControls = new List<Control>();
        AudioSnapshot snapshot = new AudioSnapshot();
        bool exiting, busy, reading, fitting, otherExpanded;
        bool traySwitch;
        Profile editing;
        string fingerprint = "";
        Exception lastRenderError;
        public Widget(bool hidden) : this(hidden,new WindowsAudioService(),new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AudioSwitch","settings.xml"))) { }
        internal Widget(bool hidden, IAudioService audioService, SettingsStore settingsStore) {
            SuspendLayout();
            service=audioService; store=settingsStore; settings=store.Load();
            Font=new Font("Segoe UI",10.5f); BackColor=Color.FromArgb(30,30,32); ForeColor=Color.WhiteSmoke;
            Text="Audio Me"; ClientSize=new Size(480,720); MinimumSize=new Size(440,480); StartPosition=FormStartPosition.CenterScreen;
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("AudioMe.ico")) brandIcon=new Icon(stream);
            Icon=brandIcon; TopMost=settings.Pinned;
            content.Dock=DockStyle.Fill; content.FlowDirection=FlowDirection.TopDown; content.WrapContents=false; content.AutoScroll=true; content.Padding=new Padding(14);
            Controls.Add(content);
            var header=Actions();
            header.Controls.Add(TextLabel("Audio Me",true));
            header.Controls.Add(TextLabel("Free · Public beta",false)); content.Controls.Add(header);
            recoveryBox.AutoSize=true; recoveryBox.FlowDirection=FlowDirection.TopDown; recoveryBox.WrapContents=false;
            recovery.AutoSize=true; recovery.ForeColor=Color.FromArgb(245,195,125); recovery.Text=store.RecoveryMessage;
            recoveryBox.Controls.Add(recovery);
            recoveryBox.Controls.Add(ActionButton("Back up original and reset",delegate {
                try { store.BackupAndReset(); Save(); recoveryBox.Visible=false; status.Text="Original settings backed up to "+store.BackupPath; }
                catch(Exception e) { status.Text="Recovery failed; original retained. "+e.Message; }
            }));
            recoveryBox.Visible=store.RecoveryRequired; content.Controls.Add(recoveryBox);
            var profileHeader=Actions(); profileHeader.Controls.Add(Heading("PROFILES"));
            profileHeader.Controls.Add(ActionButton("Create profile",delegate { BeginEdit(null); }));
            var manage=ActionButton("Edit profiles",delegate { editor.Visible=!editor.Visible; if(editor.Visible) PopulateEditorChoices(); FitContent(); });
            profileHeader.Controls.Add(manage); content.Controls.Add(profileHeader);
            content.Controls.Add(TextLabel("Active profile",false));
            activeProfileName.ReadOnly=true; activeProfileName.BackColor=Color.FromArgb(38,41,46); activeProfileName.ForeColor=Color.FromArgb(150,218,218);
            activeProfileName.BorderStyle=BorderStyle.FixedSingle; activeProfileName.AccessibleName="Active profile; blank means custom setup";
            content.Controls.Add(activeProfileName);
            profileState.AutoSize=true; profileState.ForeColor=Color.FromArgb(150,218,218); content.Controls.Add(profileState);
            SetupList(profileList); content.Controls.Add(profileList);
            BuildEditor(); content.Controls.Add(editor);
            content.Controls.Add(DeviceHeading("PLAYBACK")); SetupList(outputList); content.Controls.Add(outputList);
            content.Controls.Add(DeviceHeading("MICROPHONE")); SetupList(inputList); content.Controls.Add(inputList);
            otherToggle=ActionButton("Other devices",delegate { otherExpanded=!otherExpanded; otherList.Visible=otherExpanded; UpdateOtherTitle(); FitContent(); });
            content.Controls.Add(otherToggle); SetupList(otherList); otherList.Visible=false; content.Controls.Add(otherList);
            footer=Actions();
            var pin=new CheckBox { Text="Keep on top",AutoSize=true,Checked=settings.Pinned,Margin=new Padding(3,8,6,3) };
            pin.CheckedChanged+=delegate { TopMost=settings.Pinned=pin.Checked; Save(); }; footer.Controls.Add(pin);
            var startup=new CheckBox { Text="Start with Windows",AutoSize=true,Margin=new Padding(3,8,6,3) };
            startup.Checked=StartupEnabled(); startup.CheckedChanged+=delegate {
                try { SetStartup(startup.Checked); status.Text="Windows startup preference updated for this user."; }
                catch(Exception e) { status.Text="Startup preference could not be changed: "+e.Message; }
            }; footer.Controls.Add(startup);
            footer.Controls.Add(ActionButton("Hide",delegate { Hide(); })); content.Controls.Add(footer);
            var note=TextLabel("Closing hides Audio Me to the tray. Launch it again to reopen.\nWindows defaults may not redirect apps with their own device selections.",false);
            note.ForeColor=Color.Silver; content.Controls.Add(note);
            status.AutoSize=true; status.ForeColor=Color.FromArgb(200,205,215); status.Text="Reading connected devices…"; content.Controls.Add(status);
            tray.Icon=brandIcon; tray.Text="Audio Me";
            trayMenu.ShowItemToolTips=true;
            trayMenu.Opening+=delegate { BuildTrayMenu(); RefreshDevices(); };
            tray.ContextMenuStrip=trayMenu; tray.MouseClick+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { if(Visible) Hide(); else ShowWidget(); } }; tray.Visible=true;
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                RememberSize(); Save();
                if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); }
            };
            VisibleChanged+=delegate { if(!Visible && IsHandleCreated) { RememberSize(); Save(); } };
            ResizeEnd+=delegate { RememberSize(); Save(); };
            content.ClientSizeChanged+=delegate { FitContent(); };
            timer.Interval=2000; timer.Tick+=delegate { RefreshDevices(); };
            Shown+=delegate {
                if(settings.WindowWidth>0 && settings.WindowHeight>0) {
                    var area=Screen.FromControl(this).WorkingArea;
                    Size=new Size(Math.Min(area.Width,Math.Max(MinimumSize.Width,settings.WindowWidth)),Math.Min(area.Height,Math.Max(MinimumSize.Height,settings.WindowHeight)));
                }
                FitContent(); RefreshDevices(); timer.Start(); if(hidden) Hide();
            };
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi; ResumeLayout(true);
        }
        Label TextLabel(string text,bool bold) { return new Label { Text=text,AutoSize=true,Font=bold?new Font(Font,FontStyle.Bold):Font,Margin=new Padding(3,5,3,5) }; }
        Label Heading(string text) { var l=TextLabel(text,true); l.ForeColor=Color.FromArgb(160,165,175); return l; }
        Label DeviceHeading(string text) {
            return new Label { Text=text,AutoSize=true,Font=new Font("Segoe UI",11.5f,FontStyle.Bold),ForeColor=Color.FromArgb(177,220,224),Margin=new Padding(3,18,3,8) };
        }
        FlowLayoutPanel Actions() { return new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,1,0,1) }; }
        Button ActionButton(string text,EventHandler handler) {
            var b=new ThemeButton { Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(40,40,44),ForeColor=ForeColor,Padding=new Padding(5,3,5,3),Cursor=Cursors.Hand,Font=Font };
            b.FlatAppearance.BorderSize=0; b.FlatAppearance.MouseOverBackColor=Color.FromArgb(58,58,63); b.Click+=handler; return b;
        }
        void SetupList(FlowLayoutPanel panel) {
            panel.FlowDirection=FlowDirection.TopDown; panel.WrapContents=false; panel.AutoScroll=true; panel.Margin=Padding.Empty;
            panel.ClientSizeChanged+=delegate { FitRows(panel); };
        }
        int Px(int value) { using(var g=CreateGraphics()) return (int)Math.Round(value*g.DpiX/96f); }
        void FitRows(FlowLayoutPanel panel) {
            int width=Math.Max(100,panel.ClientSize.Width-panel.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-2);
            foreach(Control c in panel.Controls) c.Width=width;
        }
        void FitContent() {
            if(fitting || IsDisposed) return; fitting=true;
            try {
                int width=Math.Max(160,content.ClientSize.Width-content.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-4);
                foreach(Control c in content.Controls) {
                    c.Width=width;
                    var label=c as Label; if(label!=null) label.MaximumSize=new Size(width,0);
                    if(c.AutoSize && !(c is Label)) c.MaximumSize=new Size(width,0);
                }
                foreach(Label label in editor.Controls.OfType<Label>()) label.MaximumSize=new Size(Math.Max(100,width-editor.Padding.Horizontal-8),0);
                recovery.MaximumSize=new Size(Math.Max(100,width-10),0);
                editorMessage.MaximumSize=new Size(Math.Max(100,width-10),0);
                foreach(var list in new[] { profileList,outputList,inputList,otherList }) {
                    list.Width=width;
                    int total=list.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical);
                    list.Height=Math.Min(Px(list==profileList?130:128),Math.Max(Px(26),total+list.Padding.Vertical+2));
                    FitRows(list);
                }
            } finally { fitting=false; }
        }
        void Clear(FlowLayoutPanel panel) { foreach(Control c in panel.Controls.Cast<Control>().ToArray()) c.Dispose(); panel.Controls.Clear(); }
        void PostToUi(Action action) {
            if(IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch(InvalidOperationException) { }
        }
        void RefreshDevices() {
            if(reading || busy || IsDisposed) return; reading=true;
            Task.Factory.StartNew(()=>service.Read(),CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default).ContinueWith(task=>{
                var observed=task.Exception;
                PostToUi(delegate {
                reading=false; if(IsDisposed || Disposing) return;
                if(task.IsFaulted) { status.Text="Could not read audio devices: "+task.Exception.GetBaseException().Message; return; }
                if(busy) return;
                snapshot=task.Result;
                UpdateTrayProfileStates();
                foreach(var profile in settings.Profiles) {
                    var output=snapshot.Devices.FirstOrDefault(d=>d.Flow==0 && d.Id==profile.Output);
                    var input=snapshot.Devices.FirstOrDefault(d=>d.Flow==1 && d.Id==profile.Input);
                    if(output!=null) profile.OutputName=output.Name; if(input!=null) profile.InputName=input.Name;
                }
                string next=String.Join("|",snapshot.Devices.Select(d=>d.Id+d.Name+snapshot.Roles(d)).ToArray());
                if(next!=fingerprint) { fingerprint=next; Render(); }
                if(status.Text=="Reading connected devices…") status.Text="Ready. Choose a device or profile.";
                });
            },TaskScheduler.Default);
        }
        void Render() {
            content.SuspendLayout(); tips.RemoveAll(); switchControls.Clear();
            try {
                foreach(var list in new[] { profileList,outputList,inputList,otherList }) Clear(list);
                var active=settings.Profiles.Where(snapshot.IsActive).Select(p=>p.Name).ToArray();
                activeProfileName.Text=String.Join(", ",active);
                profileState.Text=active.Length>0?"Matches playback and microphone for all three roles.":"Custom setup · no profile selected";
                tips.SetToolTip(activeProfileName,active.Length>0?activeProfileName.Text:"No saved profile matches the current Windows defaults.");
                foreach(var p in settings.Profiles) {
                    var profile=p; string missing=snapshot.Missing(p);
                    var row=new TableLayoutPanel { Height=Px(60),ColumnCount=2,RowCount=2,Margin=new Padding(0,0,0,3) };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(58)));
                    row.RowStyles.Add(new RowStyle(SizeType.Percent,55)); row.RowStyles.Add(new RowStyle(SizeType.Percent,45));
                    var activate=ActionButton((snapshot.IsActive(p)?"✓  ":"Use ")+p.Name,delegate { ApplyProfile(profile); });
                    activate.AutoSize=false; activate.Dock=DockStyle.Fill; activate.AutoEllipsis=true; activate.TextAlign=ContentAlignment.MiddleLeft; activate.Enabled=missing.Length==0 && !busy;
                    activate.AccessibleName="Activate profile "+p.Name; switchControls.Add(activate);
                    if(snapshot.IsActive(p)) activate.ForeColor=Color.FromArgb(150,218,218);
                    var edit=ActionButton("Edit",delegate { BeginEdit(profile); }); edit.Dock=DockStyle.Fill;
                    var detail=TextLabel(missing.Length>0?missing:"Playback + microphone · "+(p.OutputName??FindName(p.Output))+" + "+(p.InputName??FindName(p.Input)),false);
                    detail.AutoSize=false; detail.AutoEllipsis=true; detail.Dock=DockStyle.Fill; detail.ForeColor=missing.Length>0?Color.FromArgb(235,184,123):Color.Silver;
                    tips.SetToolTip(activate,missing.Length>0?missing:"Set one playback device and one microphone for System, Media and Calls."); tips.SetToolTip(detail,detail.Text);
                    row.Controls.Add(activate,0,0); row.Controls.Add(edit,1,0); row.Controls.Add(detail,0,1); row.SetColumnSpan(detail,2); profileList.Controls.Add(row);
                }
                if(settings.Profiles.Count==0) { var create=ActionButton("Create your first profile",delegate { BeginEdit(null); }); profileList.Controls.Add(create); profileState.Text="A profile selects one playback device and one microphone."; }
                foreach(var d in settings.SortDevices(snapshot.Devices)) {
                    var list=settings.Deemphasized.Contains(d.Id)?otherList:(d.Flow==0?outputList:inputList);
                    list.Controls.Add(DeviceRow(d));
                }
                foreach(var list in new[] { outputList,inputList }) if(list.Controls.Count==0) list.Controls.Add(TextLabel("No devices here. Check Other devices or connect hardware.",false));
                UpdateOtherTitle();
                UpdateTrayProfileStates();
            } catch(Exception e) { lastRenderError=e; fingerprint=""; status.Text="Could not update device view: "+e.Message; }
            finally { content.ResumeLayout(true); FitContent(); }
        }
        string FindName(string id) { var d=snapshot.Devices.FirstOrDefault(x=>x.Id==id); return d==null?id:d.Name; }
        Control DeviceRow(Endpoint d) {
            var row=new TableLayoutPanel { Height=Px(36),ColumnCount=7,RowCount=1,Margin=new Padding(0,0,0,3),BackColor=Color.FromArgb(40,40,44) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int width in new[] {22,22,22,28,28,28}) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Px(width)));
            string roles=snapshot.Roles(d); bool other=settings.Deemphasized.Contains(d.Id), favorite=settings.Favorites.Contains(d.Id);
            var select=ActionButton((roles.Length>0?"✓  ":"")+settings.DisplayName(d),delegate { ApplyDevices(new[] {d}); });
            select.Dock=DockStyle.Fill; select.AutoSize=false; select.AutoEllipsis=true; select.TextAlign=ContentAlignment.MiddleLeft; select.Enabled=!busy;
            select.Margin=Padding.Empty; select.Padding=new Padding(5,0,5,0);
            select.AccessibleName="Switch "+(d.Flow==0?"playback to ":"microphone to ")+settings.DisplayName(d); switchControls.Add(select);
            if(roles.Length>0) { select.BackColor=Color.FromArgb(40,49,53); select.ForeColor=Color.FromArgb(150,218,218); }
            var star=ActionButton(favorite?"★":"☆",delegate { settings.ToggleDevice(d.Id,true); Save(); Render(); }); star.ForeColor=favorite?Color.FromArgb(244,202,102):Color.Silver;
            var move=ActionButton(other?"↑":"↓",delegate { settings.ToggleDevice(d.Id,false); Save(); Render(); });
            var alias=ActionButton("✎",delegate { BeginAlias(d); });
            star.AccessibleName=(favorite?"Remove favorite ":"Favorite ")+d.Name; move.AccessibleName=(other?"Move to main list ":"Move to Other devices ")+d.Name; alias.AccessibleName="Rename display label for "+d.Name;
            foreach(var b in new[] { star,move,alias }) { b.Dock=DockStyle.Fill; b.AutoSize=false; b.Padding=Padding.Empty; b.Margin=Padding.Empty; }
            select.AccessibleDescription=roles.Length>0?"Default for "+roles:"Not a Windows default";
            tips.SetToolTip(select,d.Name+"\n"+(d.Flow==0?"Playback":"Microphone")+" · "+(roles.Length==0?"Not a default":roles));
            tips.SetToolTip(star,star.AccessibleName); tips.SetToolTip(move,move.AccessibleName); tips.SetToolTip(alias,alias.AccessibleName);
            row.Controls.Add(select,0,0);
            for(int role=0;role<3;role++) {
                var indicator=new RoleIndicator(role,snapshot.Defaults[d.Flow][role]==d.Id) { Dock=DockStyle.Fill,Margin=Padding.Empty };
                tips.SetToolTip(indicator,indicator.AccessibleName); row.Controls.Add(indicator,role+1,0);
            }
            row.Controls.Add(star,4,0); row.Controls.Add(move,5,0); row.Controls.Add(alias,6,0); return row;
        }
        void UpdateOtherTitle() { otherToggle.Text=(otherExpanded?"▾":"▸")+" Other devices ("+otherList.Controls.Count+") · connected, less used"; }
        void ApplyProfile(Profile p) {
            string missing=snapshot.Missing(p); if(missing.Length>0) { status.Text=missing; return; }
            ApplyDevices(snapshot.Pair(p));
        }
        void BuildTrayMenu() {
            foreach(ToolStripItem item in trayMenu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
            trayMenu.Items.Clear();
            trayMenu.Items.Add("Show Audio Me",null,delegate { ShowWidget(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Profiles") { Enabled=false });
            foreach(var p in settings.Profiles) {
                var profile=p;
                // Text is assigned literally: profile names may contain an ampersand.
                var item=new ToolStripMenuItem { Text=p.Name,Tag=p };
                item.Click+=delegate {
                    if(busy || !settings.Profiles.Contains(profile)) return;
                    string missing=snapshot.Missing(profile);
                    if(missing.Length>0) { status.Text=missing; return; }
                    traySwitch=true; ApplyProfile(profile);
                };
                trayMenu.Items.Add(item);
            }
            if(settings.Profiles.Count==0) trayMenu.Items.Add("Create a profile…",null,delegate { ShowWidget(); BeginEdit(null); });
            trayMenu.Items.Add(new ToolStripSeparator());
            var exit=new ToolStripMenuItem("Exit",null,delegate { if(busy) return; exiting=true; Close(); });
            exit.Name="Exit"; trayMenu.Items.Add(exit);
            UpdateTrayProfileStates();
        }
        void UpdateTrayProfileStates() {
            foreach(var item in trayMenu.Items.OfType<ToolStripMenuItem>()) {
                var p=item.Tag as Profile;
                if(p!=null) {
                    string missing=snapshot.Missing(p);
                    item.Checked=snapshot.IsActive(p);
                    item.Enabled=!busy && missing.Length==0 && settings.Profiles.Contains(p);
                    item.Text=p.Name.Replace("&","&&")+(missing.Length>0?" (unavailable)":"");
                    item.ToolTipText=busy?"Switching audio; please wait.":missing.Length>0?missing:"Switch playback and microphone to "+p.Name;
                    item.AccessibleName=p.Name+(item.Checked?", active profile":"")+(missing.Length>0?", "+missing:"");
                } else if(item.Name=="Exit") item.Enabled=!busy;
            }
        }
        void ApplyDevices(Endpoint[] devices) {
            if(busy || devices.Length==0) return; busy=true; status.Text="Switching and verifying System, Media and Calls…";
            bool notify=traySwitch; traySwitch=false; UpdateTrayProfileStates();
            foreach(var c in switchControls) c.Enabled=false;
            Task.Factory.StartNew(()=>service.Apply(devices),CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default).ContinueWith(task=>{
                var observed=task.Exception;
                PostToUi(delegate {
                busy=false; if(IsDisposed || Disposing) return;
                status.Text=task.IsFaulted?"Switch failed: "+task.Exception.GetBaseException().Message:"Verified: System, Media and Calls updated.";
                if(notify && !Visible) {
                    tray.BalloonTipTitle=task.IsFaulted?"Audio Me · switch failed":"Audio Me · profile applied";
                    tray.BalloonTipText=status.Text; tray.BalloonTipIcon=task.IsFaulted?ToolTipIcon.Warning:ToolTipIcon.Info; tray.ShowBalloonTip(3000);
                }
                Render(); fingerprint=""; RefreshDevices();
                });
            },TaskScheduler.Default);
        }
        void BuildEditor() {
            editor.AutoSize=true; editor.AutoSizeMode=AutoSizeMode.GrowAndShrink; editor.ColumnCount=1; editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); editor.Visible=false; editor.Padding=new Padding(8); editor.BackColor=Color.FromArgb(36,36,40);
            editChoice.DropDownStyle=ComboBoxStyle.DropDownList; editChoice.AccessibleName="Choose profile to edit";
            editChoice.SelectionChangeCommitted+=delegate { var p=editChoice.SelectedItem as Profile; BeginEdit(p); };
            profileName.AccessibleName="Profile name";
            foreach(var combo in new[] { profileOutput,profileInput,editChoice }) { combo.DropDownStyle=ComboBoxStyle.DropDownList; combo.FlatStyle=FlatStyle.Flat; combo.BackColor=Color.FromArgb(47,47,51); combo.ForeColor=ForeColor; combo.Dock=DockStyle.Top; combo.DropDownWidth=600; }
            profileOutput.AccessibleName="Profile playback device"; profileInput.AccessibleName="Profile microphone";
            profileName.Dock=DockStyle.Top;
            editor.Controls.Add(TextLabel("PROFILE EDITOR · one playback + one microphone",true)); editor.Controls.Add(editChoice);
            editor.Controls.Add(TextLabel("Name",false)); editor.Controls.Add(profileName);
            editor.Controls.Add(TextLabel("Playback",false)); editor.Controls.Add(profileOutput);
            editor.Controls.Add(TextLabel("Microphone",false)); editor.Controls.Add(profileInput);
            editorMessage.AutoSize=true; editorMessage.ForeColor=Color.FromArgb(240,187,121); editor.Controls.Add(editorMessage);
            var actions=Actions(); actions.Controls.Add(ActionButton("Save profile",delegate { SaveProfile(); }));
            actions.Controls.Add(ActionButton("Delete profile",delegate { if(editing!=null) { settings.Profiles.Remove(editing); Save(); editor.Visible=false; Render(); } }));
            actions.Controls.Add(ActionButton("Cancel",delegate { editor.Visible=false; FitContent(); })); editor.Controls.Add(actions);
        }
        void PopulateEditorChoices() {
            editChoice.Items.Clear(); editChoice.Items.Add("New profile"); editChoice.Items.AddRange(settings.Profiles.ToArray()); editChoice.SelectedIndex=0;
            BeginEdit(null);
        }
        void BeginEdit(Profile p) {
            editing=p; editor.Visible=true; profileName.Text=p==null?"":p.Name; editorMessage.Text="Disconnected selections are retained; replacements must be chosen explicitly.";
            if(editChoice.Items.Count!=settings.Profiles.Count+1) { editChoice.Items.Clear(); editChoice.Items.Add("New profile"); editChoice.Items.AddRange(settings.Profiles.ToArray()); }
            editChoice.SelectedItem=p==null?(object)"New profile":p;
            FillDevices(profileOutput,0,p==null?null:p.Output,p==null?null:p.OutputName);
            FillDevices(profileInput,1,p==null?null:p.Input,p==null?null:p.InputName);
            profileName.Focus(); FitContent(); content.ScrollControlIntoView(editor);
        }
        void FillDevices(ComboBox combo,int flow,string id,string savedName) {
            combo.Items.Clear(); combo.Items.Add("Choose a device…"); combo.Items.AddRange(snapshot.Devices.Where(d=>d.Flow==flow).ToArray());
            var selected=combo.Items.OfType<Endpoint>().FirstOrDefault(d=>d.Id==id);
            if(selected==null && id!=null) { selected=new Endpoint { Id=id,Flow=flow,Name=savedName??id }; combo.Items.Add(selected); }
            combo.SelectedItem=selected==null?combo.Items[0]:(object)selected;
        }
        void SaveProfile() {
            var output=profileOutput.SelectedItem as Endpoint; var input=profileInput.SelectedItem as Endpoint;
            if(String.IsNullOrWhiteSpace(profileName.Text) || output==null || input==null) { editorMessage.Text="Enter a name and choose both devices. No substitutes are selected automatically."; return; }
            if(settings.Profiles.Any(p=>p!=editing && String.Equals(p.Name,profileName.Text.Trim(),StringComparison.OrdinalIgnoreCase))) { editorMessage.Text="Choose a unique profile name."; return; }
            var saved=editing??new Profile(); saved.Name=profileName.Text.Trim(); saved.Output=output.Id; saved.Input=input.Id; saved.OutputName=output.Name; saved.InputName=input.Name;
            if(editing==null) settings.Profiles.Add(saved);
            if(Save()) { editor.Visible=false; status.Text="Profile saved. Select it to switch devices."; } Render();
        }
        void BeginAlias(Endpoint d) {
            // A small inline label editor avoids changing the hardware name or endpoint reference.
            editor.Visible=false;
            var panel=Actions(); var box=new TextBox { Text=settings.DisplayName(d),Width=Px(230),AccessibleName="Local display alias for "+d.Name };
            panel.Controls.Add(TextLabel("Label: "+d.Name,false)); panel.Controls.Add(box);
            panel.Controls.Add(ActionButton("Save label",delegate {
                settings.Aliases.RemoveAll(a=>a.Id==d.Id);
                if(!String.IsNullOrWhiteSpace(box.Text) && box.Text.Trim()!=d.Name) settings.Aliases.Add(new DeviceAlias { Id=d.Id,Name=box.Text.Trim() });
                Save(); content.Controls.Remove(panel); panel.Dispose(); Render();
            }));
            panel.Controls.Add(ActionButton("Cancel",delegate { content.Controls.Remove(panel); panel.Dispose(); FitContent(); }));
            content.Controls.Add(panel); content.Controls.SetChildIndex(panel,2); FitContent(); content.ScrollControlIntoView(panel); box.Focus();
        }
        bool Save() {
            if(store.RecoveryRequired) return false;
            try { store.Save(settings); return true; } catch(Exception e) { status.Text="Settings could not be saved: "+e.Message; return false; }
        }
        void RememberSize() { var bounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds; if(bounds.Width>0 && bounds.Height>0) { settings.WindowWidth=bounds.Width; settings.WindowHeight=bounds.Height; } }
        internal void ShowWidget() { Show(); if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal; Activate(); RefreshDevices(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowTheme.Apply(Handle); }
        protected override void WndProc(ref Message message) {
            base.WndProc(ref message);
            // Windows broadcasts setting/theme changes; no system settings are modified.
            if(message.Msg==0x001A || message.Msg==0x031A) WindowTheme.Apply(Handle);
        }
        internal static Endpoint[] Pair(Profile p,List<Endpoint> list) { return new AudioSnapshot { Devices=list }.Pair(p); }
        static bool StartupEnabled() { try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key!=null && key.GetValue("AudioMe")!=null; } catch { return false; } }
        static void SetStartup(bool enabled) {
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if(enabled) key.SetValue("AudioMe","\""+Application.ExecutablePath+"\" --tray"); else key.DeleteValue("AudioMe",false);
            }
        }
        protected override void Dispose(bool disposing) {
            if(disposing) { timer.Dispose(); tips.Dispose(); tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); brandIcon.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
