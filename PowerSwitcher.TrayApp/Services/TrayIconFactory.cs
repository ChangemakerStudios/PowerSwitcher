using PowerSwitcher.TrayApp.Configuration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using WpfApplication = System.Windows.Application;

namespace PowerSwitcher.TrayApp.Services
{
    /// <summary>
    /// Builds the tray icon: the plug glyph (black or white to suit the taskbar) with a coloured dot in the
    /// bottom-right corner identifying the active power plan, so the plan can be read at a glance.
    /// </summary>
    public static class TrayIconFactory
    {
        // Defaults run cool to hot, low power to full power: green, blue, orange, red.
        static readonly Color PowerSaverColor = Color.FromArgb(0x22, 0xB1, 0x4C);
        static readonly Color BalancedColor = Color.FromArgb(0x2F, 0x8F, 0xED);
        static readonly Color HighPerformanceColor = Color.FromArgb(0xF7, 0x90, 0x1E);
        static readonly Color UltimatePerformanceColor = Color.FromArgb(0xE8, 0x45, 0x3C);

        static readonly Color PurpleColor = Color.FromArgb(0xA6, 0x66, 0xE8);
        static readonly Color TealColor = Color.FromArgb(0x17, 0xB2, 0xA6);
        static readonly Color PinkColor = Color.FromArgb(0xE8, 0x55, 0x9C);
        static readonly Color GoldColor = Color.FromArgb(0xD9, 0xA4, 0x00);

        // Custom/OEM plans; picked by GUID so a plan keeps its colour across restarts.
        static readonly Color[] CustomPlanColors = { PurpleColor, TealColor, PinkColor, GoldColor };

        /// <summary>The colours offered in Settings. All are bright enough to read on both light and dark taskbars.</summary>
        public static IReadOnlyList<(string Name, Color Color)> Palette { get; } = new[]
        {
            ("Green", PowerSaverColor),
            ("Teal", TealColor),
            ("Blue", BalancedColor),
            ("Purple", PurpleColor),
            ("Pink", PinkColor),
            ("Red", UltimatePerformanceColor),
            ("Orange", HighPerformanceColor),
            ("Gold", GoldColor),
            ("Gray", Color.FromArgb(0x8A, 0x8A, 0x8A)),
        };

        /// <summary>
        /// Badge colour for a plan: the one picked in Settings, else its default. Null when no plan is active (plain plug).
        /// </summary>
        public static Color? BadgeColorFor(Guid? schemaGuid, string schemaName, PowerSwitcherSettings settings)
        {
            if (schemaGuid == null || schemaGuid == Guid.Empty) { return null; }
            if (TryParseColor(settings?.GetPlanColor(schemaGuid.Value), out var picked)) { return picked; }
            return DefaultBadgeColorFor(schemaGuid.Value, schemaName);
        }

        /// <summary>The colour a plan gets when none has been picked for it.</summary>
        public static Color DefaultBadgeColorFor(Guid guid, string schemaName)
        {
            if (guid == WellKnownSchemas.PowerSaver) { return PowerSaverColor; }
            if (guid == WellKnownSchemas.Balanced) { return BalancedColor; }
            if (guid == WellKnownSchemas.HighPerformance) { return HighPerformanceColor; }
            if (guid == WellKnownSchemas.UltimatePerformance) { return UltimatePerformanceColor; }

            // Copies made with `powercfg -duplicatescheme` (the usual way to enable Ultimate Performance) get a fresh
            // GUID but keep the built-in name, so recognise those by their (English) name.
            switch (schemaName?.Trim().ToLowerInvariant())
            {
                case "power saver": return PowerSaverColor;
                case "balanced": return BalancedColor;
                case "high performance": return HighPerformanceColor;
                case "ultimate performance": return UltimatePerformanceColor;
            }

            // Guid.GetHashCode is computed from the GUID's bytes, so it's stable between runs.
            return CustomPlanColors[(uint)guid.GetHashCode() % CustomPlanColors.Length];
        }

