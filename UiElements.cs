using System;
using System.Drawing;
using System.Windows.Forms;

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
}
