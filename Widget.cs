using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AudioSwitch {
    class Widget : Window {
        internal const string Repository="https://github.com/justony5/audio-me";
        internal const string Donate="https://buymeacoffee.com/justony5";
        static readonly string[] RoleNames={"System","Media","Calls"};
        static readonly string[] RoleGlyphs={Glyph.Monitor,Glyph.Media,Glyph.Calls};
        static readonly string[][] ProfileIcons={
            new[] {"game",Glyph.Game},new[] {"work",Glyph.Work},new[] {"music",Glyph.Music},new[] {"movie",Glyph.Movie},
            new[] {"headset",Glyph.Headset},new[] {"call",Glyph.Calls},new[] {"speaker",Glyph.Speaker},new[] {"home",Glyph.Home}};
        readonly SettingsStore store;
        readonly IAudioService service;
        readonly Settings settings;
        readonly Forms.NotifyIcon tray=new Forms.NotifyIcon();
        readonly Forms.ContextMenuStrip trayMenu=new Forms.ContextMenuStrip();
        readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
        readonly DispatcherTimer toastTimer=new DispatcherTimer();
        readonly Drawing.Icon brandIcon;
        readonly List<UIElement> switchControls=new List<UIElement>();
        readonly Border rootBorder=new Border();
        readonly Grid body=new Grid();
        readonly ScrollViewer home=Page(), settingsPage=Page(), editorPage=Page();
        readonly TextBlock pageTitle=Ui.Text("Audio Me",14,FontWeights.SemiBold,"Text");
        readonly Button backButton, pinButton;
        readonly Border recoveryBanner=new Border();
        readonly TextBlock heroLabel=Ui.Label(""), heroName=Ui.Text("",28,FontWeights.SemiBold,"Text"), heroSub=Ui.Text("",13,FontWeights.Normal,"Muted");
        readonly Grid heroPair=new Grid { Margin=new Thickness(0,14,0,0) };
        readonly Ellipse heroGlow=new Ellipse { Width=240,Height=240,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,-110,-80,0),IsHitTestVisible=false };
        readonly ScrollViewer profileScroller=new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false };
        readonly StackPanel profileStrip=new StackPanel { Orientation=Orientation.Horizontal };
        readonly Button playbackTab, micTab, otherToggle;
        readonly StackPanel deviceList=new StackPanel(), otherList=new StackPanel();
        readonly Border toast=new Border();
        readonly TextBlock status=Ui.Text("Reading connected devices…",13,FontWeights.Normal,"Text"), toastIcon=Ui.Icon(Glyph.Check,11,"AccentText");
        readonly Border toastDot=new Border { Width=22,Height=22,CornerRadius=new CornerRadius(11),Margin=new Thickness(0,0,10,0) };
        readonly TextBox profileName=new TextBox();
        readonly TextBlock editorTitle=Ui.Text("",22,FontWeights.SemiBold,"Text"), editorMessage=Ui.Wrap("",12.5,"Warn");
        readonly WrapPanel editorProfiles=new WrapPanel(), editorIcons=new WrapPanel();
        readonly StackPanel outputChoices=new StackPanel(), inputChoices=new StackPanel();
        readonly Button deleteButton;
        readonly CheckBox pinToggle=new CheckBox(), startupToggle=new CheckBox();
        readonly StackPanel accentChoices=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0) };
        Flyout flyout;
        AudioSnapshot snapshot=new AudioSnapshot();
        Endpoint editOutput, editInput;
        string editIcon, expandedDevice, aliasDevice;
        bool exiting, busy, reading, otherExpanded, traySwitch, resourcesDisposed, glass;
        int tab;
        Profile editing;
        string fingerprint="";
        Exception lastRenderError;

        public Widget(bool hidden) : this(hidden,new WindowsAudioService(),new SettingsStore(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AudioSwitch","settings.xml"))) { }
        internal Widget(bool hidden,IAudioService audioService,SettingsStore settingsStore) {
            Theme.EnsureApp();
            service=audioService; store=settingsStore; settings=store.Load();
            Theme.ApplyAccent(settings.Accent);
            Title="Audio Me"; Width=480; Height=840; MinWidth=400; MinHeight=520;
            WindowStartupLocation=WindowStartupLocation.CenterScreen; UseLayoutRounding=true;
            SetResourceReference(FontFamilyProperty,"UiFont"); SetResourceReference(ForegroundProperty,"Text"); FontSize=14;
            Background=Theme.Surface(false); Topmost=settings.Pinned; Icon=Ui.Logo();
            WindowChrome.SetWindowChrome(this,new WindowChrome { CaptionHeight=48,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false,CornerRadius=new CornerRadius(0) });
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("AudioMe.ico")) brandIcon=new Drawing.Icon(stream);
            if(settings.WindowWidth>0 && settings.WindowHeight>0) {
                var area=SystemParameters.WorkArea;
                Width=Math.Min(area.Width,Math.Max(MinWidth,settings.WindowWidth)); Height=Math.Min(area.Height,Math.Max(MinHeight,settings.WindowHeight));
            }

            backButton=Ui.IconButton(Glyph.Back,"Back",32); pinButton=Ui.IconButton(Glyph.Pin,"Keep on top",32);
            var root=new Grid(); root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(52) }); root.RowDefinitions.Add(new RowDefinition());
            root.Children.Add(BuildTitleBar()); Grid.SetRow(body,1); root.Children.Add(body);
            rootBorder.Child=root; Content=rootBorder;
            body.Children.Add(home); body.Children.Add(settingsPage); body.Children.Add(editorPage); body.Children.Add(BuildToast());

            playbackTab=SegmentButton(Glyph.Speaker,"Playback",0); micTab=SegmentButton(Glyph.Mic,"Microphones",1);
            otherToggle=Ui.Button(null,"Other devices",10); otherToggle.Padding=new Thickness(12,10,12,8);
            otherToggle.Click+=delegate { otherExpanded=!otherExpanded; RenderDevices(); };
            deleteButton=Ui.Pill("Delete",Glyph.Delete,false);
            home.Content=BuildHome(); settingsPage.Content=BuildSettings(); editorPage.Content=BuildEditor();
            // Room below the last item so the status toast never covers it.
            foreach(var page in new[] { home,settingsPage,editorPage }) ((FrameworkElement)page.Content).Margin=new Thickness(0,0,0,84);
            ShowPage(home,false);

            tray.Icon=brandIcon; tray.Text="Audio Me";
            trayMenu.ShowItemToolTips=true; trayMenu.Renderer=new Forms.ToolStripProfessionalRenderer(new MenuColors());
            trayMenu.BackColor=Drawing.Color.FromArgb(38,38,42); trayMenu.ForeColor=Drawing.Color.WhiteSmoke;
            trayMenu.Opening+=delegate { BuildTrayMenu(); RefreshDevices(); };
            tray.ContextMenuStrip=trayMenu;
            tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e) { if(e.Button==Forms.MouseButtons.Left) ToggleFlyout(); };
            tray.Visible=true;

            SourceInitialized+=delegate {
                ApplySurface();
                var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if(source!=null) source.AddHook(WndProc);
            };
            Closing+=delegate(object sender,CancelEventArgs e) {
                RememberSize(); Save();
                if(!exiting) { e.Cancel=true; Hide(); }
            };
            Closed+=delegate { DisposeResources(); };
            IsVisibleChanged+=delegate { if(!IsVisible) { RememberSize(); Save(); } };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape && !home.IsVisible) { ShowPage(home,true); e.Handled=true; } };
            toastTimer.Tick+=delegate { toastTimer.Stop(); HideToast(); };
            timer.Tick+=delegate { RefreshDevices(); };
            new WindowInteropHelper(this).EnsureHandle();
            // Saved profiles must remain visible even if initial device discovery fails.
            Render(); RefreshDevices(); timer.Start();
        }

        static ScrollViewer Page() {
            return new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(20,2,12,0),Focusable=false };
        }
        UIElement BuildTitleBar() {
            var bar=new Grid { Margin=new Thickness(12,0,8,0) };
            foreach(var width in new[] { GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto })
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width=width });
            backButton.Visibility=Visibility.Collapsed; backButton.Margin=new Thickness(0,0,6,0); backButton.Click+=delegate { ShowPage(home,true); };
            var logo=new Image { Source=Ui.Logo(),Width=20,Height=20,Margin=new Thickness(2,0,10,0) };
            RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality);
            var version=Ui.Text(VersionText+" beta",12,FontWeights.Normal,"Muted"); version.Margin=new Thickness(8,1,0,0);
            pinButton.Click+=delegate { SetPinned(!settings.Pinned); };
            var gear=Ui.IconButton(Glyph.Settings,"Settings",32); gear.Click+=delegate { ShowPage(settingsPage.IsVisible?home:settingsPage,true); };
            var minimize=Ui.IconButton(Glyph.Minimize,"Minimize",36); minimize.Click+=delegate { WindowState=WindowState.Minimized; };
            var close=Ui.IconButton(Glyph.Close,"Close to tray",36); close.Click+=delegate { Hide(); };
            minimize.Margin=new Thickness(8,0,0,0); gear.Margin=new Thickness(4,0,0,0);
            var items=new UIElement[] { backButton,logo,pageTitle,version,null,pinButton,gear,minimize,close };
            for(int i=0;i<items.Length;i++) {
                if(items[i]==null) continue; Grid.SetColumn(items[i],i); bar.Children.Add(items[i]);
                if(items[i] is Button) WindowChrome.SetIsHitTestVisibleInChrome(items[i],true);
            }
            UpdatePinButton(); return bar;
        }
        internal static string VersionText { get { var v=typeof(Program).Assembly.GetName().Version; return v.Major+"."+v.Minor+"."+v.Build; } }

        UIElement BuildHome() {
            var stack=new StackPanel();
            recoveryBanner.CornerRadius=new CornerRadius(14); recoveryBanner.Padding=new Thickness(14); recoveryBanner.Margin=new Thickness(0,0,0,12);
            recoveryBanner.Background=new SolidColorBrush(Color.FromArgb(0x2E,0xFF,0xC2,0x7A)); recoveryBanner.BorderThickness=new Thickness(1); recoveryBanner.SetResourceReference(Border.BorderBrushProperty,"Warn");
            var recoveryStack=new StackPanel(); recoveryStack.Children.Add(Ui.Wrap(store.RecoveryMessage??"",13,"Warn"));
            var reset=Ui.Pill("Back up original and reset",null,false); reset.HorizontalAlignment=HorizontalAlignment.Left; reset.Margin=new Thickness(0,10,0,0);
            reset.Click+=delegate {
                try { store.BackupAndReset(); Save(); recoveryBanner.Visibility=Visibility.Collapsed; SetStatus("Original settings backed up to "+store.BackupPath,1); }
                catch(Exception e) { SetStatus("Recovery failed; original retained. "+e.Message,2); }
            };
            recoveryStack.Children.Add(reset); recoveryBanner.Child=recoveryStack;
            recoveryBanner.Visibility=store.RecoveryRequired?Visibility.Visible:Visibility.Collapsed; stack.Children.Add(recoveryBanner);

            var heroGrid=new Grid(); heroGrid.Children.Add(heroGlow);
            var heroStack=new StackPanel(); heroName.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont"); heroName.Margin=new Thickness(0,6,0,2);
            heroStack.Children.Add(heroLabel); heroStack.Children.Add(heroName); heroStack.Children.Add(heroSub); heroStack.Children.Add(heroPair);
            heroPair.ColumnDefinitions.Add(new ColumnDefinition()); heroPair.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(10) }); heroPair.ColumnDefinitions.Add(new ColumnDefinition());
            heroGrid.Children.Add(heroStack);
            var hero=Ui.Card(heroGrid,new Thickness(18)); hero.CornerRadius=new CornerRadius(18);
            // Clip the glow to the card's rounded shape rather than its rectangle.
            hero.SizeChanged+=delegate { hero.Clip=new RectangleGeometry(new Rect(0,0,hero.ActualWidth,hero.ActualHeight),18,18); };
            stack.Children.Add(hero);

            var header=new Grid { Margin=new Thickness(4,22,0,10) };
            header.Children.Add(Ui.Label("Profiles"));
            var edit=Ui.Button(Ui.Text("Edit",12.5,FontWeights.SemiBold,"Accent"),"Edit profiles",8); edit.Padding=new Thickness(8,3,8,3);
            edit.HorizontalAlignment=HorizontalAlignment.Right; edit.Click+=delegate { BeginEdit(settings.Profiles.FirstOrDefault()); };
            header.Children.Add(edit); stack.Children.Add(header);
            profileScroller.Content=profileStrip; profileScroller.SizeChanged+=delegate { SizeTiles(profileScroller,profileStrip,settings.Profiles.Count,4); };
            SmoothWheel(profileScroller); stack.Children.Add(profileScroller);

            var segment=new Grid(); segment.ColumnDefinitions.Add(new ColumnDefinition()); segment.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(micTab,1); segment.Children.Add(playbackTab); segment.Children.Add(micTab);
            var segmentBorder=new Border { Child=segment,Padding=new Thickness(4),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,22,0,12),BorderThickness=new Thickness(1) };
            segmentBorder.SetResourceReference(Border.BackgroundProperty,"Inset"); segmentBorder.SetResourceReference(Border.BorderBrushProperty,"CardBorder");
            stack.Children.Add(segmentBorder);

            var listStack=new StackPanel(); listStack.Children.Add(deviceList); listStack.Children.Add(otherToggle); listStack.Children.Add(otherList);
            stack.Children.Add(Ui.Card(listStack,new Thickness(6)));
            return stack;
        }
        Button SegmentButton(string glyph,string text,int index) {
            var row=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center };
            var icon=Ui.Icon(glyph,14,"Text"); icon.Margin=new Thickness(0,0,8,0); row.Children.Add(icon); row.Children.Add(Ui.Text(text,13,FontWeights.SemiBold,"Text"));
            var b=Ui.Button(row,"Show "+text.ToLowerInvariant(),9); b.Padding=new Thickness(8,8,8,8);
            b.Click+=delegate { if(tab==index) return; tab=index; expandedDevice=null; aliasDevice=null; RenderDevices(); Ui.FadeIn(deviceList,6); };
            return b;
        }
        UIElement BuildToast() {
            toast.VerticalAlignment=VerticalAlignment.Bottom; toast.Margin=new Thickness(20,0,20,16); toast.CornerRadius=new CornerRadius(14);
            toast.Padding=new Thickness(12,10,14,10); toast.BorderThickness=new Thickness(1); toast.Opacity=0; toast.IsHitTestVisible=false;
            toast.Background=new SolidColorBrush(Color.FromArgb(0xE6,0x1C,0x1F,0x2A)); toast.SetResourceReference(Border.BorderBrushProperty,"AccentLine");
            toast.Effect=new DropShadowEffect { BlurRadius=24,ShadowDepth=6,Opacity=0.35,Color=Colors.Black };
            var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            toastDot.Child=toastIcon; grid.Children.Add(toastDot);
            status.TextWrapping=TextWrapping.Wrap; status.TextTrimming=TextTrimming.None; Grid.SetColumn(status,1); grid.Children.Add(status);
            AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);
            toast.Child=grid; return toast;
        }

        UIElement BuildSettings() {
            var stack=new StackPanel();
            stack.Children.Add(Section("Appearance"));
            var accent=new StackPanel(); accent.Children.Add(Ui.Text("Accent color",14,FontWeights.SemiBold,"Text"));
            accent.Children.Add(Ui.Wrap("Use the Audio Me teal, or follow the accent color chosen in Windows Settings → Personalization → Colors.",12.5,"Muted"));
            accent.Children.Add(accentChoices); stack.Children.Add(Ui.Card(accent,new Thickness(16)));
            UpdateAccentChoices();

            stack.Children.Add(Section("Window"));
            var window=new StackPanel();
            ConfigureToggle(pinToggle,"Keep on top","Stay above other windows.");
            pinToggle.IsChecked=settings.Pinned; pinToggle.Click+=delegate { SetPinned(pinToggle.IsChecked==true); };
            ConfigureToggle(startupToggle,"Start with Windows","Starts hidden in the tray for this user. Re-enable it if you move the EXE.");
            startupToggle.IsChecked=StartupEnabled(); startupToggle.Click+=delegate {
                try { SetStartup(startupToggle.IsChecked==true); SetStatus("Windows startup preference updated for this user.",1); }
                catch(Exception e) { SetStatus("Startup preference could not be changed: "+e.Message,2); startupToggle.IsChecked=StartupEnabled(); }
            };
            window.Children.Add(pinToggle); window.Children.Add(Divider()); window.Children.Add(startupToggle);
            stack.Children.Add(Ui.Card(window,new Thickness(16,12,16,12)));

            stack.Children.Add(Section("Coming in Premium"));
            var premium=new StackPanel();
            premium.Children.Add(Planned(Glyph.Keyboard,"Keyboard shortcuts","Switch devices or profiles from anywhere."));
            premium.Children.Add(Divider());
            premium.Children.Add(Planned(Glyph.Apps,"Switch when an app opens","Activate a profile when a game or call app starts."));
            premium.Children.Add(Divider());
            premium.Children.Add(Planned(Glyph.Speaker,"Play through two devices","Mirror audio to headphones and speakers together."));
            stack.Children.Add(Ui.Card(premium,new Thickness(16,12,16,12)));
            var free=Ui.Wrap("Manual switching, profiles, favorites, the tray flyout and accessibility stay free.",12.5,"Muted"); free.Margin=new Thickness(4,8,4,0);
            stack.Children.Add(free);

            stack.Children.Add(Section("About"));
            var about=new StackPanel();
            var identity=new Grid(); identity.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); identity.ColumnDefinitions.Add(new ColumnDefinition());
            var logo=new Image { Source=Ui.Logo(),Width=44,Height=44,Margin=new Thickness(0,0,14,0) }; RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality);
            identity.Children.Add(logo);
            var names=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            names.Children.Add(Ui.Text("Audio Me",16,FontWeights.SemiBold,"Text"));
            var versionLine=Ui.Text("Version "+typeof(Program).Assembly.GetName().Version+" · Windows x64 · Public beta",12.5,FontWeights.Normal,"Muted"); versionLine.Name="AboutVersion";
            names.Children.Add(versionLine); names.Children.Add(Ui.Text("Developer: Justin Miller",12.5,FontWeights.Normal,"Muted"));
            Grid.SetColumn(names,1); identity.Children.Add(names); about.Children.Add(identity);
            var notes=Ui.Wrap("Closing the window hides Audio Me to the tray; left-click the tray icon for quick switching. Apps that pick their own audio device can ignore Windows defaults. Builds are unsigned, so Windows SmartScreen may ask before the first run.",12.5,"Muted");
            notes.Margin=new Thickness(0,12,0,12); about.Children.Add(notes);
            var links=new WrapPanel();
            var coffee=Ui.Pill("Buy me a coffee",Glyph.Heart,true); coffee.Click+=delegate { OpenLink(Donate); };
            var github=Ui.Pill("GitHub",Glyph.Link,false); github.Click+=delegate { OpenLink(Repository); };
            coffee.Margin=new Thickness(0,0,8,8); github.Margin=new Thickness(0,0,8,8); links.Children.Add(coffee); links.Children.Add(github);
            about.Children.Add(links);
            stack.Children.Add(Ui.Card(about,new Thickness(16)));

            var actions=new WrapPanel { Margin=new Thickness(0,16,0,0) };
            var hide=Ui.Pill("Hide to tray",Glyph.Hide,false); hide.Click+=delegate { Hide(); };
            var exit=Ui.Pill("Exit Audio Me",Glyph.Power,false); exit.Click+=delegate { Exit(); };
            hide.Margin=new Thickness(0,0,8,8); actions.Children.Add(hide); actions.Children.Add(exit);
            stack.Children.Add(actions);
            return stack;
        }
        static TextBlock Section(string text) { var l=Ui.Label(text); l.Margin=new Thickness(4,20,0,10); return l; }
        static Border Divider() { var d=new Border { Height=1,Margin=new Thickness(0,12,0,12) }; d.SetResourceReference(Border.BackgroundProperty,"CardBorder"); return d; }
        static void ConfigureToggle(CheckBox box,string title,string detail) {
            box.SetResourceReference(FrameworkElement.StyleProperty,"Toggle");
            var text=new StackPanel(); text.Children.Add(Ui.Text(title,14,FontWeights.SemiBold,"Text")); text.Children.Add(Ui.Wrap(detail,12.5,"Muted"));
            box.Content=text; AutomationProperties.SetName(box,title);
        }
        static UIElement Planned(string glyph,string title,string detail) {
            var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var badge=new Border { Width=34,Height=34,CornerRadius=new CornerRadius(10),Child=Ui.Icon(glyph,15,"Muted"),Margin=new Thickness(0,0,12,0) };
            badge.SetResourceReference(Border.BackgroundProperty,"Hover"); grid.Children.Add(badge);
            var text=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; text.Children.Add(Ui.Text(title,14,FontWeights.SemiBold,"Text")); text.Children.Add(Ui.Wrap(detail,12.5,"Muted"));
            Grid.SetColumn(text,1); grid.Children.Add(text);
            var tag=new Border { CornerRadius=new CornerRadius(8),Padding=new Thickness(8,3,8,3),Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center,BorderThickness=new Thickness(1),Child=Ui.Text("Planned",11,FontWeights.SemiBold,"Accent") };
            tag.SetResourceReference(Border.BorderBrushProperty,"AccentLine"); Grid.SetColumn(tag,2); grid.Children.Add(tag);
            AutomationProperties.SetName(grid,title+", planned premium feature, not available in this build");
            Ui.Tip(grid,"Not available in this build."); return grid;
        }
        void UpdateAccentChoices() {
            accentChoices.Children.Clear();
            var windows=Theme.WindowsAccent();
            accentChoices.Children.Add(AccentChoice("Audio Me teal",Theme.TealMode,Theme.Teal));
            accentChoices.Children.Add(AccentChoice("Match Windows",Theme.WindowsMode,Theme.Readable(windows.HasValue?windows.Value:Theme.Teal)));
        }
        Button AccentChoice(string text,string mode,Color swatch) {
            bool selected=(settings.Accent==Theme.WindowsMode)==(mode==Theme.WindowsMode);
            var row=new StackPanel { Orientation=Orientation.Horizontal };
            row.Children.Add(new Ellipse { Width=14,Height=14,Fill=new SolidColorBrush(swatch),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center });
            row.Children.Add(Ui.Text(text,13,FontWeights.SemiBold,"Text"));
            if(selected) { var check=Ui.Icon(Glyph.Check,11,"Accent"); check.Margin=new Thickness(8,0,0,0); row.Children.Add(check); }
            var b=Ui.Button(row,text+(selected?", selected":""),10); b.Padding=new Thickness(12,8,12,8); b.Margin=new Thickness(0,0,8,0); b.BorderThickness=new Thickness(1);
            b.SetResourceReference(Control.BackgroundProperty,selected?"Selected":"Inset"); b.SetResourceReference(Control.BorderBrushProperty,selected?"AccentLine":"CardBorder");
            b.Click+=delegate { settings.Accent=mode; Save(); Theme.ApplyAccent(settings.Accent); UpdateAccentChoices(); Render(); };
            return b;
        }

        UIElement BuildEditor() {
            var stack=new StackPanel();
            editorTitle.SetResourceReference(TextBlock.FontFamilyProperty,"DisplayFont"); editorTitle.Margin=new Thickness(2,4,0,12); stack.Children.Add(editorTitle);
            stack.Children.Add(editorProfiles);
            stack.Children.Add(Section("Name"));
            profileName.SetResourceReference(FrameworkElement.StyleProperty,"Input"); AutomationProperties.SetName(profileName,"Profile name");
            profileName.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) SaveProfile(); };
            stack.Children.Add(profileName);
            stack.Children.Add(Section("Icon")); stack.Children.Add(editorIcons);
            stack.Children.Add(Section("Playback")); stack.Children.Add(Ui.Card(outputChoices,new Thickness(6)));
            stack.Children.Add(Section("Microphone")); stack.Children.Add(Ui.Card(inputChoices,new Thickness(6)));
            editorMessage.Margin=new Thickness(4,12,4,0); stack.Children.Add(editorMessage);
            var actions=new WrapPanel { Margin=new Thickness(0,16,0,0) };
            var save=Ui.Pill("Save profile",Glyph.Check,true); save.Click+=delegate { SaveProfile(); };
            deleteButton.Click+=delegate { if(editing!=null) { settings.Profiles.Remove(editing); editing=null; Save(); ShowPage(home,true); Render(); SetStatus("Profile deleted.",0); } };
            ((StackPanel)deleteButton.Content).Children.OfType<TextBlock>().ToList().ForEach(t => t.SetResourceReference(TextBlock.ForegroundProperty,"Danger"));
            var cancel=Ui.Pill("Cancel",null,false); cancel.Click+=delegate { ShowPage(home,true); };
            foreach(var b in new[] { save,deleteButton,cancel }) { b.Margin=new Thickness(0,0,8,8); actions.Children.Add(b); }
            stack.Children.Add(actions);
            return stack;
        }
        void ShowPage(ScrollViewer page,bool animate) {
            foreach(var p in new[] { home,settingsPage,editorPage }) p.Visibility=p==page?Visibility.Visible:Visibility.Collapsed;
            backButton.Visibility=page==home?Visibility.Collapsed:Visibility.Visible;
            pageTitle.Text=page==settingsPage?"Settings":page==editorPage?"Profiles":"Audio Me";
            if(page==settingsPage) { pinToggle.IsChecked=settings.Pinned; startupToggle.IsChecked=StartupEnabled(); UpdateAccentChoices(); }
            page.ScrollToTop();
            if(animate) Ui.FadeIn((UIElement)page.Content,10);
        }

        internal void Render() {
            switchControls.Clear();
            try {
                RenderHero(); RenderProfiles(); RenderDevices();
                if(flyout!=null && flyout.IsVisible) flyout.Render();
                UpdateTrayProfileStates();
            } catch(Exception e) { lastRenderError=e; fingerprint=""; SetStatus("Could not update device view: "+e.Message,2); }
        }
        internal Profile[] ActiveProfiles() { return settings.Profiles.Where(snapshot.IsActive).ToArray(); }
        void RenderHero() {
            var active=ActiveProfiles();
            heroLabel.Text=active.Length>0?"ACTIVE PROFILE":"CURRENT SETUP";
            heroName.Text=active.Length>0?String.Join(", ",active.Select(p=>p.Name).ToArray()):"Custom setup";
            heroSub.Text=active.Length>0?"All six defaults verified · System, Media & Calls":
                settings.Profiles.Count==0?"Create a profile to switch speaker and microphone together.":"No saved profile matches the current Windows defaults.";
            Ui.Tip(heroName,active.Length>0?heroName.Text:"No saved profile matches the current Windows defaults.");
            var glow=new RadialGradientBrush(); glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x55,Theme.Current.R,Theme.Current.G,Theme.Current.B),0)); glow.GradientStops.Add(new GradientStop(Color.FromArgb(0,Theme.Current.R,Theme.Current.G,Theme.Current.B),1));
            heroGlow.Fill=glow;
            heroPair.Children.Clear();
            for(int flow=0;flow<2;flow++) {
                var device=Find(snapshot.Defaults[flow][0]);
                string label=flow==0?"Playback":"Microphone";
                var calls=Find(snapshot.Defaults[flow][2]);
                if(calls!=null && device!=null && calls.Id!=device.Id) label+=" · calls on "+settings.DisplayName(calls);
                var cell=PairCell(DeviceGlyph(device,flow),device==null?"None selected":settings.DisplayName(device),label);
                Grid.SetColumn(cell,flow*2); heroPair.Children.Add(cell);
            }
        }
        static Border PairCell(string glyph,string name,string label) {
            var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon=Ui.Icon(glyph,18,"Accent"); icon.Margin=new Thickness(0,0,10,0); grid.Children.Add(icon);
            var text=new StackPanel(); text.Children.Add(Ui.Text(name,13,FontWeights.SemiBold,"Text")); text.Children.Add(Ui.Text(label,11.5,FontWeights.Normal,"Muted"));
            Grid.SetColumn(text,1); grid.Children.Add(text);
            var cell=new Border { Child=grid,CornerRadius=new CornerRadius(12),Padding=new Thickness(12,10,12,10),BorderThickness=new Thickness(1) };
            cell.SetResourceReference(Border.BackgroundProperty,"Inset"); cell.SetResourceReference(Border.BorderBrushProperty,"Card");
            Ui.Tip(cell,name+" · "+label); return cell;
        }
        void RenderProfiles() {
            profileStrip.Children.Clear();
            foreach(var p in settings.Profiles) {
                var tile=ProfileTile(p,92);
                var profile=p; tile.MouseRightButtonUp+=delegate { BeginEdit(profile); };
                profileStrip.Children.Add(tile);
            }
            profileStrip.Children.Add(NewTile());
            SizeTiles(profileScroller,profileStrip,settings.Profiles.Count,4);
        }
        internal Button ProfileTile(Profile p,double height) {
            string missing=snapshot.Missing(p); bool active=snapshot.IsActive(p);
            var grid=new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            var badge=new Border { Width=30,Height=30,CornerRadius=new CornerRadius(10),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=Ui.Icon(ProfileGlyph(p),15,active?"AccentText":"Text") };
            badge.SetResourceReference(Border.BackgroundProperty,active?"Accent":"Hover"); grid.Children.Add(badge);
            var name=Ui.Text(p.Name,13,FontWeights.SemiBold,"Text"); Grid.SetRow(name,1); grid.Children.Add(name);
            if(active) {
                var dot=new Ellipse { Width=8,Height=8,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,2,0,0),Effect=new DropShadowEffect { Color=Theme.Current,BlurRadius=10,ShadowDepth=0,Opacity=0.9 } };
                dot.SetResourceReference(Shape.FillProperty,"Accent"); grid.Children.Add(dot);
            } else if(missing.Length>0) {
                var warn=Ui.Icon(Glyph.Warn,12,"Warn"); warn.HorizontalAlignment=HorizontalAlignment.Right; warn.VerticalAlignment=VerticalAlignment.Top; grid.Children.Add(warn);
            }
            var tile=Ui.Button(grid,"Activate profile "+p.Name+(active?", active":"")+(missing.Length>0?", unavailable":""),16);
            tile.Height=height; tile.Padding=new Thickness(12); tile.VerticalContentAlignment=VerticalAlignment.Stretch; tile.BorderThickness=new Thickness(1); tile.Tag=p;
            if(active) {
                var fill=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,1) };
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x55,Theme.Current.R,Theme.Current.G,Theme.Current.B),0));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x16,Theme.Current.R,Theme.Current.G,Theme.Current.B),1));
                tile.Background=fill; tile.SetResourceReference(Control.BorderBrushProperty,"AccentLine");
                tile.Effect=new DropShadowEffect { Color=Theme.Current,BlurRadius=22,ShadowDepth=0,Opacity=0.28 };
            } else { tile.SetResourceReference(Control.BackgroundProperty,"Card"); tile.SetResourceReference(Control.BorderBrushProperty,"CardBorder"); }
            tile.IsEnabled=missing.Length==0 && !busy;
            Ui.Tip(tile,missing.Length>0?missing:"Switch playback and microphone to "+p.Name+" for System, Media and Calls.\nRight-click to edit.");
            tile.Click+=delegate { if(flyout!=null && flyout.IsVisible) traySwitch=true; ApplyProfile(p); };
            switchControls.Add(tile); return tile;
        }
        Button NewTile() {
            var grid=new Grid();
            var outline=new Rectangle { RadiusX=16,RadiusY=16,StrokeThickness=1.2,StrokeDashArray=new DoubleCollection(new double[] { 4,3 }) };
            outline.SetResourceReference(Shape.StrokeProperty,"Faint"); grid.Children.Add(outline);
            var text=new StackPanel { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            text.Children.Add(Ui.Icon(Glyph.Add,16,"Muted")); var label=Ui.Text("New",12,FontWeights.Normal,"Muted"); label.Margin=new Thickness(0,6,0,0); label.HorizontalAlignment=HorizontalAlignment.Center; text.Children.Add(label);
            grid.Children.Add(text);
            var tile=Ui.Button(grid,"Create profile",16); tile.Height=92; tile.Padding=new Thickness(0); tile.VerticalContentAlignment=VerticalAlignment.Stretch;
            Ui.Tip(tile,"Create a profile with one playback device and one microphone.");
            tile.Click+=delegate { BeginEdit(null); }; return tile;
        }
        // Up to three profiles share the row evenly; beyond that tiles keep their size and the row scrolls.
        internal static void SizeTiles(ScrollViewer scroller,StackPanel strip,int profiles,int slots) {
            double available=scroller.ActualWidth; if(available<=0 || strip.Children.Count==0) return;
            const double gap=10; bool scrolls=profiles>3;
            double width=scrolls?(available-2*gap)/3.4:(available-(slots-1)*gap)/slots;
            width=Math.Max(84,Math.Floor(width));
            for(int i=0;i<strip.Children.Count;i++) {
                var tile=(FrameworkElement)strip.Children[i]; tile.Width=width;
                tile.Margin=new Thickness(0,0,i==strip.Children.Count-1?0:gap,scrolls?8:0);
            }
        }
        internal static void SmoothWheel(ScrollViewer scroller) {
            double target=0; EventHandler tick=null;
            scroller.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e) {
                if(scroller.ScrollableWidth<=0) return;
                if(tick==null) target=scroller.HorizontalOffset;
                target=Math.Max(0,Math.Min(scroller.ScrollableWidth,target-e.Delta*0.9)); e.Handled=true;
                if(tick!=null) return;
                tick=delegate {
                    double current=scroller.HorizontalOffset, next=current+(target-current)*0.2;
                    if(Math.Abs(target-next)<0.5) { next=target; CompositionTarget.Rendering-=tick; tick=null; }
                    scroller.ScrollToHorizontalOffset(next);
                };
                CompositionTarget.Rendering+=tick;
            };
        }
        internal static string ProfileGlyph(Profile p) {
            string key=p.Icon;
            if(String.IsNullOrEmpty(key)) {
                string n=(p.Name??"").ToLowerInvariant();
                key=Has(n,"game","fps","play")?"game":Has(n,"work","office","meet","zoom","teams")?"work":Has(n,"music","casual","listen","spotify")?"music":
                    Has(n,"movie","film","watch","stream","tv")?"movie":Has(n,"call","chat","discord")?"call":Has(n,"home","family")?"home":"headset";
            }
            var match=ProfileIcons.FirstOrDefault(i=>i[0]==key); return match==null?Glyph.Headset:match[1];
        }
        static bool Has(string text,params string[] words) { return words.Any(w=>text.Contains(w)); }
        internal static string DeviceGlyph(Endpoint d,int flow) {
            if(d==null) return flow==0?Glyph.Speaker:Glyph.Mic;
            string n=(d.Name??"").ToLowerInvariant();
            if(d.Flow==1) return Has(n,"headset")?Glyph.Headset:Glyph.Mic;
            if(Has(n,"headphone","headset","earbud","airpods","buds","arctis")) return Glyph.Headphones;
            if(Has(n,"hdmi","displayport","display audio","monitor","tv","nvidia high definition")) return Glyph.Monitor;
            return Glyph.Speaker;
        }

        void RenderDevices() {
            deviceList.Children.Clear(); otherList.Children.Clear();
            foreach(var t in new[] { playbackTab,micTab }) {
                bool on=(t==playbackTab)==(tab==0);
                if(on) t.SetResourceReference(Control.BackgroundProperty,"Selected"); else t.ClearValue(Control.BackgroundProperty);
                t.Effect=on?new DropShadowEffect { BlurRadius=8,ShadowDepth=1,Opacity=0.25,Color=Colors.Black }:null;
                AutomationProperties.SetHelpText(t,on?"Selected":"");
            }
            var sorted=settings.SortDevices(snapshot.Devices.Where(d=>d.Flow==tab)).ToList();
            var main=sorted.Where(d=>!settings.Deemphasized.Contains(d.Id)).ToList();
            var other=sorted.Where(d=>settings.Deemphasized.Contains(d.Id)).ToList();
            foreach(var d in main) deviceList.Children.Add(DeviceRow(d));
            if(main.Count==0) { var empty=Ui.Wrap("No devices here. Check Other devices or connect hardware.",13,"Muted"); empty.Margin=new Thickness(12); deviceList.Children.Add(empty); }
            var toggle=new StackPanel { Orientation=Orientation.Horizontal };
            var chevron=Ui.Icon(otherExpanded?Glyph.ChevronDown:Glyph.ChevronRight,10,"Muted"); chevron.Margin=new Thickness(2,0,10,0); toggle.Children.Add(chevron);
            toggle.Children.Add(Ui.Text("Other devices · "+other.Count,13,FontWeights.Normal,"Muted"));
            otherToggle.Content=toggle; otherToggle.Visibility=other.Count>0?Visibility.Visible:Visibility.Collapsed;
            AutomationProperties.SetName(otherToggle,(otherExpanded?"Collapse":"Expand")+" other devices, "+other.Count+" connected and less used");
            Ui.Tip(otherToggle,"Connected but less-used devices. Use ⋯ on a device to move it here or back.");
            otherList.Visibility=otherExpanded && other.Count>0?Visibility.Visible:Visibility.Collapsed;
            foreach(var d in other) otherList.Children.Add(DeviceRow(d));
        }
        UIElement DeviceRow(Endpoint d) {
            var roles=Enumerable.Range(0,3).Where(r=>snapshot.Defaults[d.Flow][r]==d.Id).ToArray();
            bool isDefault=roles.Length>0, favorite=settings.Favorites.Contains(d.Id), other=settings.Deemphasized.Contains(d.Id);
            string display=settings.DisplayName(d), roleText=RoleText(roles);
            var content=new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var badge=new Border { Width=36,Height=36,CornerRadius=new CornerRadius(11),Margin=new Thickness(0,0,12,0),Child=Ui.Icon(DeviceGlyph(d,d.Flow),16,isDefault?"AccentText":"Text") };
            badge.SetResourceReference(Border.BackgroundProperty,isDefault?"Accent":"Hover"); content.Children.Add(badge);
            var text=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            text.Children.Add(Ui.Text(display,14,FontWeights.SemiBold,"Text"));
            string sub=isDefault?"Default for "+roleText:"Ready";
            if(display!=d.Name) sub=d.Name+(isDefault?" · default":"");
            text.Children.Add(Ui.Text(sub,12,FontWeights.Normal,"Muted")); Grid.SetColumn(text,1); content.Children.Add(text);
            var chips=new StackPanel { Orientation=Orientation.Horizontal };
            for(int role=0;role<3;role++) chips.Children.Add(RoleChip(role,snapshot.Defaults[d.Flow][role]==d.Id));
            var chipBox=new Border { Child=chips,CornerRadius=new CornerRadius(9),Padding=new Thickness(3),Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center };
            chipBox.SetResourceReference(Border.BackgroundProperty,"Inset"); Grid.SetColumn(chipBox,2); content.Children.Add(chipBox);
            var select=Ui.Button(content,"Switch "+(d.Flow==0?"playback to ":"microphone to ")+display,12); select.Padding=new Thickness(8,8,8,8);
            AutomationProperties.SetHelpText(select,isDefault?"Default for "+roleText:"Not a Windows default"); select.IsEnabled=!busy;
            Ui.Tip(select,d.Name+"\n"+(d.Flow==0?"Playback":"Microphone")+" · "+(isDefault?"Default for "+roleText:"Not a default"));
            select.Click+=delegate { if(flyout!=null && flyout.IsVisible) traySwitch=true; ApplyDevices(new[] { d },display); }; switchControls.Add(select);
            var star=Ui.IconButton(favorite?Glyph.Star:Glyph.StarOutline,(favorite?"Remove favorite ":"Favorite ")+d.Name,32);
            ((TextBlock)star.Content).SetResourceReference(TextBlock.ForegroundProperty,favorite?"Gold":"Faint");
            star.Click+=delegate { settings.ToggleDevice(d.Id,true); Save(); Render(); };
            var more=Ui.IconButton(Glyph.More,"More actions for "+display,32);
            more.Click+=delegate { expandedDevice=expandedDevice==d.Id?null:d.Id; aliasDevice=null; RenderDevices(); };
            var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            row.Children.Add(select); Grid.SetColumn(star,1); Grid.SetColumn(more,2); star.Margin=new Thickness(4,0,0,0); more.Margin=new Thickness(0,0,4,0);
            row.Children.Add(star); row.Children.Add(more);
            var stack=new StackPanel(); stack.Children.Add(row);
            if(expandedDevice==d.Id && aliasDevice!=d.Id) {
                var actions=new WrapPanel { Margin=new Thickness(56,2,8,10) };
                var rename=Ui.Pill("Rename label",Glyph.Edit,false); rename.Click+=delegate { aliasDevice=d.Id; RenderDevices(); };
                var move=Ui.Pill(other?"Move to main list":"Move to Other devices",other?Glyph.Up:Glyph.Down,false);
                move.Click+=delegate { settings.ToggleDevice(d.Id,false); expandedDevice=null; Save(); Render(); };
                AutomationProperties.SetName(rename,"Rename display label for "+d.Name); AutomationProperties.SetName(move,(other?"Move to main list ":"Move to Other devices ")+d.Name);
                rename.Margin=new Thickness(0,0,8,0); actions.Children.Add(rename); actions.Children.Add(move); stack.Children.Add(actions); Ui.FadeIn(actions,-4);
            }
            if(aliasDevice==d.Id) stack.Children.Add(AliasEditor(d));
            var container=new Border { Child=stack,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Margin=new Thickness(0,1,0,1),Tag=d };
            if(isDefault) { container.SetResourceReference(Border.BackgroundProperty,"AccentSoft"); container.SetResourceReference(Border.BorderBrushProperty,"AccentLine"); }
            else container.BorderBrush=Brushes.Transparent;
            return container;
        }
        static string RoleText(int[] roles) {
            if(roles.Length==3) return "System, Media & Calls";
            return String.Join(" & ",roles.Select(r=>RoleNames[r]).ToArray());
        }
        // Noninteractive indicators: role state has accessible text as well as color.
        static Border RoleChip(int role,bool active) {
            var chip=new Border { Width=24,Height=20,CornerRadius=new CornerRadius(6),Child=Ui.Icon(RoleGlyphs[role],11,active?"Accent":"Faint"),Tag=new[] { role,active?1:0 },Focusable=false };
            if(active) chip.SetResourceReference(Border.BackgroundProperty,"AccentSoft"); else chip.Background=Brushes.Transparent;
            string name=RoleNames[role]+": "+(active?"active default":"not default");
            AutomationProperties.SetName(chip,name); Ui.Tip(chip,name); return chip;
        }
        UIElement AliasEditor(Endpoint d) {
            var grid=new Grid { Margin=new Thickness(56,2,8,10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var box=new TextBox { Text=settings.DisplayName(d) }; box.SetResourceReference(FrameworkElement.StyleProperty,"Input");
            AutomationProperties.SetName(box,"Local display alias for "+d.Name); grid.Children.Add(box);
            Action save=delegate {
                // Aliases are local labels; the hardware name and endpoint reference never change.
                settings.Aliases.RemoveAll(a=>a.Id==d.Id);
                if(!String.IsNullOrWhiteSpace(box.Text) && box.Text.Trim()!=d.Name) settings.Aliases.Add(new DeviceAlias { Id=d.Id,Name=box.Text.Trim() });
                aliasDevice=null; expandedDevice=null; Save(); Render();
            };
            var ok=Ui.Pill("Save",null,true); ok.Margin=new Thickness(8,0,0,0); ok.Click+=delegate { save(); };
            var cancel=Ui.Pill("Cancel",null,false); cancel.Margin=new Thickness(8,0,0,0); cancel.Click+=delegate { aliasDevice=null; RenderDevices(); };
            box.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter) save(); if(e.Key==Key.Escape) { aliasDevice=null; RenderDevices(); e.Handled=true; } };
            Grid.SetColumn(ok,1); Grid.SetColumn(cancel,2); grid.Children.Add(ok); grid.Children.Add(cancel);
            box.Loaded+=delegate { box.Focus(); box.SelectAll(); };
            return grid;
        }
        internal Endpoint Find(string id) { return id==null?null:snapshot.Devices.FirstOrDefault(x=>x.Id==id); }
        internal AudioSnapshot Snapshot { get { return snapshot; } }
        internal Settings Preferences { get { return settings; } }
        internal bool Busy { get { return busy; } }
        internal string StatusText { get { return status.Text; } }
        internal void Register(UIElement control) { switchControls.Add(control); }

        void PostToUi(Action action) {
            if(Dispatcher.HasShutdownStarted || resourcesDisposed) return;
            Dispatcher.BeginInvoke(action);
        }
        void RefreshDevices() {
            if(reading || busy || resourcesDisposed) return; reading=true;
            Task.Factory.StartNew(()=>service.Read(),CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default).ContinueWith(task=>{
                var observed=task.Exception;
                PostToUi(delegate {
                    reading=false; if(resourcesDisposed) return;
                    if(task.IsFaulted) { SetStatus("Could not read audio devices: "+task.Exception.GetBaseException().Message,2); return; }
                    if(busy) return;
                    snapshot=task.Result;
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
        internal void ApplyProfile(Profile p) {
            string missing=snapshot.Missing(p); if(missing.Length>0) { SetStatus(missing,2); return; }
            ApplyDevices(snapshot.Pair(p),p.Name);
        }
        internal void ApplyDevices(Endpoint[] devices,string label) {
            if(busy || devices.Length==0) return; busy=true; SetStatus("Switching to "+label+" and verifying System, Media and Calls…",3);
            bool notify=traySwitch; traySwitch=false; UpdateTrayProfileStates();
            foreach(var c in switchControls) c.IsEnabled=false;
            Task.Factory.StartNew(()=>service.Apply(devices),CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default).ContinueWith(task=>{
                var observed=task.Exception;
                PostToUi(delegate {
                    busy=false; if(resourcesDisposed) return;
                    if(task.IsFaulted) SetStatus("Switch failed: "+task.Exception.GetBaseException().Message,2);
                    else SetStatus("Switched to "+label+". Verified: System, Media and Calls updated.",1);
                    if(notify && !IsVisible && (flyout==null || !flyout.IsVisible)) {
                        tray.BalloonTipTitle=task.IsFaulted?"Audio Me · switch failed":"Audio Me · "+label;
                        tray.BalloonTipText=status.Text; tray.BalloonTipIcon=task.IsFaulted?Forms.ToolTipIcon.Warning:Forms.ToolTipIcon.Info; tray.ShowBalloonTip(3000);
                    }
                    Render(); fingerprint=""; RefreshDevices();
                });
            },TaskScheduler.Default);
        }
        // kind: 0 info, 1 success, 2 problem, 3 in progress (stays until replaced).
        internal void SetStatus(string text,int kind) {
            status.Text=text; toastTimer.Stop();
            toastIcon.Text=kind==2?"!":kind==3?Glyph.More:kind==1?Glyph.Check:"i";
            toastIcon.SetResourceReference(TextBlock.FontFamilyProperty,kind==2 || kind==0?"UiFont":"Icons");
            toastIcon.FontWeight=FontWeights.Bold; toastIcon.Foreground=kind==2?new SolidColorBrush(Color.FromRgb(0x2A,0x08,0x08)):null;
            if(kind!=2) toastIcon.SetResourceReference(TextBlock.ForegroundProperty,"AccentText");
            toastDot.SetResourceReference(Border.BackgroundProperty,kind==2?"Danger":"Accent");
            toast.SetResourceReference(Border.BorderBrushProperty,kind==2?"Danger":"AccentLine");
            if(toast.Opacity<0.5) Ui.FadeIn(toast,14); else toast.BeginAnimation(OpacityProperty,null);
            toast.Opacity=1;
            if(kind!=3) { toastTimer.Interval=TimeSpan.FromSeconds(kind==2?9:4); toastTimer.Start(); }
            if(flyout!=null && flyout.IsVisible) flyout.UpdateStatus();
        }
        void HideToast() {
            var fade=new System.Windows.Media.Animation.DoubleAnimation(0,TimeSpan.FromMilliseconds(260));
            toast.BeginAnimation(OpacityProperty,fade);
        }

        void BuildTrayMenu() {
            foreach(Forms.ToolStripItem item in trayMenu.Items.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
            trayMenu.Items.Clear();
            trayMenu.Items.Add("Show Audio Me",null,delegate { ShowWidget(); });
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add(new Forms.ToolStripMenuItem("Profiles") { Enabled=false });
            foreach(var p in settings.Profiles) {
                var profile=p;
                // Text is assigned literally: profile names may contain an ampersand.
                var item=new Forms.ToolStripMenuItem { Text=p.Name,Tag=p };
                item.Click+=delegate {
                    if(busy || !settings.Profiles.Contains(profile)) return;
                    string missing=snapshot.Missing(profile);
                    if(missing.Length>0) { SetStatus(missing,2); return; }
                    traySwitch=true; ApplyProfile(profile);
                };
                trayMenu.Items.Add(item);
            }
            if(settings.Profiles.Count==0) trayMenu.Items.Add("Create a profile…",null,delegate { ShowWidget(); BeginEdit(null); });
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            var exit=new Forms.ToolStripMenuItem("Exit",null,delegate { Exit(); });
            exit.Name="Exit"; trayMenu.Items.Add(exit);
            foreach(Forms.ToolStripItem item in trayMenu.Items) item.ForeColor=Drawing.Color.WhiteSmoke;
            UpdateTrayProfileStates();
        }
        void UpdateTrayProfileStates() {
            foreach(var item in trayMenu.Items.OfType<Forms.ToolStripMenuItem>()) {
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
        internal void ToggleFlyout() {
            if(flyout==null) flyout=new Flyout(this);
            if(flyout.IsVisible) { flyout.HideFlyout(); return; }
            // A click on the tray icon first deactivates an open flyout; don't reopen it immediately.
            if((DateTime.UtcNow-flyout.HiddenAt).TotalMilliseconds<350) return;
            RefreshDevices(); flyout.ShowNearTray();
        }

        void BeginEditPage() { ShowPage(editorPage,true); }
        internal void BeginEdit(Profile p) {
            editing=p; editIcon=p==null?null:p.Icon;
            editorTitle.Text=p==null?"New profile":"Edit profile";
            profileName.Text=p==null?"":p.Name;
            editOutput=p==null?null:(Find(p.Output)??new Endpoint { Id=p.Output,Flow=0,Name=p.OutputName??p.Output });
            editInput=p==null?null:(Find(p.Input)??new Endpoint { Id=p.Input,Flow=1,Name=p.InputName??p.Input });
            bool offline=p!=null && snapshot.Missing(p).Length>0;
            editorMessage.Text=offline?"Disconnected selections are retained; replacements must be chosen explicitly.":"A profile sets one playback device and one microphone for System, Media and Calls.";
            editorMessage.SetResourceReference(TextBlock.ForegroundProperty,offline?"Warn":"Muted");
            deleteButton.Visibility=p==null?Visibility.Collapsed:Visibility.Visible;
            RenderEditor();
            if(!IsVisible) ShowWidget();
            if(!editorPage.IsVisible) BeginEditPage();
            Dispatcher.BeginInvoke(new Action(delegate { profileName.Focus(); }),DispatcherPriority.Input);
        }
        void RenderEditor() {
            editorProfiles.Children.Clear();
            foreach(var p in settings.Profiles.Concat(new Profile[] { null })) {
                var profile=p; bool selected=p==editing;
                var chip=Ui.Pill(p==null?"New":p.Name,p==null?Glyph.Add:ProfileGlyph(p),false);
                chip.Margin=new Thickness(0,0,8,8); chip.BorderThickness=new Thickness(1);
                chip.SetResourceReference(Control.BackgroundProperty,selected?"AccentSoft":"Card"); chip.SetResourceReference(Control.BorderBrushProperty,selected?"AccentLine":"CardBorder");
                AutomationProperties.SetName(chip,(p==null?"New profile":"Edit "+p.Name)+(selected?", selected":""));
                chip.Click+=delegate { BeginEdit(profile); };
                editorProfiles.Children.Add(chip);
            }
            editorIcons.Children.Clear();
            string current=editIcon??ProfileIcons.First(i=>i[1]==ProfileGlyph(new Profile { Name=profileName.Text }))[0];
            foreach(var icon in ProfileIcons) {
                var key=icon[0]; bool selected=key==current;
                var b=Ui.Button(Ui.Icon(icon[1],16,selected?"Accent":"Text"),"Icon "+key+(selected?", selected":""),10);
                b.Width=44; b.Height=40; b.Padding=new Thickness(0); b.HorizontalContentAlignment=HorizontalAlignment.Center; b.Margin=new Thickness(0,0,8,8); b.BorderThickness=new Thickness(1);
                b.SetResourceReference(Control.BackgroundProperty,selected?"AccentSoft":"Card"); b.SetResourceReference(Control.BorderBrushProperty,selected?"AccentLine":"CardBorder");
                b.Click+=delegate { editIcon=key; RenderEditor(); };
                editorIcons.Children.Add(b);
            }
            FillChoices(outputChoices,0); FillChoices(inputChoices,1);
        }
        void FillChoices(StackPanel panel,int flow) {
            panel.Children.Clear();
            var selected=flow==0?editOutput:editInput;
            var devices=settings.SortDevices(snapshot.Devices.Where(d=>d.Flow==flow)).ToList();
            if(selected!=null && !devices.Any(d=>d.Id==selected.Id)) devices.Add(selected);
            foreach(var d in devices) {
                var device=d; bool on=selected!=null && selected.Id==d.Id, offline=!snapshot.Devices.Any(x=>x.Id==d.Id && x.Flow==flow);
                var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
                var badge=new Border { Width=30,Height=30,CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,12,0),Child=Ui.Icon(offline?Glyph.Warn:DeviceGlyph(d,flow),14,on?"AccentText":offline?"Warn":"Text") };
                badge.SetResourceReference(Border.BackgroundProperty,on?"Accent":"Hover"); grid.Children.Add(badge);
                var text=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
                text.Children.Add(Ui.Text(offline?d.Name:settings.DisplayName(d),13.5,FontWeights.SemiBold,"Text"));
                if(offline) text.Children.Add(Ui.Text("Disconnected · kept until you choose another",11.5,FontWeights.Normal,"Warn"));
                Grid.SetColumn(text,1); grid.Children.Add(text);
                if(on) { var check=Ui.Icon(Glyph.Check,13,"Accent"); Grid.SetColumn(check,2); grid.Children.Add(check); }
                var b=Ui.Button(grid,(flow==0?"Profile playback device ":"Profile microphone ")+d.Name+(on?", selected":""),10); b.Padding=new Thickness(8,7,10,7); b.Tag=d;
                if(on) b.SetResourceReference(Control.BackgroundProperty,"AccentSoft");
                b.Click+=delegate { if(flow==0) editOutput=device; else editInput=device; FillChoices(panel,flow); };
                panel.Children.Add(b);
            }
            if(devices.Count==0) { var empty=Ui.Wrap("No devices found. Connect hardware and try again.",13,"Muted"); empty.Margin=new Thickness(10); panel.Children.Add(empty); }
        }
        void SaveProfile() {
            var output=editOutput; var input=editInput; string name=profileName.Text.Trim();
            if(name.Length==0 || output==null || input==null) { editorMessage.Text="Enter a name and choose both devices. No substitutes are selected automatically."; editorMessage.SetResourceReference(TextBlock.ForegroundProperty,"Warn"); return; }
            if(settings.Profiles.Any(p=>p!=editing && String.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))) { editorMessage.Text="Choose a unique profile name."; editorMessage.SetResourceReference(TextBlock.ForegroundProperty,"Warn"); return; }
            var saved=editing??new Profile(); saved.Name=name; saved.Output=output.Id; saved.Input=input.Id; saved.OutputName=output.Name; saved.InputName=input.Name; saved.Icon=editIcon;
            if(editing==null) settings.Profiles.Add(saved);
            editing=saved;
            if(Save()) { ShowPage(home,true); SetStatus("Profile saved. Select it to switch devices.",1); }
            Render();
        }
        bool Save() {
            if(store.RecoveryRequired) return false;
            try { store.Save(settings); return true; } catch(Exception e) { SetStatus("Settings could not be saved: "+e.Message,2); return false; }
        }
        void SetPinned(bool pinned) { settings.Pinned=pinned; Topmost=pinned; pinToggle.IsChecked=pinned; UpdatePinButton(); Save(); }
        void UpdatePinButton() {
            var icon=(TextBlock)pinButton.Content; icon.Text=settings.Pinned?Glyph.Unpin:Glyph.Pin;
            icon.SetResourceReference(TextBlock.ForegroundProperty,settings.Pinned?"Accent":"Muted");
            if(settings.Pinned) pinButton.SetResourceReference(Control.BackgroundProperty,"AccentSoft"); else pinButton.ClearValue(Control.BackgroundProperty);
            string name=settings.Pinned?"Keep on top is on":"Keep on top is off";
            AutomationProperties.SetName(pinButton,name); Ui.Tip(pinButton,name);
        }
        void RememberSize() {
            var bounds=WindowState==WindowState.Normal?new Rect(Left,Top,ActualWidth,ActualHeight):RestoreBounds;
            if(bounds.Width>0 && bounds.Height>0) { settings.WindowWidth=(int)bounds.Width; settings.WindowHeight=(int)bounds.Height; }
        }
        internal void ShowWidget() {
            if(flyout!=null && flyout.IsVisible) flyout.HideFlyout();
            Show(); if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal; Activate(); RefreshDevices();
        }
        internal void Exit() {
            if(busy) { SetStatus("Wait for switching to finish before exiting.",2); return; }
            exiting=true; Close(); if(Application.Current!=null) Application.Current.Shutdown();
        }
        void ApplySurface() {
            glass=Theme.ApplyBackdrop(this);
            Background=glass?Brushes.Transparent:Theme.Surface(false);
            rootBorder.Background=glass?Theme.Surface(true):null;
        }
        IntPtr WndProc(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
            // Windows broadcasts setting, accent and theme changes; no system settings are modified.
            if(message==0x001A || message==0x0320 || message==0x031A) Dispatcher.BeginInvoke(new Action(OnSystemThemeChanged),DispatcherPriority.Background);
            return IntPtr.Zero;
        }
        void OnSystemThemeChanged() {
            if(resourcesDisposed) return;
            ApplySurface(); if(flyout!=null) flyout.ApplySurface();
            if(settings.Accent==Theme.WindowsMode) Theme.ApplyAccent(settings.Accent);
            if(settingsPage.IsVisible) UpdateAccentChoices();
            Render();
        }
        static void OpenLink(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute=true }); } catch { } }
        internal static Endpoint[] Pair(Profile p,List<Endpoint> list) { return new AudioSnapshot { Devices=list }.Pair(p); }
        static bool StartupEnabled() { try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key!=null && key.GetValue("AudioMe")!=null; } catch { return false; } }
        static void SetStartup(bool enabled) {
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if(enabled) key.SetValue("AudioMe","\""+Forms.Application.ExecutablePath+"\" --tray"); else key.DeleteValue("AudioMe",false);
            }
        }
        void DisposeResources() {
            if(resourcesDisposed) return;
            resourcesDisposed=true; timer.Stop(); toastTimer.Stop(); tray.Visible=false; trayMenu.Dispose(); tray.Dispose(); brandIcon.Dispose();
            if(flyout!=null) flyout.Close();
        }
    }
}
