using System;
using System.Windows;
using System.Windows.Media;

namespace RDES.App.Services
{
    public class ThemeService
    {
        private static bool _isDark = false;
        public static bool IsDarkMode => _isDark;

        public static event Action<bool>? ThemeChanged;

        public static void ApplyTheme(bool isDark)
        {
            _isDark = isDark;
            var app = Application.Current;
            if (app == null) return;

            if (isDark)
            {
                // Dark Mode Palette
                SetResource(app, "BackgroundBrush", new SolidColorBrush(Color.FromRgb(24, 24, 27)));      // #18181B
                SetResource(app, "SurfaceBrush", new SolidColorBrush(Color.FromRgb(39, 39, 42)));         // #27272A
                SetResource(app, "SidebarBrush", new SolidColorBrush(Color.FromRgb(31, 31, 35)));         // #1F1F23
                SetResource(app, "BorderBrush", new SolidColorBrush(Color.FromRgb(63, 63, 70)));          // #3F3F46
                SetResource(app, "TextPrimaryBrush", new SolidColorBrush(Color.FromRgb(244, 244, 245)));   // #F4F4F5
                SetResource(app, "TextSecondaryBrush", new SolidColorBrush(Color.FromRgb(161, 161, 170))); // #A1A1AA
                SetResource(app, "PrimaryBrush", new SolidColorBrush(Color.FromRgb(59, 130, 246)));       // #3B82F6
                SetResource(app, "PrimaryHoverBrush", new SolidColorBrush(Color.FromRgb(96, 165, 250)));  // #60A5FA
                SetResource(app, "PrimaryPressedBrush", new SolidColorBrush(Color.FromRgb(29, 78, 216)));// #1D4ED8
            }
            else
            {
                // Light Mode Palette
                SetResource(app, "BackgroundBrush", new SolidColorBrush(Color.FromRgb(248, 249, 250)));  // #F8F9FA
                SetResource(app, "SurfaceBrush", new SolidColorBrush(Color.FromRgb(255, 255, 255)));     // #FFFFFF
                SetResource(app, "SidebarBrush", new SolidColorBrush(Color.FromRgb(240, 242, 245)));     // #F0F2F5
                SetResource(app, "BorderBrush", new SolidColorBrush(Color.FromRgb(224, 224, 224)));      // #E0E0E0
                SetResource(app, "TextPrimaryBrush", new SolidColorBrush(Color.FromRgb(32, 31, 30)));     // #201F1E
                SetResource(app, "TextSecondaryBrush", new SolidColorBrush(Color.FromRgb(96, 94, 92)));   // #605E5C
                SetResource(app, "PrimaryBrush", new SolidColorBrush(Color.FromRgb(15, 108, 189)));      // #0F6CBD
                SetResource(app, "PrimaryHoverBrush", new SolidColorBrush(Color.FromRgb(17, 94, 163)));   // #115EA3
                SetResource(app, "PrimaryPressedBrush", new SolidColorBrush(Color.FromRgb(12, 59, 94)));  // #0C3B5E
            }

            ThemeChanged?.Invoke(isDark);
        }

        private static void SetResource(Application app, string key, object value)
        {
            if (app.Resources.Contains(key))
            {
                app.Resources[key] = value;
            }
            else
            {
                app.Resources.Add(key, value);
            }
        }
    }
}
