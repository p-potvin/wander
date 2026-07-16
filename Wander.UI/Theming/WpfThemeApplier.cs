using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Wander.UI.Theming
{
    /// <summary>Maps a VaultTheme onto the app-level dynamic brushes declared in App.xaml.</summary>
    public static class WpfThemeApplier
    {
        public static void Apply(VaultTheme theme)
        {
            var resources = Application.Current.Resources;
            resources["BackgroundBrush"] = BrushFrom(theme.Background);
            resources["SurfaceBrush"] = BrushFrom(theme.Surface);
            resources["TextBrush"] = BrushFrom(theme.Text);
            resources["TextMutedBrush"] = BrushFrom(theme.TextMuted);
            resources["AccentBrush"] = BrushFrom(theme.Accent);
            resources["InteractiveBrush"] = BrushFrom(theme.Info);
            resources["BorderBrush"] = BrushFrom(theme.Border);
        }

        /// <summary>Theme values are either "#RRGGBB" or CSS-style "rgba(r,g,b,a)".</summary>
        private static SolidColorBrush BrushFrom(string value)
        {
            if (value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value[5..].TrimEnd(')').Split(',');
                var r = byte.Parse(parts[0], CultureInfo.InvariantCulture);
                var g = byte.Parse(parts[1], CultureInfo.InvariantCulture);
                var b = byte.Parse(parts[2], CultureInfo.InvariantCulture);
                var a = (byte)Math.Round(double.Parse(parts[3], CultureInfo.InvariantCulture) * 255);
                return new SolidColorBrush(Color.FromArgb(a, r, g, b));
            }

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
    }
}
