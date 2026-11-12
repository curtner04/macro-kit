using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MacroKit.App
{
    /// <summary>
    /// Light and dark colour sets. Styles.xaml (embedded) defines every brush key once (dark defaults) and every style uses
    /// DynamicResource, so swapping the brushes here restyles all windows at once. The dictionary lives in the application
    /// resources, so the Deck, the run pill and dialogs follow the main window's theme.
    /// </summary>
    public static class Theme
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        static readonly Dictionary<string, string> Dark = new Dictionary<string, string>
        {
            { "Bg", "#111318" }, { "Card", "#1A1D24" }, { "Card2", "#22262F" }, { "Input", "#15181E" }, { "Border", "#2E333D" },
            { "Text", "#E8EAED" }, { "Muted", "#A0A7B4" }, { "Accent", "#4CA8FF" }, { "AccentFg", "#0B1220" }, { "AccentText", "#6CB8FF" },
            { "BtnBg", "#2A2F3A" }, { "Good", "#6CCB8A" }, { "GoodBg", "#1B3326" }, { "Warn", "#F0B44C" }, { "WarnBg", "#3A2E14" },
            { "Bad", "#FF8A80" }, { "BadBg", "#45201E" }, { "Info", "#7FB7FF" }, { "InfoBg", "#1B2A40" }, { "Selected", "#243246" },
            { "Rec", "#FF5A52" }
        };

        static readonly Dictionary<string, string> Light = new Dictionary<string, string>
        {
            { "Bg", "#F3F5F8" }, { "Card", "#FFFFFF" }, { "Card2", "#F6F8FB" }, { "Input", "#FFFFFF" }, { "Border", "#D9DEE6" },
            { "Text", "#1B1F27" }, { "Muted", "#5B6575" }, { "Accent", "#0A64C8" }, { "AccentFg", "#FFFFFF" }, { "AccentText", "#0A5BB5" },
            { "BtnBg", "#E8ECF2" }, { "Good", "#1E7D3C" }, { "GoodBg", "#E2F3E7" }, { "Warn", "#8A5A00" }, { "WarnBg", "#FFF2D6" },
            { "Bad", "#B3261E" }, { "BadBg", "#FBE4E1" }, { "Info", "#0A5BB5" }, { "InfoBg", "#E4EEFB" }, { "Selected", "#E3ECF8" },
            { "Rec", "#D92D20" }
        };

        static ResourceDictionary resources;

        public static bool IsDark { get; private set; }
        public static string Mode { get; private set; }

        public static FontFamily BodyFont { get { return new FontFamily("Segoe UI Variable Text, Segoe UI"); } }

        /// <summary>Loads the embedded styles into the application resources (once) and applies the theme mode.</summary>
        public static void Initialize(Application app, string mode)
        {
            if (resources == null)
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.Styles.xaml"))
                {
                    if (stream == null) throw new InvalidOperationException("The embedded styles are missing.");
                    resources = (ResourceDictionary)XamlReader.Load(stream);
                }
                app.Resources.MergedDictionaries.Add(resources);
            }
            Apply(mode);
        }

        /// <summary>True when Windows' "app mode" is dark (HKCU ...\Themes\Personalize\AppsUseLightTheme = 0). Light when unknown.</summary>
        public static bool SystemIsDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value == 0;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Applies "System", "Light" or "Dark" (any case).</summary>
        public static void Apply(string mode)
        {
            mode = (mode ?? "System").Trim();
            bool dark = mode.Equals("Dark", StringComparison.OrdinalIgnoreCase) || (!mode.Equals("Light", StringComparison.OrdinalIgnoreCase) && SystemIsDark());
            Mode = mode;
            IsDark = dark;
            foreach (var kv in dark ? Dark : Light)
                resources[kv.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
            foreach (Window window in Application.Current.Windows)
                ApplyTitleBar(window);
        }

        /// <summary>Makes the title bar of a window follow the current theme (Windows 10 1809+ / Windows 11).</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            }
            catch (Exception) { /* older Windows: the title bar stays as it is */ }
        }

        /// <summary>Asks Windows 11 for rounded corners on a frameless window (ignored where unsupported).</summary>
        public static void RoundCorners(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                int round = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            }
            catch (Exception) { }
        }

        /// <summary>Gives a window the shared font, colours and title bar.</summary>
        public static void Style(Window window)
        {
            window.SetResourceReference(Window.BackgroundProperty, "Bg");
            window.SetResourceReference(Window.ForegroundProperty, "Text");
            window.FontFamily = BodyFont;
            window.FontSize = 13;
            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            System.Windows.Media.TextOptions.SetTextFormattingMode(window, System.Windows.Media.TextFormattingMode.Ideal);
            window.SourceInitialized += (s, e) => ApplyTitleBar(window);
        }
    }
}
