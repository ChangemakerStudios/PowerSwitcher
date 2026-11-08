using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PowerSwitcher.TrayApp.Services
{
    /// <summary>
    /// Draws the tray context menu in the system's light or dark colours.
    /// </summary>
    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color _text;
        private readonly Color _disabledText;

        public static ToolStripRenderer Create(bool isHighContrast, bool isLight)
        {
            // The stock renderer already uses the contrast theme's system colours.
            return isHighContrast ? new ToolStripProfessionalRenderer() : new TrayMenuRenderer(isLight);
        }

        private TrayMenuRenderer(bool isLight) : base(new Colors(isLight))
        {
            RoundedEdges = false;
            _text = isLight ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;
            _disabledText = isLight ? Color.FromArgb(0x8A, 0x8A, 0x8A) : Color.FromArgb(0x78, 0x78, 0x78);
        }

        private Color GetTextColor(ToolStripItem item)
        {
            return item.Enabled ? _text : _disabledText;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = GetTextColor(e.Item);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = GetTextColor(e.Item);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // The stock check mark is a black bitmap that can't be seen on a dark menu.
            var bounds = e.ImageRectangle;
            var unit = bounds.Width / 16f;
            var graphics = e.Graphics;
            var previousSmoothingMode = graphics.SmoothingMode;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(GetTextColor(e.Item), 1.5f * unit))
            {
                graphics.DrawLines(pen, new[]
                {
                    new PointF(bounds.X + 3f * unit, bounds.Y + 8.5f * unit),
                    new PointF(bounds.X + 6.5f * unit, bounds.Y + 12f * unit),
                    new PointF(bounds.X + 13f * unit, bounds.Y + 4.5f * unit),
                });
            }
            graphics.SmoothingMode = previousSmoothingMode;
        }

        private sealed class Colors : ProfessionalColorTable
        {
            private readonly Color _background;
            private readonly Color _hover;
            private readonly Color _border;

            public Colors(bool isLight)
            {
                UseSystemColors = false;
                _background = isLight ? Color.FromArgb(0xF9, 0xF9, 0xF9) : Color.FromArgb(0x2C, 0x2C, 0x2C);
                _hover = isLight ? Color.FromArgb(0xE8, 0xE8, 0xE8) : Color.FromArgb(0x3D, 0x3D, 0x3D);
                _border = isLight ? Color.FromArgb(0xD0, 0xD0, 0xD0) : Color.FromArgb(0x45, 0x45, 0x45);
            }

            public override Color ToolStripDropDownBackground => _background;
            public override Color ImageMarginGradientBegin => _background;
            public override Color ImageMarginGradientMiddle => _background;
            public override Color ImageMarginGradientEnd => _background;
            public override Color MenuBorder => _border;
            public override Color MenuItemBorder => _hover;
            public override Color MenuItemSelected => _hover;
            public override Color MenuItemSelectedGradientBegin => _hover;
            public override Color MenuItemSelectedGradientEnd => _hover;
            public override Color MenuItemPressedGradientBegin => _hover;
            public override Color MenuItemPressedGradientMiddle => _hover;
            public override Color MenuItemPressedGradientEnd => _hover;
            public override Color SeparatorDark => _border;
            public override Color SeparatorLight => _background;
        }
    }
}