        /// <summary>The badge colour as a WPF brush, for showing the same dot in windows; null when there's no badge.</summary>
        public static System.Windows.Media.Brush BadgeBrushFor(Guid? schemaGuid, string schemaName, PowerSwitcherSettings settings)
        {
            var color = BadgeColorFor(schemaGuid, schemaName, settings);
            return color == null ? null : ToBrush(color.Value);
        }

        public static System.Windows.Media.Brush ToBrush(Color color)
        {
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>Reads "#RRGGBB" (as saved in settings). Anything else, e.g. a hand-edited typo, is ignored.</summary>
        public static bool TryParseColor(string hex, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(hex)) { return false; }

            var digits = hex.Trim().TrimStart('#');
            if (digits.Length != 6 || !int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) { return false; }

            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        public static Icon Create(Color? badgeColor, bool isLightTheme, Size size)
        {
            using (var bitmap = CreateBitmap(badgeColor, isLightTheme, size))
            {
                var hIcon = bitmap.GetHicon();
                try
                {
                    using (var borrowed = Icon.FromHandle(hIcon))
                    {
                        return (Icon)borrowed.Clone(); // Clone owns its own handle, so ours can be destroyed.
                    }
                }
                finally { DestroyIcon(hIcon); }
            }
        }

        public static Bitmap CreateBitmap(Color? badgeColor, bool isLightTheme, Size size)
        {
            // Tray.ico has a white glyph, TrayLightTheme.ico has a black one.
            var iconFile = isLightTheme ? "TrayLightTheme.ico" : "Tray.ico";

            Bitmap bitmap;
            using (var iconStream = WpfApplication.GetResourceStream(new Uri($"pack://application:,,,/PowerSwitcher.TrayApp;component/{iconFile}")).Stream)
            using (var baseIcon = new Icon(iconStream, size))
            using (var baseBitmap = baseIcon.ToBitmap())
            {
                // Normalise to the requested size and to 32bpp ARGB (some frames are palettised).
                bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(baseBitmap, new Rectangle(Point.Empty, size));
                }
            }

            if (badgeColor != null) { drawBadge(bitmap, badgeColor.Value); }

            return bitmap;
        }

        /// <summary>
        /// Punches a transparent ring into the glyph and fills a dot inside it. Done per pixel with analytic
        /// coverage because GDI+ can't anti-alias an "erase", and at 16px every pixel of the edge shows.
        /// </summary>
        private static void drawBadge(Bitmap bitmap, Color color)
        {
            int size = Math.Min(bitmap.Width, bitmap.Height);
            float scale = size / 16f;

            float dotRadius = 4.0f * scale;                    // 8px dot at 16px
            float gap = Math.Max(1f, (float)Math.Round(scale)); // transparent ring separating dot from glyph
            float eraseRadius = dotRadius + gap;
            float cx = bitmap.Width - dotRadius;
            float cy = bitmap.Height - dotRadius;

            int left = Math.Max(0, (int)Math.Floor(cx - eraseRadius));
            int top = Math.Max(0, (int)Math.Floor(cy - eraseRadius));

            for (int y = top; y < bitmap.Height; y++)
            {
                for (int x = left; x < bitmap.Width; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    float eraseCoverage = clamp01(eraseRadius - dist + 0.5f);
                    float dotCoverage = clamp01(dotRadius - dist + 0.5f);
                    if (eraseCoverage <= 0f) { continue; }

                    var src = bitmap.GetPixel(x, y);

                    // Glyph faded out by the ring, then the dot composited over it ("over" in premultiplied terms).
                    float glyphA = src.A / 255f * (1f - eraseCoverage) * (1f - dotCoverage);
                    float outA = dotCoverage + glyphA;
                    if (outA <= 0f) { bitmap.SetPixel(x, y, Color.Transparent); continue; }

                    int channel(byte dot, byte glyph) => (int)Math.Round((dot * dotCoverage + glyph * glyphA) / outA);
                    bitmap.SetPixel(x, y, Color.FromArgb(
                        (int)Math.Round(outA * 255f),
                        channel(color.R, src.R),
                        channel(color.G, src.G),
                        channel(color.B, src.B)));
                }
            }
        }

        private static float clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
