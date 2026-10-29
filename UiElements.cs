using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioSwitch {
    class MenuColors : ProfessionalColorTable {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(38,38,42); } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(60,68,76); } }
        public override Color MenuItemSelectedGradientBegin { get { return MenuItemSelected; } }
        public override Color MenuItemSelectedGradientEnd { get { return MenuItemSelected; } }
        public override Color MenuItemPressedGradientBegin { get { return MenuItemSelected; } }
        public override Color MenuItemPressedGradientMiddle { get { return MenuItemSelected; } }
        public override Color MenuItemPressedGradientEnd { get { return MenuItemSelected; } }
    }
    // Noninteractive indicators: role state has accessible text as well as color.
    public sealed class RoleIndicator : Control {
        public int Role { get; private set; }
        public bool IsActive { get; private set; }
        public RoleIndicator(int role,bool active) {
            Role=role; IsActive=active; TabStop=false; AccessibleRole=AccessibleRole.Graphic;
            string[] names={"System","Media","Calls"};
            AccessibleName=names[role]+": "+(active?"active default":"not default");
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            float size=Math.Min(Width,Height)*0.88f;
            g.TranslateTransform((Width-size)/2,(Height-size)/2); g.ScaleTransform(size/20f,size/20f);
            using(var pen=new Pen(IsActive?Color.FromArgb(105,220,150):Color.FromArgb(113,119,129),1.6f)) {
                pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round;
                if(Role==0) {
                    g.DrawRectangle(pen,3,3,14,10); g.DrawLine(pen,10,13,10,16); g.DrawLine(pen,6,17,14,17);
                } else if(Role==1) {
                    g.DrawEllipse(pen,3,3,14,14); g.DrawEllipse(pen,8,8,4,4); g.DrawLine(pen,12,6,14,5); g.DrawLine(pen,6,14,8,13);
                } else {
                    pen.Width=2.4f; g.DrawBezier(pen,4,4,1,10,10,19,16,14);
                    pen.Width=3.8f; g.DrawLine(pen,4,3,6,5); g.DrawLine(pen,14,12,16,14);
                }
            }
        }
    }
    class DeviceList : FlowLayoutPanel {
        public DeviceList() { DoubleBuffered=true; Padding=new Padding(3); BackColor=Color.FromArgb(33,34,38); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using(var pen=new Pen(Color.FromArgb(76,85,96))) e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,ClientSize.Width-1),Math.Max(0,ClientSize.Height-1));
        }
    }
    static class WindowTheme {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        public static bool IsDark() {
            if(SystemInformation.HighContrast) return false;
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    object value=key==null?null:key.GetValue("AppsUseLightTheme");
                    return value!=null && Convert.ToInt32(value)==0;
                }
            } catch { return false; }
        }
        public static void Apply(IntPtr window) {
            try {
                int dark=IsDark()?1:0;
                // 20 is the documented immersive-dark-mode attribute; older Windows 10
                // builds used 19. Unsupported builds keep their native title bar.
                if(DwmSetWindowAttribute(window,20,ref dark,4)<0) DwmSetWindowAttribute(window,19,ref dark,4);
            } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
        }
    }
}
