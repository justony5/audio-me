using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AudioSwitch {
    // Segoe Fluent Icons (Windows 11) and Segoe MDL2 Assets (Windows 10) share these code points.
    static class Glyph {
        public const string Pin="", Unpin="", Settings="", Minimize="", Close="", Back="";
        public const string Headphones="", Speaker="", Mic="", Monitor="", Add="";
        public const string Star="", StarOutline="", More="", Check="", Warn="", Info="";
        public const string Media="", Calls="", ChevronDown="", ChevronRight="", Edit="", Delete="";
        public const string Open="", Heart="", Link="", Keyboard="", Apps="", Hide="", Power="";
        public const string Game="", Work="", Music="", Movie="", Headset="", Home="", Down="", Up="";
    }
    public static class Ui {
        public static readonly DependencyProperty RadiusProperty=DependencyProperty.RegisterAttached("Radius",typeof(CornerRadius),typeof(Ui),new FrameworkPropertyMetadata(new CornerRadius(10),delegate(DependencyObject o,DependencyPropertyChangedEventArgs e) { ApplyRadius(o as Control); }));
        // Rounds the template's surfaces; applied in code so the loose XAML needs no assembly reference.
        static void ApplyRadius(Control control) {
            if(control==null || control.Template==null) return;
            foreach(var name in new[] { "bd","hov" }) { var part=control.Template.FindName(name,control) as Border; if(part!=null) part.CornerRadius=GetRadius(control); }
        }
        public static CornerRadius GetRadius(DependencyObject o) { return (CornerRadius)o.GetValue(RadiusProperty); }
        public static void SetRadius(DependencyObject o,CornerRadius value) { o.SetValue(RadiusProperty,value); }
        internal static T Res<T>(string key) { return (T)Application.Current.Resources[key]; }
        internal static TextBlock Text(string text,double size,FontWeight weight,string brush) {
            var t=new TextBlock { Text=text,FontSize=size,FontWeight=weight,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty,brush); t.SetResourceReference(TextBlock.FontFamilyProperty,"UiFont"); return t;
        }
        internal static TextBlock Wrap(string text,double size,string brush) {
            var t=Text(text,size,FontWeights.Normal,brush); t.TextWrapping=TextWrapping.Wrap; t.TextTrimming=TextTrimming.None; return t;
        }
        internal static TextBlock Label(string text) {
            var t=Text(text.ToUpperInvariant(),11,FontWeights.SemiBold,"Muted"); t.Margin=new Thickness(2,0,0,0); return t;
        }
        internal static TextBlock Icon(string glyph,double size,string brush) {
            var t=new TextBlock { Text=glyph,FontSize=size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty,"Icons"); t.SetResourceReference(TextBlock.ForegroundProperty,brush);
            AutomationHidden(t); return t;
        }
        static void AutomationHidden(UIElement e) { System.Windows.Automation.AutomationProperties.SetName(e,""); }
        internal static Button Button(object content,string name,double radius) {
            var b=new Button { Content=content };
            b.SetResourceReference(FrameworkElement.StyleProperty,"Btn");
            SetRadius(b,new CornerRadius(radius));
            b.Loaded+=delegate { b.ApplyTemplate(); ApplyRadius(b); };
            System.Windows.Automation.AutomationProperties.SetName(b,name);
            return b;
        }
        internal static Button IconButton(string glyph,string name,double size) {
            var b=Button(Icon(glyph,14,"Muted"),name,8); b.Width=size; b.Height=size; b.Padding=new Thickness(0);
            b.HorizontalContentAlignment=HorizontalAlignment.Center; Tip(b,name); return b;
        }
        internal static Button Pill(string text,string glyph,bool accent) {
            var row=new StackPanel { Orientation=Orientation.Horizontal };
            if(glyph!=null) { var i=Icon(glyph,12,accent?"AccentText":"Text"); i.Margin=new Thickness(0,0,8,0); row.Children.Add(i); }
            row.Children.Add(Text(text,13,FontWeights.SemiBold,accent?"AccentText":"Text"));
            var b=Button(row,text,10); b.Padding=new Thickness(14,8,14,8); b.HorizontalContentAlignment=HorizontalAlignment.Center;
            if(accent) b.SetResourceReference(Control.BackgroundProperty,"Accent"); else b.SetResourceReference(Control.BackgroundProperty,"Card");
            return b;
        }
        internal static Border Card(UIElement child,Thickness padding) {
            var c=new Border { Child=child,Padding=padding,CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1) };
            c.SetResourceReference(Border.BackgroundProperty,"Card"); c.SetResourceReference(Border.BorderBrushProperty,"CardBorder"); return c;
        }
        internal static void Tip(FrameworkElement e,string text) {
            if(String.IsNullOrEmpty(text)) { e.ToolTip=null; return; }
            e.ToolTip=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap };
        }
        internal static void FadeIn(UIElement e,double rise) {
            var ease=new CubicEase { EasingMode=EasingMode.EaseOut };
            e.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)) { EasingFunction=ease });
            if(rise!=0) {
                var t=new TranslateTransform(); e.RenderTransform=t;
                t.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(rise,0,TimeSpan.FromMilliseconds(240)) { EasingFunction=ease });
            }
        }
        // The logo is taken from the largest frame of the embedded icon.
        internal static BitmapSource Logo() {
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("AudioMe.ico")) {
                var decoder=new IconBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                return decoder.Frames.OrderByDescending(f=>f.PixelWidth).First();
            }
        }
    }
    static class Theme {
        public const string TealMode="Teal", WindowsMode="Windows";
        public static readonly Color Teal=Color.FromRgb(0x19,0xE3,0xC6);
        public static Color Current { get; private set; }
        public static void EnsureApp() {
            if(Application.Current==null) new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            if(Application.Current.Resources.Contains("Btn")) return;
            string xaml;
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("Theme.xaml")) using(var reader=new StreamReader(stream)) xaml=reader.ReadToEnd();
            Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
            ApplyAccent(TealMode);
        }
        public static void ApplyAccent(string mode) {
            var color=mode==WindowsMode?Readable(WindowsAccent()??Teal):Teal;
            Current=color; var r=Application.Current.Resources;
            r["Accent"]=Frozen(color); r["AccentText"]=Frozen(Luminance(color)>0.42?Color.FromRgb(0x08,0x14,0x16):Colors.White);
            r["AccentSoft"]=Frozen(Color.FromArgb(0x26,color.R,color.G,color.B)); r["AccentLine"]=Frozen(Color.FromArgb(0x80,color.R,color.G,color.B));
        }
        static SolidColorBrush Frozen(Color c) { var b=new SolidColorBrush(c); b.Freeze(); return b; }
        // Windows stores eight accent shades; the second (Light 2) is the one Windows itself uses on dark surfaces.
        internal static Color? WindowsAccent() {
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent")) {
                    if(key!=null) {
                        var palette=key.GetValue("AccentPalette") as byte[];
                        if(palette!=null && palette.Length>=8) return Color.FromRgb(palette[4],palette[5],palette[6]);
                        object menu=key.GetValue("AccentColorMenu");
                        if(menu!=null) { uint abgr=unchecked((uint)Convert.ToInt32(menu)); return Color.FromRgb((byte)abgr,(byte)(abgr>>8),(byte)(abgr>>16)); }
                    }
                }
            } catch { }
            try { var glass=SystemParameters.WindowGlassColor; return Color.FromRgb(glass.R,glass.G,glass.B); } catch { return null; }
        }
        internal static double Luminance(Color c) {
            Func<byte,double> channel=v => { double s=v/255.0; return s<=0.03928?s/12.92:Math.Pow((s+0.055)/1.055,2.4); };
            return 0.2126*channel(c.R)+0.7152*channel(c.G)+0.0722*channel(c.B);
        }
        // Very dark accents are lifted toward white so text and indicators stay legible on the dark glass.
        internal static Color Readable(Color c) {
            for(int i=0;i<12 && Luminance(c)<0.2;i++) c=Color.FromRgb((byte)(c.R+(255-c.R)*0.15),(byte)(c.G+(255-c.G)*0.15),(byte)(c.B+(255-c.B)*0.15));
            return c;
        }
        public static bool TransparencyEnabled() {
            if(SystemParameters.HighContrast) return false;
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    object value=key==null?null:key.GetValue("EnableTransparency");
                    return value==null || Convert.ToInt32(value)!=0;
                }
            } catch { return true; }
        }
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left,Right,Top,Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
        // Requests the Windows 11 acrylic backdrop. Returns false where it is unavailable so callers paint a solid surface.
        public static bool ApplyBackdrop(Window window) {
            var handle=new WindowInteropHelper(window).Handle; if(handle==IntPtr.Zero) return false;
            try {
                int dark=1; if(DwmSetWindowAttribute(handle,20,ref dark,4)<0) DwmSetWindowAttribute(handle,19,ref dark,4);
                int round=2; DwmSetWindowAttribute(handle,33,ref round,4);
                // Attribute 38 exists from Windows 11 22H2; earlier builds reject it and keep a solid surface.
                int backdrop=TransparencyEnabled()?3:1;
                bool supported=backdrop==3 && DwmSetWindowAttribute(handle,38,ref backdrop,4)>=0;
                if(supported) {
                    var source=HwndSource.FromHwnd(handle);
                    if(source!=null) source.CompositionTarget.BackgroundColor=Colors.Transparent;
                    var margins=new Margins { Left=-1,Right=-1,Top=-1,Bottom=-1 }; DwmExtendFrameIntoClientArea(handle,ref margins);
                    // With the frame extended, DWM would draw native caption buttons over the custom title bar.
                    long style=GetWindowLongPtr(handle,-16).ToInt64(); SetWindowLongPtr(handle,-16,new IntPtr(style&~0x00080000L));
                } else { int none=1; DwmSetWindowAttribute(handle,38,ref none,4); }
                return supported;
            } catch(DllNotFoundException) { return false; } catch(EntryPointNotFoundException) { return false; }
        }
        // A light wash over acrylic keeps text contrast steady over bright wallpapers.
        public static Brush Surface(bool glass) {
            var b=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(0.6,1) };
            b.GradientStops.Add(new GradientStop(glass?Color.FromArgb(0x5C,0x22,0x26,0x36):Color.FromRgb(0x20,0x23,0x30),0));
            b.GradientStops.Add(new GradientStop(glass?Color.FromArgb(0x80,0x10,0x12,0x1A):Color.FromRgb(0x13,0x15,0x1D),1));
            b.Freeze(); return b;
        }
    }
}
