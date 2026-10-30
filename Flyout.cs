using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Forms = System.Windows.Forms;

namespace AudioSwitch {
    // Quick-switch panel opened by a left click on the tray icon, styled like Windows Quick Settings.
    class Flyout : Window {
        readonly Widget owner;
        readonly Border rootBorder=new Border { Padding=new Thickness(16) };
        readonly StackPanel stack=new StackPanel();
        readonly ScrollViewer scroller=new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false };
        readonly StackPanel strip=new StackPanel { Orientation=Orientation.Horizontal };
        TextBlock status;
        int expanded=-1;
        // Usable screen edges (DIPs) captured when the flyout opens; used to keep it on screen as it grows.
        double workTop, workBottom; bool taskbarOnTop, anchored;
        const double Edge=12;
        internal DateTime HiddenAt=DateTime.MinValue;

        public Flyout(Widget owner) {
            this.owner=owner;
            Title="Audio Me quick switch"; Width=360; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize;
            WindowStyle=WindowStyle.ToolWindow; ShowInTaskbar=false; Topmost=true; UseLayoutRounding=true; ShowActivated=true;
            SetResourceReference(FontFamilyProperty,"UiFont"); SetResourceReference(ForegroundProperty,"Text"); FontSize=14;
            Background=Theme.Surface(false);
            WindowChrome.SetWindowChrome(this,new WindowChrome { CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false,CornerRadius=new CornerRadius(0) });
            rootBorder.Child=stack; Content=rootBorder;
            scroller.Content=strip; scroller.SizeChanged+=delegate { Widget.SizeTiles(scroller,strip,owner.Preferences.Profiles.Count,3); };
            Widget.SmoothWheel(scroller); owner.EditOnRightClick(strip);
            SizeChanged+=delegate { if(anchored) KeepOnScreen(); };
            SourceInitialized+=delegate { ApplySurface(); };
            Deactivated+=delegate { HideFlyout(); };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) { HideFlyout(); e.Handled=true; } };
        }
        internal void ApplySurface() {
            bool glass=Theme.ApplyBackdrop(this);
            Background=glass?Brushes.Transparent:Theme.Surface(false);
            rootBorder.Background=glass?Theme.Surface(true):null;
        }
        internal void Render() {
            stack.Children.Clear(); strip.Children.Clear();
            var snapshot=owner.Snapshot; var settings=owner.Preferences; var active=owner.ActiveProfiles();
            var header=new Grid { Margin=new Thickness(0,0,0,14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var logo=new Image { Source=Ui.Logo(),Width=26,Height=26,Margin=new Thickness(0,0,10,0) }; RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality); header.Children.Add(logo);
            var titles=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            titles.Children.Add(Ui.Text("Audio Me",14,FontWeights.SemiBold,"Text"));
            titles.Children.Add(Ui.Text(active.Length>0?active[0].Name+" is active":"Custom setup",12,FontWeights.Normal,active.Length>0?"Accent":"Muted"));
            Grid.SetColumn(titles,1); header.Children.Add(titles);
            var open=Ui.IconButton(Glyph.Open,"Open Audio Me",32); open.Click+=delegate { owner.ShowWidget(); };
            Grid.SetColumn(open,2); header.Children.Add(open);
            stack.Children.Add(header);

            if(settings.Profiles.Count>0) {
                foreach(var p in settings.Profiles) strip.Children.Add(owner.ProfileTile(p,76));
                stack.Children.Add(scroller); Widget.SizeTiles(scroller,strip,settings.Profiles.Count,3);
            } else {
                var create=Ui.Pill("Create a profile",Glyph.Add,true); create.HorizontalAlignment=HorizontalAlignment.Stretch;
                create.Click+=delegate { owner.BeginEdit(null); }; stack.Children.Add(create);
            }
            for(int flow=0;flow<2;flow++) stack.Children.Add(Picker(flow,snapshot,settings));

            var footer=new Grid { Margin=new Thickness(2,14,0,0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            // Rebuilt each render: the previous footer still owns the old status text.
            status=Ui.Wrap("",12,"Muted"); status.VerticalAlignment=VerticalAlignment.Center;
            footer.Children.Add(status); UpdateStatus();
            var more=Ui.Button(Ui.Text("Open Audio Me  ›",12,FontWeights.SemiBold,"Accent"),"Open Audio Me",8); more.Padding=new Thickness(8,3,8,3);
            more.VerticalAlignment=VerticalAlignment.Center; more.Click+=delegate { owner.ShowWidget(); };
            Grid.SetColumn(more,1); footer.Children.Add(more);
            stack.Children.Add(footer);
        }
        internal void UpdateStatus() { if(status!=null) status.Text=owner.StatusText; }
        UIElement Picker(int flow,AudioSnapshot snapshot,Settings settings) {
            var panel=new StackPanel { Margin=new Thickness(0,12,0,0) };
            var current=owner.Find(snapshot.Defaults[flow][0]);
            var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var icon=Ui.Icon(flow==0?Glyph.Speaker:Glyph.Mic,17,"Accent"); icon.Margin=new Thickness(2,0,12,0); grid.Children.Add(icon);
            var text=new StackPanel();
            text.Children.Add(Ui.Text(current==null?"None selected":settings.DisplayName(current),13.5,FontWeights.SemiBold,"Text"));
            text.Children.Add(Ui.Text(flow==0?"Playback":"Microphone",11.5,FontWeights.Normal,"Muted"));
            Grid.SetColumn(text,1); grid.Children.Add(text);
            var chevron=Ui.Icon(expanded==flow?Glyph.ChevronDown:Glyph.ChevronRight,10,"Muted"); Grid.SetColumn(chevron,2); grid.Children.Add(chevron);
            var head=Ui.Button(grid,(flow==0?"Choose playback device":"Choose microphone")+(expanded==flow?", expanded":""),12);
            head.Padding=new Thickness(12,10,12,10); head.BorderThickness=new Thickness(1);
            head.SetResourceReference(Control.BackgroundProperty,"Inset"); head.SetResourceReference(Control.BorderBrushProperty,"CardBorder");
            head.Click+=delegate { expanded=expanded==flow?-1:flow; Render(); };
            panel.Children.Add(head);
            if(expanded==flow) {
                var list=new StackPanel();
                var devices=settings.SortDevices(snapshot.Devices.Where(d=>d.Flow==flow)).OrderBy(d=>settings.Deemphasized.Contains(d.Id)?1:0).ToList();
                foreach(var d in devices) {
                    var device=d; bool on=current!=null && current.Id==d.Id;
                    var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
                    row.Children.Add(Ui.Text(settings.DisplayName(d),13,on?FontWeights.SemiBold:FontWeights.Normal,on?"Accent":"Text"));
                    if(on) { var check=Ui.Icon(Glyph.Check,12,"Accent"); Grid.SetColumn(check,1); row.Children.Add(check); }
                    var b=Ui.Button(row,"Switch "+(flow==0?"playback":"microphone")+" to "+settings.DisplayName(d)+(on?", current":""),9);
                    b.Padding=new Thickness(14,8,12,8); b.IsEnabled=!owner.Busy; Ui.Tip(b,d.Name);
                    b.Click+=delegate { owner.ApplyDevices(new[] { device },settings.DisplayName(device)); };
                    owner.Register(b); list.Children.Add(b);
                }
                if(devices.Count==0) list.Children.Add(Ui.Wrap("No devices found.",12.5,"Muted"));
                var listScroller=new ScrollViewer { Content=list,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,4,0,0),Focusable=false };
                panel.Children.Add(listScroller); Ui.FadeIn(listScroller,-4);
            }
            return panel;
        }
        // Anchors above the taskbar (or below it when the taskbar is on top), near the tray icon.
        internal void ShowNearTray() {
            expanded=-1; Render();
            if(!IsVisible) { Left=-20000; Top=-20000; Opacity=0; Show(); }
            UpdateLayout();
            var source=PresentationSource.FromVisual(this);
            var toDip=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
            var cursor=Forms.Cursor.Position; var screen=Forms.Screen.FromPoint(cursor);
            var work=screen.WorkingArea; var bounds=screen.Bounds;
            var topLeft=toDip.Transform(new Point(work.Left,work.Top)); var bottomRight=toDip.Transform(new Point(work.Right,work.Bottom));
            var point=toDip.Transform(new Point(cursor.X,cursor.Y));
            Left=Math.Max(topLeft.X+Edge,Math.Min(bottomRight.X-ActualWidth-Edge,point.X-ActualWidth+48));
            taskbarOnTop=work.Top>bounds.Top; workTop=topLeft.Y; workBottom=bottomRight.Y; anchored=true;
            double finalTop=AnchoredTop();
            var ease=new CubicEase { EasingMode=EasingMode.EaseOut };
            BeginAnimation(TopProperty,new DoubleAnimation(finalTop+(taskbarOnTop?-14:14),finalTop,TimeSpan.FromMilliseconds(220)) { EasingFunction=ease,FillBehavior=FillBehavior.Stop });
            Top=finalTop;
            BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
            Activate();
        }
        double AnchoredTop() {
            double top=taskbarOnTop?workTop+Edge:workBottom-ActualHeight-Edge;
            // Grow away from the taskbar, but never past either edge of the usable area.
            return Math.Max(workTop+Edge,Math.Min(top,workBottom-ActualHeight-Edge));
        }
        void KeepOnScreen() { BeginAnimation(TopProperty,null); Top=AnchoredTop(); }
        internal void HideFlyout() {
            if(!IsVisible) return;
            HiddenAt=DateTime.UtcNow;
            var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(120));
            fade.Completed+=delegate { if(Opacity<0.05) Hide(); };
            BeginAnimation(OpacityProperty,fade);
        }
    }
}
