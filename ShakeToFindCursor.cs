using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShakeToFindCursor
{
    public enum RenderMode
    {
        OverlayOnly = 0, // Standard Overlay Mode (Zero system cursor modifications)
        HideNativeCursor = 1 // Hide Original Cursor Mode (Blanks native cursors during shake)
    }

    public enum HotspotMode
    {
        AutoTip = 0, // Auto-Detect Tip of Graphic (Finds true top-left tip of non-transparent arrow!)
        TopLeftCorner = 1, // Top-Left (0,0) of image file
        Center = 2, // Center (width/2, height/2) for Crosshair / Target style
        CustomOffset = 3 // Custom Manual Percentage Offset
    }

    // ==========================================
    // Native Win32 API Interop
    // ==========================================
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        public const int CURSOR_SHOWING = 0x00000001;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int ULW_ALPHA = 0x00000002;
        public const uint DI_NORMAL = 0x0003;

        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int GWL_EXSTYLE = -20;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;

        public const int WM_HOTKEY = 0x0312;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int SM_CXCURSOR = 13;
        public const int SM_CYCURSOR = 14;

        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public const uint OCR_NORMAL = 32512;
        public const uint SPI_SETCURSORS = 0x0057;
        public const uint SPIF_SENDCHANGE = 0x02;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, uint lpCursorName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadCursorFromFile(string lpFileName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    // ==========================================
    // App Settings
    // ==========================================
    public class Settings
    {
        // Kept in one place so the loader clamp and the settings slider cannot drift apart.
        public const int MinCursorSize = 100;
        public const int MaxCursorSizeLimit = 500;

        public const double MinShrinkSpeed = 0.05;
        public const double MaxShrinkSpeed = 0.50;
        public const int ShrinkSliderMax = 100;

        public int MaxCursorSize { get; set; }
        public double Sensitivity { get; set; }
        public double TriggerThreshold { get; set; }
        public double ShrinkSpeed { get; set; }
        public bool Enabled { get; set; }
        public bool StartWithWindows { get; set; }
        public RenderMode Mode { get; set; }

        public bool HotkeyEnabled { get; set; }
        public uint HotkeyModifiers { get; set; }
        public uint HotkeyKey { get; set; }
        public bool ShowNotifications { get; set; }

        public bool UseCustomCursor { get; set; }
        public string CustomCursorPath { get; set; }
        public HotspotMode CustomHotspot { get; set; }
        public double CustomHotspotXPercent { get; set; }
        public double CustomHotspotYPercent { get; set; }

        public Settings()
        {
            MaxCursorSize = 300;
            Sensitivity = 1.0;
            TriggerThreshold = 14.0;
            ShrinkSpeed = 0.20;
            Enabled = true;
            StartWithWindows = false;
            Mode = RenderMode.HideNativeCursor;

            HotkeyEnabled = true;
            HotkeyModifiers = NativeMethods.MOD_CONTROL;
            HotkeyKey = (uint)Keys.F7;
            ShowNotifications = true;

            UseCustomCursor = false;
            CustomCursorPath = "";
            CustomHotspot = HotspotMode.AutoTip;
            CustomHotspotXPercent = 0.0;
            CustomHotspotYPercent = 0.0;
        }

        private static string ConfigPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ShakeToFindCursor",
                    "settings.ini"
                );
            }
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Invariant culture throughout: on a locale that uses a comma decimal
                // separator these would otherwise round-trip as "0,20" and stop parsing
                // the moment the user's regional format changed.
                CultureInfo inv = CultureInfo.InvariantCulture;
                using (StreamWriter sw = new StreamWriter(ConfigPath))
                {
                    sw.WriteLine(string.Format(inv, "MaxCursorSize={0}", MaxCursorSize));
                    sw.WriteLine(string.Format(inv, "Sensitivity={0}", Sensitivity));
                    sw.WriteLine(string.Format(inv, "TriggerThreshold={0}", TriggerThreshold));
                    sw.WriteLine(string.Format(inv, "ShrinkSpeed={0}", ShrinkSpeed));
                    sw.WriteLine(string.Format(inv, "Enabled={0}", Enabled));
                    sw.WriteLine(string.Format(inv, "StartWithWindows={0}", StartWithWindows));
                    sw.WriteLine(string.Format(inv, "RenderMode={0}", (int)Mode));
                    sw.WriteLine(string.Format(inv, "HotkeyEnabled={0}", HotkeyEnabled));
                    sw.WriteLine(string.Format(inv, "HotkeyModifiers={0}", HotkeyModifiers));
                    sw.WriteLine(string.Format(inv, "HotkeyKey={0}", HotkeyKey));
                    sw.WriteLine(string.Format(inv, "ShowNotifications={0}", ShowNotifications));
                    sw.WriteLine(string.Format(inv, "UseCustomCursor={0}", UseCustomCursor));
                    sw.WriteLine(string.Format(inv, "CustomCursorPath={0}", CustomCursorPath));
                    sw.WriteLine(string.Format(inv, "CustomHotspot={0}", (int)CustomHotspot));
                    sw.WriteLine(string.Format(inv, "CustomHotspotXPercent={0}", CustomHotspotXPercent));
                    sw.WriteLine(string.Format(inv, "CustomHotspotYPercent={0}", CustomHotspotYPercent));
                }

                SetRegistryStartup(StartWithWindows);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving settings: " + ex.Message);
            }
        }

        public static Settings Load()
        {
            Settings settings = new Settings();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (string line in File.ReadAllLines(ConfigPath))
                    {
                        // Split on the first '=' only -- a cursor file path is allowed to
                        // contain one, and Split('=') would silently drop the whole line.
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            string key = line.Substring(0, eq).Trim();
                            string val = line.Substring(eq + 1).Trim();
                            if (key == "MaxCursorSize") { int mcs; if (TryParseInt(val, out mcs)) settings.MaxCursorSize = Math.Max(MinCursorSize, Math.Min(MaxCursorSizeLimit, mcs)); }
                            if (key == "Sensitivity") { double sv; if (TryParseDouble(val, out sv)) settings.Sensitivity = Math.Max(0.2, Math.Min(3.0, sv)); }
                            if (key == "TriggerThreshold") { double tt; if (TryParseDouble(val, out tt)) settings.TriggerThreshold = Math.Max(4.0, Math.Min(40.0, tt)); }
                            if (key == "ShrinkSpeed") { double ss; if (TryParseDouble(val, out ss)) settings.ShrinkSpeed = Math.Max(0.05, Math.Min(0.5, ss)); }
                            if (key == "Enabled") { bool en; if (bool.TryParse(val, out en)) settings.Enabled = en; }
                            if (key == "StartWithWindows") { bool sww; if (bool.TryParse(val, out sww)) settings.StartWithWindows = sww; }
                            if (key == "RenderMode") { int rm; if (TryParseInt(val, out rm) && Enum.IsDefined(typeof(RenderMode), rm)) settings.Mode = (RenderMode)rm; }
                            if (key == "HotkeyEnabled") { bool he; if (bool.TryParse(val, out he)) settings.HotkeyEnabled = he; }
                            if (key == "HotkeyModifiers") { int hm; if (TryParseInt(val, out hm) && hm >= 0) settings.HotkeyModifiers = (uint)hm; }
                            if (key == "HotkeyKey") { int hk; if (TryParseInt(val, out hk) && hk > 0 && hk <= 0xFF) settings.HotkeyKey = (uint)hk; }
                            if (key == "ShowNotifications") { bool sn; if (bool.TryParse(val, out sn)) settings.ShowNotifications = sn; }
                            if (key == "UseCustomCursor") { bool ucc; if (bool.TryParse(val, out ucc)) settings.UseCustomCursor = ucc; }
                            if (key == "CustomCursorPath") { settings.CustomCursorPath = val; }
                            if (key == "CustomHotspot") { int ch; if (TryParseInt(val, out ch) && Enum.IsDefined(typeof(HotspotMode), ch)) settings.CustomHotspot = (HotspotMode)ch; }
                            if (key == "CustomHotspotXPercent") { double hx; if (TryParseDouble(val, out hx)) settings.CustomHotspotXPercent = ClampRatio(hx); }
                            if (key == "CustomHotspotYPercent") { double hy; if (TryParseDouble(val, out hy)) settings.CustomHotspotYPercent = ClampRatio(hy); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading settings: " + ex.Message);
            }
            return settings;
        }

        /// <summary>
        /// Invariant first, then the current culture, so settings files written by earlier
        /// versions (which used the local decimal separator) still load.
        /// </summary>
        private static bool TryParseDouble(string val, out double result)
        {
            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return true;
            return double.TryParse(val, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private static bool TryParseInt(string val, out int result)
        {
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) return true;
            return int.TryParse(val, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
        }

        /// <summary>
        /// Hotspot offsets are a 0..1 ratio of the rendered size, despite the historic
        /// "Percent" name. A hand-edited 50 meaning "50%" would otherwise place the
        /// hotspot fifty times the cursor width away from the pointer.
        /// </summary>
        private static double ClampRatio(double v)
        {
            if (v > 1.0 && v <= 100.0) v /= 100.0;
            return Math.Max(0.0, Math.Min(1.0, v));
        }

        /// <summary>
        /// Maps the shrink slider position to the per-tick lerp coefficient geometrically.
        ///
        /// Perceived shrink duration goes as 1 / -ln(1 - k), which is steeply non-linear.
        /// Under the old linear mapping the top 60% of the slider moved the animation from
        /// 281 ms to 203 ms -- a difference nobody can see -- while everything useful was
        /// crammed into the bottom few steps. Geometric spacing gives every part of the
        /// slider travel a visible effect.
        /// </summary>
        public static double ShrinkSpeedFromSlider(int pos)
        {
            if (pos < 0) pos = 0;
            if (pos > ShrinkSliderMax) pos = ShrinkSliderMax;
            return MinShrinkSpeed * Math.Pow(MaxShrinkSpeed / MinShrinkSpeed, pos / (double)ShrinkSliderMax);
        }

        public static int SliderFromShrinkSpeed(double speed)
        {
            if (speed <= MinShrinkSpeed) return 0;
            if (speed >= MaxShrinkSpeed) return ShrinkSliderMax;
            double pos = ShrinkSliderMax * Math.Log(speed / MinShrinkSpeed) / Math.Log(MaxShrinkSpeed / MinShrinkSpeed);
            return Math.Max(0, Math.Min(ShrinkSliderMax, (int)Math.Round(pos)));
        }

        /// <summary>
        /// Approximate visible shrink time from full size, calibrated against a simulation
        /// of the detector loop. For the settings label only -- the true duration also
        /// depends on how large the cursor actually grew.
        /// </summary>
        public static int EstimateShrinkMilliseconds(double speed)
        {
            return (int)Math.Round(41.8 / (-Math.Log(1.0 - speed)) + 121.0);
        }

        /// <summary>
        /// Human-readable form of a hotkey, e.g. "Ctrl + F7" or "Ctrl + Shift + MediaPlayPause".
        /// </summary>
        public static string DescribeHotkey(uint modifiers, uint key)
        {
            if (key == 0) return "(none)";

            string text = "";
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0) text += "Ctrl + ";
            if ((modifiers & NativeMethods.MOD_ALT) != 0) text += "Alt + ";
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0) text += "Shift + ";
            if ((modifiers & NativeMethods.MOD_WIN) != 0) text += "Win + ";

            return text + DescribeKey(key);
        }

        private static string DescribeKey(uint key)
        {
            Keys k = (Keys)key;

            // Keys.D0..D9 and Keys.NumPad0..9 stringify with prefixes nobody recognises.
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (char)('0' + (k - Keys.NumPad0));

            switch (k)
            {
                case Keys.Oemplus: return "+";
                case Keys.OemMinus: return "-";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Oemtilde: return "`";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Prior: return "Page Up";
                case Keys.Next: return "Page Down";
                default: return k.ToString();
            }
        }

        /// <summary>
        /// Modifier-only combinations cannot be registered, and a bare key would swallow that
        /// key system-wide, so at least one modifier plus a real key is required.
        /// </summary>
        public static bool IsValidHotkey(uint modifiers, uint key)
        {
            if (key == 0) return false;
            Keys k = (Keys)key;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu ||
                k == Keys.LWin || k == Keys.RWin || k == Keys.None) return false;
            return modifiers != 0;
        }

        public Settings Clone()
        {
            return (Settings)this.MemberwiseClone();
        }

        public void CopyFrom(Settings other)
        {
            if (other == null) return;
            MaxCursorSize = other.MaxCursorSize;
            Sensitivity = other.Sensitivity;
            TriggerThreshold = other.TriggerThreshold;
            ShrinkSpeed = other.ShrinkSpeed;
            Enabled = other.Enabled;
            StartWithWindows = other.StartWithWindows;
            Mode = other.Mode;
            HotkeyEnabled = other.HotkeyEnabled;
            HotkeyModifiers = other.HotkeyModifiers;
            HotkeyKey = other.HotkeyKey;
            ShowNotifications = other.ShowNotifications;
            UseCustomCursor = other.UseCustomCursor;
            CustomCursorPath = other.CustomCursorPath;
            CustomHotspot = other.CustomHotspot;
            CustomHotspotXPercent = other.CustomHotspotXPercent;
            CustomHotspotYPercent = other.CustomHotspotYPercent;
        }

        public static void SetRegistryStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        string appPath = Application.ExecutablePath;
                        if (enable)
                        {
                            key.SetValue("ShakeToFindCursor", string.Format("\"{0}\"", appPath));
                        }
                        else
                        {
                            if (key.GetValue("ShakeToFindCursor") != null)
                            {
                                key.DeleteValue("ShakeToFindCursor");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error configuring startup registry: " + ex.Message);
            }
        }
    }

    // ==========================================
    // Global Native Cursor Blanking Helper
    // ==========================================
    public static class NativeCursorHelper
    {
        // Guards all mutable state below. The crash-safety handlers (ProcessExit,
        // UnhandledException, SessionEnding) can fire on threads other than the UI thread.
        private static readonly object _sync = new object();

        // Re-asserting the blank cursors defends against another app broadcasting
        // SPI_SETCURSORS while we are hidden. Doing it every frame is pure waste, so it is
        // rate limited. SetSystemCursor destroys the handle it is given and reports a
        // different handle back through GetCursorInfo, so the installed blank cursor can
        // never be identified by comparing handle values.
        private const int ReassertIntervalMs = 500;

        private static bool _isHidden = false;
        private static bool _backupComplete = false;
        private static int _lastAssertTick = 0;
        private static IntPtr _hBlankCursor = IntPtr.Zero;
        private static IntPtr _hCachedArrowCursor = IntPtr.Zero;
        private static Dictionary<uint, IntPtr> _backedUpCursors = new Dictionary<uint, IntPtr>();

        public static readonly uint[] SystemCursorIds = new uint[]
        {
            32512, // OCR_NORMAL (Arrow)
            32513, // OCR_IBEAM (Text beam)
            32514, // OCR_WAIT (Spinner)
            32515, // OCR_CROSS (Crosshair)
            32516, // OCR_UPARROW (Up arrow)
            32642, // OCR_SIZENWSE
            32643, // OCR_SIZENESW
            32644, // OCR_SIZEWE
            32645, // OCR_SIZENS
            32646, // OCR_SIZEALL
            32648, // OCR_NO
            32649, // OCR_HAND (Link hand)
            32650  // OCR_APPSTARTING
        };

        public static bool IsHidden { get { lock (_sync) { return _isHidden; } } }

        public static void BackupSystemCursors()
        {
            lock (_sync) { BackupSystemCursorsCore(); }
        }

        private static void BackupSystemCursorsCore()
        {
            if (_backupComplete) return;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
            {
                string[] valueNames = new string[]
                {
                    "Arrow", "IBeam", "Wait", "Crosshair", "UpArrow",
                    "SizeNWSE", "SizeNESW", "SizeWE", "SizeNS", "SizeAll",
                    "No", "Hand", "AppStarting"
                };

                for (int i = 0; i < SystemCursorIds.Length && i < valueNames.Length; i++)
                {
                    uint id = SystemCursorIds[i];
                    if (!_backedUpCursors.ContainsKey(id) || _backedUpCursors[id] == IntPtr.Zero)
                    {
                        string path = key != null ? key.GetValue(valueNames[i]) as string : null;
                        IntPtr hCur = IntPtr.Zero;

                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            hCur = NativeMethods.LoadCursorFromFile(path);
                        }

                        if (hCur == IntPtr.Zero)
                        {
                            IntPtr hCurrent = NativeMethods.LoadCursor(IntPtr.Zero, id);
                            if (hCurrent != IntPtr.Zero) hCur = NativeMethods.CopyIcon(hCurrent);
                        }

                        if (hCur != IntPtr.Zero)
                        {
                            _backedUpCursors[id] = hCur;
                        }
                    }
                }
            }

            if (_backedUpCursors.ContainsKey(NativeMethods.OCR_NORMAL))
            {
                _hCachedArrowCursor = _backedUpCursors[NativeMethods.OCR_NORMAL];
            }

            _backupComplete = _backedUpCursors.Count > 0;
        }

        public static IntPtr GetDefaultArrowCursor()
        {
            lock (_sync)
            {
                if (_hCachedArrowCursor == IntPtr.Zero)
                {
                    BackupSystemCursorsCore();
                }
                if (_hCachedArrowCursor == IntPtr.Zero)
                {
                    IntPtr hSystemArrow = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.OCR_NORMAL);
                    if (hSystemArrow != IntPtr.Zero)
                    {
                        _hCachedArrowCursor = NativeMethods.CopyIcon(hSystemArrow);
                    }
                }
                return _hCachedArrowCursor;
            }
        }

        private static IntPtr GetBlankCursorCore()
        {
            if (_hBlankCursor == IntPtr.Zero)
            {
                // CreateCursor requires the system cursor dimensions, which are not always
                // 16x16 -- large-cursor accessibility settings and high-DPI schemes report
                // 32, 48 or more. Masks are 1 bit per pixel with WORD-aligned rows.
                int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR);
                int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYCURSOR);
                if (w <= 0) w = 32;
                if (h <= 0) h = 32;

                int strideBytes = ((w + 15) / 16) * 2;
                int maskBytes = strideBytes * h;

                byte[] andMask = new byte[maskBytes];
                for (int i = 0; i < andMask.Length; i++) andMask[i] = 0xFF; // 1s = transparent

                byte[] xorMask = new byte[maskBytes]; // 0s = zero color

                _hBlankCursor = NativeMethods.CreateCursor(IntPtr.Zero, 0, 0, w, h, andMask, xorMask);
            }
            return _hBlankCursor;
        }

        /// <summary>
        /// Blanks the system cursors if they are not already blanked, and periodically
        /// re-asserts the blanking. Cheap enough to call every frame.
        /// </summary>
        public static void EnsureNativeCursorHidden()
        {
            lock (_sync)
            {
                int now = Environment.TickCount;
                if (_isHidden && unchecked(now - _lastAssertTick) < ReassertIntervalMs) return;
                _lastAssertTick = now;
                HideNativeCursorCore();
            }
        }

        private static void HideNativeCursorCore()
        {
            BackupSystemCursorsCore();

            IntPtr hBlank = GetBlankCursorCore();
            if (hBlank != IntPtr.Zero)
            {
                foreach (uint id in SystemCursorIds)
                {
                    // SetSystemCursor destroys the handle it is passed, so each role gets
                    // its own copy and the master blank handle stays valid.
                    IntPtr hBlankCopy = NativeMethods.CopyIcon(hBlank);
                    if (hBlankCopy != IntPtr.Zero) NativeMethods.SetSystemCursor(hBlankCopy, id);
                }
                _isHidden = true;
            }
        }

        public static void RestoreNativeCursor()
        {
            lock (_sync)
            {
                if (!_isHidden) return;

                foreach (uint id in SystemCursorIds)
                {
                    if (_backedUpCursors.ContainsKey(id) && _backedUpCursors[id] != IntPtr.Zero)
                    {
                        IntPtr hRestoreCopy = NativeMethods.CopyIcon(_backedUpCursors[id]);
                        if (hRestoreCopy != IntPtr.Zero)
                        {
                            NativeMethods.SetSystemCursor(hRestoreCopy, id);
                        }
                    }
                }

                ForceSystemCursorReload();
                _isHidden = false;
            }
        }

        /// <summary>
        /// The system cursor edge length in pixels. Not always 32: large-cursor
        /// accessibility settings and high-DPI cursor schemes commonly report 48 or 64,
        /// and every scale and hotspot calculation has to be relative to this.
        /// </summary>
        public static int GetBaseCursorSize()
        {
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR);
            return w > 0 ? w : 32;
        }

        /// <summary>
        /// Unconditionally reloads every system cursor role from the user's saved scheme.
        /// SPIF_SENDCHANGE only broadcasts the change -- it never writes to the registry,
        /// so the saved scheme stays the source of truth and cannot be corrupted.
        ///
        /// Called at startup as a self-heal: if a previous run was killed while the cursors
        /// were blanked, this is what gives the user their pointer back.
        /// </summary>
        public static void ForceSystemCursorReload()
        {
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero, NativeMethods.SPIF_SENDCHANGE);
        }
    }

    // ==========================================
    // Global Hotkey (works while other apps, including games, have focus)
    // ==========================================
    public class HotkeyManager : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0xB1A5;

        private bool _registered = false;

        public event EventHandler Pressed;

        public HotkeyManager()
        {
            // A plain message-receiving window: RegisterHotKey needs an HWND whose thread
            // pumps messages, and WM_HOTKEY is delivered to it rather than to the focused
            // application, which is what lets this work from inside a game.
            CreateHandle(new CreateParams());
        }

        public bool IsRegistered { get { return _registered; } }

        /// <summary>
        /// Returns false if the combination is invalid or already owned by another
        /// application; the caller is expected to surface that to the user.
        /// </summary>
        public bool Register(uint modifiers, uint key)
        {
            Unregister();

            if (!Settings.IsValidHotkey(modifiers, key)) return false;

            // MOD_NOREPEAT stops a held-down combo from firing continuously.
            _registered = NativeMethods.RegisterHotKey(this.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, key);
            return _registered;
        }

        public void Unregister()
        {
            if (!_registered) return;
            NativeMethods.UnregisterHotKey(this.Handle, HotkeyId);
            _registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                EventHandler handler = Pressed;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (this.Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    // ==========================================
    // Mouse Shake Detection & Progressive Scale Engine
    // ==========================================
    public class ShakeDetector
    {
        // The physics below was tuned against a nominal 10 ms tick. The WinForms timer is
        // WM_TIMER based and cannot actually deliver 10 ms (~15.6 ms floor), and it slips
        // further under load, so decay and smoothing are expressed per reference tick and
        // then raised to the number of reference ticks that actually elapsed. At exactly
        // 10 ms this is arithmetically identical to the original code.
        private const double ReferenceTickMs = 10.0;
        private const double EnergyDecayPerTick = 0.88;
        private const double GrowLerpPerTick = 0.25;

        private NativeMethods.POINT _lastPos;
        private int _lastDx = 0;
        private int _lastDy = 0;
        private int _lastTick = 0;
        private double _shakeEnergy = 0.0;
        private double _currentScale = 1.0;
        private bool _isFirstSample = true;

        public double CurrentScale { get { return _currentScale; } }

        public void Reset()
        {
            _shakeEnergy = 0;
            _currentScale = 1.0;
            _isFirstSample = true;
            _lastTick = 0;
        }

        public void TriggerSimulatedShake(double amount)
        {
            _shakeEnergy = amount;
        }

        public void Update(Settings settings)
        {
            if (!settings.Enabled)
            {
                _currentScale = 1.0;
                _shakeEnergy = 0.0;
                return;
            }

            NativeMethods.POINT currentPos;
            if (!NativeMethods.GetCursorPos(out currentPos))
            {
                return;
            }

            if (_isFirstSample)
            {
                _lastPos = currentPos;
                _isFirstSample = false;
                return;
            }

            int dx = currentPos.x - _lastPos.x;
            int dy = currentPos.y - _lastPos.y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist > 6.0)
            {
                int dotProduct = dx * _lastDx + dy * _lastDy;
                if (dotProduct < 0)
                {
                    double addEnergy = dist * 1.5;
                    _shakeEnergy += addEnergy;
                }

                _lastDx = dx;
                _lastDy = dy;
            }

            _lastPos = currentPos;

            int now = Environment.TickCount;
            double elapsedMs = (_lastTick == 0) ? ReferenceTickMs : unchecked(now - _lastTick);
            if (elapsedMs < 1.0) elapsedMs = 1.0;
            if (elapsedMs > 100.0) elapsedMs = 100.0; // don't let a hitch dump all energy
            _lastTick = now;

            double steps = elapsedMs / ReferenceTickMs;

            _shakeEnergy *= Math.Pow(EnergyDecayPerTick, steps);
            if (_shakeEnergy < 0.1) _shakeEnergy = 0.0;

            double triggerThreshold = settings.TriggerThreshold;
            double targetScale = 1.0;

            if (_shakeEnergy > triggerThreshold)
            {
                double excess = _shakeEnergy - triggerThreshold;
                double maxScaleFactor = settings.MaxCursorSize / (double)NativeCursorHelper.GetBaseCursorSize();

                double scaleAdd = Math.Min(maxScaleFactor - 1.0, excess * 0.035 * settings.Sensitivity);
                targetScale = 1.0 + scaleAdd;
            }

            double lerpPerTick = (targetScale > _currentScale) ? GrowLerpPerTick : settings.ShrinkSpeed;
            double lerp = 1.0 - Math.Pow(1.0 - lerpPerTick, steps);
            _currentScale += (targetScale - _currentScale) * lerp;

            if (_currentScale < 1.05)
            {
                _currentScale = 1.0;
            }
        }
    }

    // ==========================================
    // Layered Topmost Overlay Form
    // ==========================================
    public class OverlayForm : Form
    {
        private Settings _settings;
        private Bitmap _cachedCustomBmp;
        private string _cachedCustomPath;
        private PointF _detectedTipRatio = new PointF(0, 0);

        // The layered window is never Show()n, so Form.Visible stays false and cannot be
        // used to tell whether the overlay is currently on screen.
        private bool _overlayVisible = false;

        public OverlayForm(Settings settings)
        {
            _settings = settings;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.DoubleBuffered = true;

            IntPtr h = this.Handle;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED;
                cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT;
                cp.ExStyle |= NativeMethods.WS_EX_TOPMOST;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cachedCustomBmp != null)
            {
                _cachedCustomBmp.Dispose();
                _cachedCustomBmp = null;
            }
            base.Dispose(disposing);
        }

        private PointF AutoDetectGraphicTip(Bitmap bmp)
        {
            if (bmp == null) return new PointF(0, 0);

            int width = bmp.Width;
            int height = bmp.Height;
            if (width <= 0 || height <= 0) return new PointF(0, 0);

            int bestX = 0;
            int bestY = 0;
            double minDistanceSquare = double.MaxValue;

            // GetPixel is a marshalled call per pixel and visibly hangs the Browse dialog
            // on a large PNG; one LockBits pass does the same work in a single copy.
            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb
            );

            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] bytes = new byte[stride * height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        byte alpha = bytes[row + x * 4 + 3];
                        if (alpha > 30)
                        {
                            double distSq = (double)x * x + (double)y * y;
                            if (distSq < minDistanceSquare)
                            {
                                minDistanceSquare = distSq;
                                bestX = x;
                                bestY = y;
                            }
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return new PointF((float)bestX / width, (float)bestY / height);
        }

        private Bitmap GetCustomBitmap(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            if (_cachedCustomBmp != null && _cachedCustomPath == path)
            {
                return _cachedCustomBmp;
            }

            try
            {
                // Clear the cache fields before loading. If the load throws, leaving the
                // old path paired with a disposed bitmap would make every later call take
                // the cache-hit path above and hand back a disposed object.
                if (_cachedCustomBmp != null) _cachedCustomBmp.Dispose();
                _cachedCustomBmp = null;
                _cachedCustomPath = null;

                using (Image img = Image.FromFile(path))
                {
                    _cachedCustomBmp = new Bitmap(img);
                    _cachedCustomPath = path;
                    _detectedTipRatio = AutoDetectGraphicTip(_cachedCustomBmp);
                }
                return _cachedCustomBmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading custom cursor image: " + ex.Message);
                return null;
            }
        }

        private static void PremultiplyAlpha(Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb
            );

            try
            {
                int size = Math.Abs(data.Stride) * data.Height;
                byte[] bytes = new byte[size];
                Marshal.Copy(data.Scan0, bytes, 0, size);

                for (int i = 0; i < size; i += 4)
                {
                    byte b = bytes[i];
                    byte g = bytes[i + 1];
                    byte r = bytes[i + 2];
                    byte a = bytes[i + 3];

                    if (a == 0)
                    {
                        bytes[i] = 0;
                        bytes[i + 1] = 0;
                        bytes[i + 2] = 0;
                    }
                    else if (a < 255)
                    {
                        bytes[i] = (byte)((b * a + 127) / 255);
                        bytes[i + 1] = (byte)((g * a + 127) / 255);
                        bytes[i + 2] = (byte)((r * a + 127) / 255);
                    }
                }

                Marshal.Copy(bytes, 0, data.Scan0, size);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public void RenderCursorOverlay(double scale)
        {
            if (scale <= 1.05 || !_settings.Enabled)
            {
                ClearAndHideOverlay();
                return;
            }

            if (_settings.Mode == RenderMode.HideNativeCursor)
            {
                NativeCursorHelper.EnsureNativeCursorHidden();
            }

            NativeMethods.CURSORINFO ci = new NativeMethods.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(NativeMethods.CURSORINFO));
            if (!NativeMethods.GetCursorInfo(ref ci))
            {
                ClearAndHideOverlay();
                return;
            }

            int baseSize = NativeCursorHelper.GetBaseCursorSize();
            int targetSize = (int)(baseSize * scale);
            if (targetSize > _settings.MaxCursorSize) targetSize = _settings.MaxCursorSize;

            double actualScale = (double)targetSize / baseSize;

            Bitmap customBmp = _settings.UseCustomCursor ? GetCustomBitmap(_settings.CustomCursorPath) : null;

            int scaledHotspotX = 0;
            int scaledHotspotY = 0;

            if (customBmp != null)
            {
                if (_settings.CustomHotspot == HotspotMode.AutoTip)
                {
                    scaledHotspotX = (int)(targetSize * _detectedTipRatio.X);
                    scaledHotspotY = (int)(targetSize * _detectedTipRatio.Y);
                }
                else if (_settings.CustomHotspot == HotspotMode.Center)
                {
                    scaledHotspotX = targetSize / 2;
                    scaledHotspotY = targetSize / 2;
                }
                else if (_settings.CustomHotspot == HotspotMode.CustomOffset)
                {
                    scaledHotspotX = (int)(targetSize * _settings.CustomHotspotXPercent);
                    scaledHotspotY = (int)(targetSize * _settings.CustomHotspotYPercent);
                }
                else
                {
                    scaledHotspotX = 0;
                    scaledHotspotY = 0;
                }
            }
            else
            {
                IntPtr hArrowCursor = NativeCursorHelper.GetDefaultArrowCursor();
                if (hArrowCursor == IntPtr.Zero) hArrowCursor = ci.hCursor;

                NativeMethods.ICONINFO ii;
                if (NativeMethods.GetIconInfo(hArrowCursor, out ii))
                {
                    scaledHotspotX = (int)(ii.xHotspot * actualScale);
                    scaledHotspotY = (int)(ii.yHotspot * actualScale);

                    if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
                }
            }

            int windowX = ci.ptScreenPos.x - scaledHotspotX;
            int windowY = ci.ptScreenPos.y - scaledHotspotY;

            using (Bitmap bitmap = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (customBmp != null)
                    {
                        g.DrawImage(customBmp, 0, 0, targetSize, targetSize);
                    }
                    else
                    {
                        IntPtr hArrowCursor = NativeCursorHelper.GetDefaultArrowCursor();
                        if (hArrowCursor == IntPtr.Zero) hArrowCursor = ci.hCursor;

                        IntPtr hdc = g.GetHdc();
                        try
                        {
                            NativeMethods.DrawIconEx(
                                hdc, 
                                0, 0, 
                                hArrowCursor, 
                                targetSize, targetSize, 
                                0, 
                                IntPtr.Zero, 
                                NativeMethods.DI_NORMAL
                            );
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                    }
                }

                PremultiplyAlpha(bitmap);
                UpdateLayeredWindowBitmap(bitmap, windowX, windowY, targetSize, targetSize);
            }

            // UpdateLayeredWindow already moved and resized the window; SetWindowPos is
            // only needed to bring it on screen the first time.
            if (!_overlayVisible)
            {
                _overlayVisible = true;
                NativeMethods.SetWindowPos(
                    this.Handle,
                    NativeMethods.HWND_TOPMOST,
                    windowX, windowY, targetSize, targetSize,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW
                );
            }
        }

        private void ClearAndHideOverlay()
        {
            if (NativeCursorHelper.IsHidden)
            {
                NativeCursorHelper.RestoreNativeCursor();
            }

            // The tick timer runs continuously, so without this guard an idle tray app
            // would allocate a bitmap and churn GDI handles ~60 times a second forever.
            if (!_overlayVisible) return;
            _overlayVisible = false;

            using (Bitmap emptyBmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
            {
                UpdateLayeredWindowBitmap(emptyBmp, -10000, -10000, 1, 1);
            }

            if (this.Visible)
            {
                this.Hide();
            }

            NativeMethods.SetWindowPos(
                this.Handle,
                IntPtr.Zero,
                -10000, -10000, 1, 1,
                NativeMethods.SWP_HIDEWINDOW | NativeMethods.SWP_NOACTIVATE
            );
        }

        private void UpdateLayeredWindowBitmap(Bitmap bitmap, int x, int y, int width, int height)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

            try
            {
                NativeMethods.POINT ptDst = new NativeMethods.POINT { x = x, y = y };
                NativeMethods.SIZE size = new NativeMethods.SIZE { cx = width, cy = height };
                NativeMethods.POINT ptSrc = new NativeMethods.POINT { x = 0, y = 0 };

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(
                    this.Handle, 
                    screenDc, 
                    ref ptDst, 
                    ref size, 
                    memDc, 
                    ref ptSrc, 
                    0, 
                    ref blend, 
                    NativeMethods.ULW_ALPHA
                );
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                NativeMethods.SelectObject(memDc, oldBitmap);
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(memDc);
            }
        }
    }

    // ==========================================
    // Hotkey Capture Field
    // ==========================================
    public class HotkeyTextBox : TextBox
    {
        public uint Modifiers { get; private set; }
        public uint KeyCode { get; private set; }

        public event EventHandler HotkeyChanged;

        public HotkeyTextBox()
        {
            this.ReadOnly = true;
            this.Cursor = Cursors.Hand;
        }

        public void SetHotkey(uint modifiers, uint key)
        {
            Modifiers = modifiers;
            KeyCode = key;
            this.Text = Settings.DescribeHotkey(modifiers, key);
        }

        /// <summary>
        /// ProcessCmdKey runs before dialog and menu key handling, which is the only way to
        /// capture Tab, Escape, Enter, the arrows, F10 and Alt combinations. Media, browser
        /// and launch keys arrive here too on keyboards that report them as virtual keys;
        /// vendor macro keys handled entirely inside their own driver never reach Windows
        /// as a key press and cannot be captured by any application.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!this.Focused) return base.ProcessCmdKey(ref msg, keyData);

            Keys key = keyData & Keys.KeyCode;

            // Ignore the modifiers themselves; wait for a real key to land.
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.None)
            {
                return true;
            }

            // Backspace or Delete with no modifiers clears the shortcut.
            if ((key == Keys.Back || key == Keys.Delete) && (keyData & Keys.Modifiers) == 0)
            {
                SetHotkey(0, 0);
                RaiseChanged();
                return true;
            }

            uint mods = 0;
            if ((keyData & Keys.Control) == Keys.Control) mods |= NativeMethods.MOD_CONTROL;
            if ((keyData & Keys.Alt) == Keys.Alt) mods |= NativeMethods.MOD_ALT;
            if ((keyData & Keys.Shift) == Keys.Shift) mods |= NativeMethods.MOD_SHIFT;

            SetHotkey(mods, (uint)key);
            RaiseChanged();
            return true;
        }

        private void RaiseChanged()
        {
            EventHandler handler = HotkeyChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            this.Text = "Press a key combination...";
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            this.Text = Settings.DescribeHotkey(Modifiers, KeyCode);
        }
    }

    // ==========================================
    // Modern Windows 11 Styled Settings Dialog
    // ==========================================
    public class SettingsForm : Form
    {
        private Settings _settings;
        private ShakeDetector _detector;

        // Snapshot taken when the dialog opens, so closing without saving can put back
        // anything that was already pushed live by the Apply button.
        private Settings _snapshot;
        private bool _saved = false;
        private bool _dpiScaleApplied = false;
        private bool _uiReady = false;

        private TrackBar tbMaxSize;
        private TrackBar tbTriggerThreshold;
        private TrackBar tbSensitivity;
        private TrackBar tbShrinkSpeed;

        private Label lblMaxSizeVal;
        private Label lblTriggerThresholdVal;
        private Label lblSensitivityVal;
        private Label lblShrinkSpeedVal;

        private CheckBox chkEnabled;
        private CheckBox chkStartup;

        private CheckBox chkHotkeyEnabled;
        private CheckBox chkNotifications;
        private HotkeyTextBox txtHotkey;
        private Label lblHotkeyStatus;

        private readonly TrayApplicationContext _tray;

        private Panel pnlCursorGraphicGroup;
        private Panel pnlDisplayStyleGroup;

        private RadioButton rbHideNative;
        private RadioButton rbStandardOverlay;

        private RadioButton rbUseDefaultCursor;
        private RadioButton rbUseCustomCursor;
        private TextBox txtCustomPath;
        private Button btnBrowseCustom;
        private PictureBox picPreview;
        
        private RadioButton rbHotspotAutoTip;
        private RadioButton rbHotspotCenter;
        private RadioButton rbHotspotTopLeft;
        private Panel pnlCustomControls;

        private Button btnTestShake;
        private Button btnSaveAndClose;
        private Button btnSave;
        private Button btnCancel;
        private Label lblStatus;

        public SettingsForm(Settings settings, ShakeDetector detector, TrayApplicationContext tray)
        {
            _settings = settings;
            _detector = detector;
            _tray = tray;
            _snapshot = settings.Clone();

            InitializeComponent();
            LoadSettingsToUI();
        }

        private void InitializeComponent()
        {
            // Every Location/Size below is a literal 96 DPI pixel value, and the fonts
            // are all in points. In a per-monitor DPI aware process the fonts scale
            // themselves but these bounds do not, so the layout is rescaled explicitly in
            // OnLoad. WinForms AutoScaleMode is deliberately left off: its automatic pass
            // does not run reliably for a hand-built layout like this one.
            this.AutoScaleMode = AutoScaleMode.None;

            this.Text = "Shake to Find Cursor - Settings";
            this.Icon = AppIconHelper.GetAppIcon();
            this.ShowIcon = true;
            this.Size = new Size(580, 1020);
            this.FormBorderStyle = FormBorderStyle.FixedDialog; // Fixed size dialog
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(32, 32, 32); // Modern Dark mode
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            this.AutoScroll = true;

            int leftMargin = 30;
            int rightAlign = 490;
            int currentY = 25;

            // Title Label
            Label lblTitle = new Label
            {
                Text = "Shake to Find Cursor",
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(96, 205, 255), // Windows 11 accent cyan
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblTitle);

            currentY += 40;
            Label lblSubTitle = new Label
            {
                Text = "Enlarges your mouse cursor up to 300px when rapidly shaken.",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(leftMargin, currentY),
                Size = new Size(480, 30)
            };
            this.Controls.Add(lblSubTitle);

            currentY += 38;
            Panel pnlDiv1 = new Panel { BackColor = Color.FromArgb(50, 50, 50), Location = new Point(leftMargin, currentY), Size = new Size(480, 1) };
            this.Controls.Add(pnlDiv1);

            // Enable Toggle
            currentY += 15;
            chkEnabled = new CheckBox
            {
                Text = "Enable Shake to Find Cursor",
                Font = new Font("Segoe UI Semibold", 10.5F),
                ForeColor = Color.White,
                Location = new Point(leftMargin, currentY),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat
            };
            chkEnabled.CheckedChanged += (s, e) => ApplyLive();
            this.Controls.Add(chkEnabled);

            // Start with Windows Toggle
            currentY += 35;
            chkStartup = new CheckBox
            {
                Text = "Start automatically with Windows",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(220, 220, 220),
                Location = new Point(leftMargin, currentY),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat
            };
            // Only mirrored into _settings here; the actual Run-key write happens in
            // Settings.Save(), so nothing touches the registry until Save & Close.
            chkStartup.CheckedChanged += (s, e) => ApplyLive();
            this.Controls.Add(chkStartup);

            // Global shortcut section
            currentY += 38;
            chkHotkeyEnabled = new CheckBox
            {
                Text = "Global shortcut to turn shake on/off (works inside games)",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(220, 220, 220),
                Location = new Point(leftMargin, currentY),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat
            };
            chkHotkeyEnabled.CheckedChanged += (s, e) => { UpdateHotkeyControlsEnabled(); ApplyLive(); };
            this.Controls.Add(chkHotkeyEnabled);

            currentY += 30;
            Label lblHotkeyPrompt = new Label
            {
                Text = "Shortcut:",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(leftMargin + 12, currentY + 5),
                AutoSize = true
            };
            this.Controls.Add(lblHotkeyPrompt);

            txtHotkey = new HotkeyTextBox
            {
                Location = new Point(leftMargin + 85, currentY),
                Size = new Size(165, 26),
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI Semibold", 9.5F)
            };
            txtHotkey.HotkeyChanged += (s, e) => ApplyLive();
            this.Controls.Add(txtHotkey);

            lblHotkeyStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(160, 160, 160),
                Location = new Point(leftMargin + 260, currentY + 5),
                Size = new Size(230, 20)
            };
            this.Controls.Add(lblHotkeyStatus);

            currentY += 32;
            chkNotifications = new CheckBox
            {
                Text = "Show a notification when the shortcut is used",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(leftMargin + 12, currentY),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat
            };
            chkNotifications.CheckedChanged += (s, e) => ApplyLive();
            this.Controls.Add(chkNotifications);

            currentY += 40;
            // Group 1: Cursor Graphic Selection Container Panel
            Label lblCursorType = new Label
            {
                Text = "Enlarged Cursor Graphic:",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.White,
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblCursorType);

            currentY += 25;
            pnlCursorGraphicGroup = new Panel
            {
                Location = new Point(leftMargin + 10, currentY),
                Size = new Size(480, 30),
                BackColor = Color.Transparent
            };
            this.Controls.Add(pnlCursorGraphicGroup);

            rbUseDefaultCursor = new RadioButton
            {
                Text = "Default Arrow Pointer",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(0, 2),
                Size = new Size(190, 25),
                Checked = true
            };
            rbUseDefaultCursor.CheckedChanged += (s, e) => { ToggleCustomControls(); ApplyLive(); };
            pnlCursorGraphicGroup.Controls.Add(rbUseDefaultCursor);

            rbUseCustomCursor = new RadioButton
            {
                Text = "Custom Image / Cursor File",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(200, 2),
                AutoSize = true
            };
            rbUseCustomCursor.CheckedChanged += (s, e) => { ToggleCustomControls(); ApplyLive(); };
            pnlCursorGraphicGroup.Controls.Add(rbUseCustomCursor);

            currentY += 32;
            // Panel container for custom image upload controls
            pnlCustomControls = new Panel
            {
                Location = new Point(leftMargin + 10, currentY),
                Size = new Size(505, 100),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            this.Controls.Add(pnlCustomControls);

            // Preview Thumbnail Box
            picPreview = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(60, 60),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(25, 25, 25)
            };
            pnlCustomControls.Controls.Add(picPreview);

            // Path TextBox
            txtCustomPath = new TextBox
            {
                Location = new Point(80, 12),
                Size = new Size(280, 25),
                ReadOnly = true,
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtCustomPath.TextChanged += (s, e) => ApplyLive();
            pnlCustomControls.Controls.Add(txtCustomPath);

            // Browse Button
            btnBrowseCustom = new Button
            {
                Text = "📁 Browse...",
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(370, 11),
                Size = new Size(95, 28),
                Cursor = Cursors.Hand
            };
            btnBrowseCustom.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            btnBrowseCustom.Click += BtnBrowseCustom_Click;
            pnlCustomControls.Controls.Add(btnBrowseCustom);

            // Hotspot Alignment radios inside panel
            Label lblHotspot = new Label
            {
                Text = "Cursor Tip Hotspot:",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(80, 48),
                AutoSize = true
            };
            pnlCustomControls.Controls.Add(lblHotspot);

            rbHotspotAutoTip = new RadioButton
            {
                Text = "🎯 Auto-Detect Tip (Recommended)",
                Font = new Font("Segoe UI Semibold", 8.5F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(80, 68),
                AutoSize = true,
                Checked = true
            };
            rbHotspotAutoTip.CheckedChanged += (s, e) => ApplyLive();
            pnlCustomControls.Controls.Add(rbHotspotAutoTip);

            rbHotspotCenter = new RadioButton
            {
                Text = "Center (Target)",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.White,
                Location = new Point(300, 68),
                AutoSize = true
            };
            rbHotspotCenter.CheckedChanged += (s, e) => ApplyLive();
            pnlCustomControls.Controls.Add(rbHotspotCenter);

            rbHotspotTopLeft = new RadioButton
            {
                Text = "(0,0) Corner",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.White,
                Location = new Point(408, 68),
                AutoSize = true
            };
            rbHotspotTopLeft.CheckedChanged += (s, e) => ApplyLive();
            pnlCustomControls.Controls.Add(rbHotspotTopLeft);

            currentY += 110;
            // Group 2: Cursor Display Style Container Panel
            Label lblRenderMode = new Label
            {
                Text = "Cursor Display Style:",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.White,
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblRenderMode);

            currentY += 25;
            pnlDisplayStyleGroup = new Panel
            {
                Location = new Point(leftMargin + 10, currentY),
                Size = new Size(480, 58),
                BackColor = Color.Transparent
            };
            this.Controls.Add(pnlDisplayStyleGroup);

            rbHideNative = new RadioButton
            {
                Text = "🌟 Hide Original Cursor While Enlarged (Bazzite / macOS Style)",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(0, 2),
                Size = new Size(460, 25),
                Checked = true
            };
            rbHideNative.CheckedChanged += (s, e) => ApplyLive();
            pnlDisplayStyleGroup.Controls.Add(rbHideNative);

            rbStandardOverlay = new RadioButton
            {
                Text = "Standard Pointer Overlay (Leave native cursor underneath)",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(0, 30),
                Size = new Size(460, 25)
            };
            rbStandardOverlay.CheckedChanged += (s, e) => ApplyLive();
            pnlDisplayStyleGroup.Controls.Add(rbStandardOverlay);

            currentY += 65;
            // Slider 1: Max Cursor Size
            Label lblMaxSize = new Label
            {
                Text = "Maximum Cursor Size (px):",
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblMaxSize);

            lblMaxSizeVal = new Label
            {
                Text = "300 px",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(rightAlign - 60, currentY),
                Size = new Size(70, 20),
                TextAlign = ContentAlignment.TopRight
            };
            this.Controls.Add(lblMaxSizeVal);

            currentY += 25;
            tbMaxSize = new TrackBar
            {
                Minimum = 100,
                Maximum = 500,
                SmallChange = 10,
                LargeChange = 50,
                TickFrequency = 50,
                Location = new Point(leftMargin - 5, currentY),
                Size = new Size(490, 45),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            tbMaxSize.ValueChanged += (s, e) => { lblMaxSizeVal.Text = string.Format("{0} px", tbMaxSize.Value); ApplyLive(); };
            this.Controls.Add(tbMaxSize);

            currentY += 50;
            // Slider 2: Shake Activation Threshold
            Label lblTriggerThreshold = new Label
            {
                Text = "Shake Activation Threshold (Effort to start):",
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblTriggerThreshold);

            lblTriggerThresholdVal = new Label
            {
                Text = "Medium Effort",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(rightAlign - 100, currentY),
                Size = new Size(110, 20),
                TextAlign = ContentAlignment.TopRight
            };
            this.Controls.Add(lblTriggerThresholdVal);

            currentY += 25;
            tbTriggerThreshold = new TrackBar
            {
                Minimum = 5,
                Maximum = 35,
                SmallChange = 1,
                LargeChange = 5,
                TickFrequency = 5,
                Location = new Point(leftMargin - 5, currentY),
                Size = new Size(490, 45),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            tbTriggerThreshold.ValueChanged += (s, e) => { UpdateTriggerThresholdLabel(); ApplyLive(); };
            this.Controls.Add(tbTriggerThreshold);

            currentY += 50;
            // Slider 3: Shake Growth Sensitivity
            Label lblSensitivity = new Label
            {
                Text = "Growth Sensitivity (Enlargement speed):",
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblSensitivity);

            lblSensitivityVal = new Label
            {
                Text = "1.0 x",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(rightAlign - 60, currentY),
                Size = new Size(70, 20),
                TextAlign = ContentAlignment.TopRight
            };
            this.Controls.Add(lblSensitivityVal);

            currentY += 25;
            tbSensitivity = new TrackBar
            {
                Minimum = 2,
                Maximum = 30,
                SmallChange = 1,
                LargeChange = 5,
                TickFrequency = 5,
                Location = new Point(leftMargin - 5, currentY),
                Size = new Size(490, 45),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            tbSensitivity.ValueChanged += (s, e) => { lblSensitivityVal.Text = string.Format("{0:0.0} x", tbSensitivity.Value / 10.0); ApplyLive(); };
            this.Controls.Add(tbSensitivity);

            currentY += 50;
            // Slider 4: Shrink Speed
            Label lblShrink = new Label
            {
                Text = "Shrink Animation Speed:",
                Location = new Point(leftMargin, currentY),
                AutoSize = true
            };
            this.Controls.Add(lblShrink);

            lblShrinkSpeedVal = new Label
            {
                Text = "Normal",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(rightAlign - 150, currentY),
                Size = new Size(160, 20),
                TextAlign = ContentAlignment.TopRight
            };
            this.Controls.Add(lblShrinkSpeedVal);

            currentY += 25;
            tbShrinkSpeed = new TrackBar
            {
                // Slider position on a geometric scale, not the coefficient itself.
                // See Settings.ShrinkSpeedFromSlider.
                Minimum = 0,
                Maximum = Settings.ShrinkSliderMax,
                SmallChange = 2,
                LargeChange = 10,
                TickFrequency = 10,
                Location = new Point(leftMargin - 5, currentY),
                Size = new Size(490, 45),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            tbShrinkSpeed.ValueChanged += (s, e) => { UpdateShrinkSpeedLabel(); ApplyLive(); };
            this.Controls.Add(tbShrinkSpeed);

            currentY += 55;
            // Test Button & Status
            btnTestShake = new Button
            {
                Text = "⚡ Test Shake Animation",
                Font = new Font("Segoe UI Semibold", 9.5F),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.FromArgb(96, 205, 255),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(leftMargin, currentY),
                Size = new Size(200, 36),
                Cursor = Cursors.Hand
            };
            btnTestShake.FlatAppearance.BorderColor = Color.FromArgb(96, 205, 255);
            btnTestShake.Click += (s, e) => { _detector.TriggerSimulatedShake(100.0); };
            this.Controls.Add(btnTestShake);

            lblStatus = new Label
            {
                Text = "Changes apply instantly",
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = Color.FromArgb(160, 160, 160),
                Location = new Point(leftMargin + 215, currentY + 8),
                Size = new Size(270, 25)
            };
            this.Controls.Add(lblStatus);

            currentY += 50;
            Panel pnlDiv2 = new Panel { BackColor = Color.FromArgb(50, 50, 50), Location = new Point(leftMargin, currentY), Size = new Size(480, 1) };
            this.Controls.Add(pnlDiv2);

            currentY += 15;
            // Bottom Left: "Save & Close" button
            btnSaveAndClose = new Button
            {
                Text = "Save & Close",
                UseMnemonic = false,
                Font = new Font("Segoe UI Semibold", 10F),
                BackColor = Color.FromArgb(0, 120, 212), // Windows accent blue
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(leftMargin, currentY),
                Size = new Size(145, 36),
                Cursor = Cursors.Hand
            };
            btnSaveAndClose.FlatAppearance.BorderSize = 0;
            btnSaveAndClose.Click += BtnSaveAndClose_Click;
            this.Controls.Add(btnSaveAndClose);

            // Bottom Right: "Save" (persist, stay open) and "Cancel". There is no Apply
            // button -- every control applies itself the moment it changes.
            btnSave = new Button
            {
                Text = "Save",
                UseMnemonic = false,
                Font = new Font("Segoe UI Semibold", 10F),
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(rightAlign - 205, currentY),
                Size = new Size(90, 36),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);

            btnCancel = new Button
            {
                Text = "Cancel",
                UseMnemonic = false,
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(rightAlign - 105, currentY),
                Size = new Size(105, 36),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            // OnFormClosing rolls the live settings back to the opening snapshot.
            btnCancel.Click += (s, e) => { this.Close(); };
            this.Controls.Add(btnCancel);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            ApplyDpiScaling();

            // FixedDialog cannot be resized, so if the scaled dialog is taller than the
            // monitor work area, clamp it and let AutoScroll take over.
            Rectangle work = Screen.FromControl(this).WorkingArea;
            if (this.Height > work.Height - 40)
            {
                this.Height = work.Height - 40;
                this.Top = work.Top + 20;
            }
        }

        /// <summary>
        /// Scales the hand-built 96 DPI layout to the monitor this dialog opened on.
        /// Only bounds are scaled -- the fonts are declared in points and already render
        /// at the correct physical size, so scaling them too would double-apply the DPI.
        /// </summary>
        private void ApplyDpiScaling()
        {
            if (_dpiScaleApplied) return;
            _dpiScaleApplied = true;

            float dpi = 96F;
            try
            {
                uint windowDpi = NativeMethods.GetDpiForWindow(this.Handle);
                if (windowDpi >= 48 && windowDpi <= 960) dpi = windowDpi;
            }
            catch (EntryPointNotFoundException)
            {
                using (Graphics g = this.CreateGraphics()) { dpi = g.DpiX; }
            }

            float factor = dpi / 96F;
            if (Math.Abs(factor - 1F) < 0.01F) return;

            this.Scale(new SizeF(factor, factor));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_saved)
            {
                // Apply pushes changes live without saving them; closing with Cancel or
                // the title-bar X has to put the previous values back.
                _settings.CopyFrom(_snapshot);
                if (_settings.Mode == RenderMode.OverlayOnly && NativeCursorHelper.IsHidden)
                {
                    NativeCursorHelper.RestoreNativeCursor();
                }
            }
            base.OnFormClosing(e);
        }

        private void UpdateShrinkSpeedLabel()
        {
            double speed = Settings.ShrinkSpeedFromSlider(tbShrinkSpeed.Value);
            int ms = Settings.EstimateShrinkMilliseconds(speed);

            string word;
            if (tbShrinkSpeed.Value <= 25) word = "Slow";
            else if (tbShrinkSpeed.Value <= 70) word = "Normal";
            else word = "Fast";

            lblShrinkSpeedVal.Text = string.Format(CultureInfo.CurrentCulture, "{0} - {1:0.00} s", word, ms / 1000.0);
        }

        private void UpdateTriggerThresholdLabel()
        {
            int val = tbTriggerThreshold.Value;
            if (val <= 8) lblTriggerThresholdVal.Text = "Light Shake";
            else if (val <= 18) lblTriggerThresholdVal.Text = "Medium Effort";
            else lblTriggerThresholdVal.Text = "Vigorous Shake";
        }

        /// <summary>
        /// Every control pushes its change straight into the live settings, so the effect
        /// can be felt by shaking immediately. Nothing is written to disk or to the
        /// registry until Save &amp; Close; closing with Cancel or the title-bar X reverts
        /// everything via the snapshot taken when the dialog opened.
        /// </summary>
        private void ApplyLive()
        {
            if (!_uiReady) return;

            ApplyUIToSettings();
            RefreshHotkeyRegistration();

            // Switching to the non-invasive mode has to give the native cursor back right
            // away rather than waiting for the next shake to end.
            if (_settings.Mode == RenderMode.OverlayOnly && NativeCursorHelper.IsHidden)
            {
                NativeCursorHelper.RestoreNativeCursor();
            }

            lblStatus.Text = "Live preview - Save & Close to keep";
            lblStatus.ForeColor = Color.FromArgb(160, 160, 160);
        }

        private void UpdateHotkeyControlsEnabled()
        {
            bool on = chkHotkeyEnabled.Checked;
            txtHotkey.Enabled = on;
            chkNotifications.Enabled = on;
            if (!on) lblHotkeyStatus.Text = "";
        }

        /// <summary>
        /// Re-registers the shortcut and reports the outcome. RegisterHotKey fails when
        /// another application already owns the combination, and the user needs to be told
        /// rather than left wondering why nothing happens.
        /// </summary>
        private void RefreshHotkeyRegistration()
        {
            if (_tray == null) return;

            if (!chkHotkeyEnabled.Checked)
            {
                _tray.ApplyHotkeySettings();
                lblHotkeyStatus.Text = "";
                return;
            }

            if (!Settings.IsValidHotkey(txtHotkey.Modifiers, txtHotkey.KeyCode))
            {
                lblHotkeyStatus.Text = "Needs Ctrl, Alt or Shift plus a key";
                lblHotkeyStatus.ForeColor = Color.FromArgb(255, 170, 90);
                return;
            }

            if (_tray.ApplyHotkeySettings())
            {
                lblHotkeyStatus.Text = "Shortcut active";
                lblHotkeyStatus.ForeColor = Color.FromArgb(120, 200, 140);
            }
            else
            {
                lblHotkeyStatus.Text = "Already used by another app";
                lblHotkeyStatus.ForeColor = Color.FromArgb(255, 140, 140);
            }
        }

        /// <summary>
        /// Called by the tray when the enabled state changes behind the dialog's back, so
        /// the checkbox cannot drift out of sync with the tray menu or the hotkey.
        /// </summary>
        public void RefreshEnabledState()
        {
            if (chkEnabled == null || chkEnabled.Checked == _settings.Enabled) return;

            bool wasReady = _uiReady;
            _uiReady = false;
            chkEnabled.Checked = _settings.Enabled;
            _uiReady = wasReady;

            // Keep the snapshot in step, otherwise Cancel would undo a toggle the user
            // made deliberately from the tray or the shortcut.
            _snapshot.Enabled = _settings.Enabled;
        }

        private void ToggleCustomControls()
        {
            bool useCustom = rbUseCustomCursor.Checked;
            pnlCustomControls.Enabled = useCustom;
            pnlCustomControls.Visible = useCustom;
        }

        private void BtnBrowseCustom_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Custom Cursor Image File";
                ofd.Filter = "Image & Cursor Files (*.png;*.cur;*.ico;*.jpg;*.bmp)|*.png;*.cur;*.ico;*.jpg;*.bmp|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtCustomPath.Text = ofd.FileName;
                    UpdatePreviewThumbnail(ofd.FileName);
                }
            }
        }

        private void UpdatePreviewThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                SetPreviewImage(null);
                return;
            }

            try
            {
                using (Image img = Image.FromFile(path))
                {
                    SetPreviewImage(new Bitmap(img));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading thumbnail preview: " + ex.Message);
                SetPreviewImage(null);
            }
        }

        /// <summary>Swaps the preview image, disposing the one it replaces.</summary>
        private void SetPreviewImage(Image image)
        {
            Image previous = picPreview.Image;
            picPreview.Image = image;
            if (previous != null) previous.Dispose();
        }

        private void LoadSettingsToUI()
        {
            chkEnabled.Checked = _settings.Enabled;
            chkStartup.Checked = _settings.StartWithWindows;

            chkHotkeyEnabled.Checked = _settings.HotkeyEnabled;
            chkNotifications.Checked = _settings.ShowNotifications;
            txtHotkey.SetHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            UpdateHotkeyControlsEnabled();

            if (_settings.Mode == RenderMode.HideNativeCursor) rbHideNative.Checked = true;
            else rbStandardOverlay.Checked = true;

            // Custom cursor settings
            if (_settings.UseCustomCursor) rbUseCustomCursor.Checked = true;
            else rbUseDefaultCursor.Checked = true;

            txtCustomPath.Text = _settings.CustomCursorPath ?? "";
            UpdatePreviewThumbnail(_settings.CustomCursorPath);

            if (_settings.CustomHotspot == HotspotMode.AutoTip) rbHotspotAutoTip.Checked = true;
            else if (_settings.CustomHotspot == HotspotMode.Center) rbHotspotCenter.Checked = true;
            else rbHotspotTopLeft.Checked = true;

            ToggleCustomControls();

            tbMaxSize.Value = Math.Max(Settings.MinCursorSize, Math.Min(Settings.MaxCursorSizeLimit, _settings.MaxCursorSize));
            lblMaxSizeVal.Text = string.Format("{0} px", tbMaxSize.Value);

            tbTriggerThreshold.Value = Math.Max(5, Math.Min(35, (int)_settings.TriggerThreshold));
            UpdateTriggerThresholdLabel();

            int sensVal = (int)(_settings.Sensitivity * 10.0);
            tbSensitivity.Value = Math.Max(2, Math.Min(30, sensVal));
            lblSensitivityVal.Text = string.Format("{0:0.0} x", tbSensitivity.Value / 10.0);

            tbShrinkSpeed.Value = Settings.SliderFromShrinkSpeed(_settings.ShrinkSpeed);
            UpdateShrinkSpeedLabel();

            _uiReady = true;
        }

        private void ApplyUIToSettings()
        {
            _settings.Enabled = chkEnabled.Checked;
            _settings.StartWithWindows = chkStartup.Checked;

            _settings.HotkeyEnabled = chkHotkeyEnabled.Checked;
            _settings.ShowNotifications = chkNotifications.Checked;
            _settings.HotkeyModifiers = txtHotkey.Modifiers;
            _settings.HotkeyKey = txtHotkey.KeyCode;
            _settings.Mode = rbHideNative.Checked ? RenderMode.HideNativeCursor : RenderMode.OverlayOnly;
            
            _settings.UseCustomCursor = rbUseCustomCursor.Checked;
            _settings.CustomCursorPath = txtCustomPath.Text;
            
            if (rbHotspotAutoTip.Checked) _settings.CustomHotspot = HotspotMode.AutoTip;
            else if (rbHotspotCenter.Checked) _settings.CustomHotspot = HotspotMode.Center;
            else _settings.CustomHotspot = HotspotMode.TopLeftCorner;

            _settings.MaxCursorSize = tbMaxSize.Value;
            _settings.TriggerThreshold = tbTriggerThreshold.Value;
            _settings.Sensitivity = tbSensitivity.Value / 10.0;
            _settings.ShrinkSpeed = Settings.ShrinkSpeedFromSlider(tbShrinkSpeed.Value);
        }

        /// <summary>
        /// Persists without closing. After this the dialog is "clean", so the snapshot is
        /// advanced -- a later Cancel reverts to what was saved, not to what was on screen
        /// when the dialog first opened.
        /// </summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            ApplyUIToSettings();
            _settings.Save();
            _snapshot = _settings.Clone();

            lblStatus.Text = "Saved";
            lblStatus.ForeColor = Color.FromArgb(96, 205, 255);
        }

        private void BtnSaveAndClose_Click(object sender, EventArgs e)
        {
            ApplyUIToSettings();

            if (_settings.Mode == RenderMode.OverlayOnly && NativeCursorHelper.IsHidden)
            {
                NativeCursorHelper.RestoreNativeCursor();
            }

            _settings.Save(); // Writes to config disk file & updates Windows Registry startup
            _snapshot = _settings.Clone();
            _saved = true;
            this.Close();
        }
    }

    // ==========================================
    // System Tray App Context
    // ==========================================
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon _notifyIcon;
        private Settings _settings;
        private ShakeDetector _detector;
        private OverlayForm _overlayForm;
        private Timer _timer;
        private SettingsForm _settingsForm;
        private HotkeyManager _hotkey;
        private ToolStripMenuItem _itemEnabled;

        public TrayApplicationContext()
        {
            _settings = Settings.Load();
            _detector = new ShakeDetector();
            _overlayForm = new OverlayForm(_settings);

            // Pre-cache & snapshot exact 32-bit hardware alpha custom cursors at startup
            NativeCursorHelper.BackupSystemCursors();

            InitializeTray();

            _hotkey = new HotkeyManager();
            _hotkey.Pressed += (s, e) => SetEnabled(!_settings.Enabled, true);
            ApplyHotkeySettings();

            // Main loop timer running at ~100 Hz (10ms) for responsive mouse velocity tracking
            _timer = new Timer();
            _timer.Interval = 10;
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void InitializeTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(32, 32, 32);
            menu.ForeColor = Color.White;
            menu.RenderMode = ToolStripRenderMode.System;

            ToolStripMenuItem itemSettings = new ToolStripMenuItem("⚙️ Settings...", null, (s, e) => ShowSettings());
            ToolStripMenuItem itemTest = new ToolStripMenuItem("⚡ Test Shake Animation", null, (s, e) => _detector.TriggerSimulatedShake(100.0));
            _itemEnabled = new ToolStripMenuItem("Shake Detection Enabled", null, (s, e) => SetEnabled(!_settings.Enabled, false));
            _itemEnabled.Checked = _settings.Enabled;

            ToolStripMenuItem itemExit = new ToolStripMenuItem("❌ Exit", null, (s, e) => ExitApp());

            menu.Items.Add(itemSettings);
            menu.Items.Add(itemTest);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_itemEnabled);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemExit);

            _notifyIcon = new NotifyIcon
            {
                Icon = AppIconHelper.GetAppIcon(),
                ContextMenuStrip = menu,
                Text = "Shake to Find Cursor (Active)",
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => ShowSettings();
        }

        /// <summary>
        /// Single path for enabling/disabling, shared by the tray menu and the global
        /// hotkey, so the tray check mark, the tray tooltip and an open settings dialog
        /// can never disagree about the current state.
        /// </summary>
        private void SetEnabled(bool enabled, bool fromHotkey)
        {
            _settings.Enabled = enabled;

            if (_itemEnabled != null) _itemEnabled.Checked = enabled;
            _notifyIcon.Text = enabled ? "Shake to Find Cursor (Active)" : "Shake to Find Cursor (Disabled)";

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.RefreshEnabledState();
            }
            else
            {
                // Only persist when the dialog is closed. While it is open it owns the
                // live settings, and saving here would quietly commit edits the user has
                // not confirmed with Save yet.
                _settings.Save();
            }

            if (fromHotkey && _settings.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(
                    2500,
                    "Shake to Find Cursor",
                    enabled ? "Shake to find is ON" : "Shake to find is OFF",
                    ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// (Re)registers the global hotkey from the current settings. Called at startup and
        /// whenever the settings dialog changes the shortcut.
        /// </summary>
        public bool ApplyHotkeySettings()
        {
            if (_hotkey == null) return false;

            if (!_settings.HotkeyEnabled)
            {
                _hotkey.Unregister();
                return true;
            }

            return _hotkey.Register(_settings.HotkeyModifiers, _settings.HotkeyKey);
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_settings, _detector, this);
                _settingsForm.Show();
            }
            else
            {
                _settingsForm.BringToFront();
                _settingsForm.Focus();
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _detector.Update(_settings);
            _overlayForm.RenderCursorOverlay(_detector.CurrentScale);
        }

        private void ExitApp()
        {
            _timer.Stop();

            if (_hotkey != null)
            {
                _hotkey.Dispose();
                _hotkey = null;
            }

            NativeCursorHelper.RestoreNativeCursor();

            // Without the explicit Dispose the tray icon lingers as a ghost until the
            // user happens to hover over it.
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            _overlayForm.Close();
            Application.Exit();
        }
    }

    // ==========================================
    // Application Icon Helper
    // ==========================================
    public class AppIconHelper
    {
        private static Icon _cachedIcon;

        public static Icon GetAppIcon()
        {
            if (_cachedIcon != null) return _cachedIcon;

            try
            {
                string icoPath = Path.Combine(Application.StartupPath, "app.ico");
                if (File.Exists(icoPath))
                {
                    _cachedIcon = new Icon(icoPath, 64, 64);
                    return _cachedIcon;
                }
                
                string exePath = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    Icon icon = Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        _cachedIcon = icon;
                        return _cachedIcon;
                    }
                }
            }
            catch { }

            _cachedIcon = CreateProceduralIcon(64);
            return _cachedIcon;
        }

        public static Icon CreateProceduralIcon(int size = 48)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    float s = size / 256.0f;

                    PointF pTip = new PointF(30f * s, 20f * s);
                    PointF pBottom = new PointF(30f * s, 210f * s);
                    PointF pNotch = new PointF(110f * s, 155f * s);
                    PointF pTail = new PointF(165f * s, 225f * s);
                    PointF pTailRight = new PointF(205f * s, 190f * s);
                    PointF pNotchRight = new PointF(145f * s, 130f * s);
                    PointF pRight = new PointF(210f * s, 120f * s);

                    PointF[] arrowPoly = new PointF[]
                    {
                        pTip, pRight, pNotchRight, pTailRight, pTail, pNotch, pBottom
                    };

                    PointF[] topFacet = new PointF[] { pTip, pRight, pNotchRight, pNotch };
                    PointF[] bottomFacet = new PointF[] { pTip, pNotch, pBottom };
                    PointF[] tailFacet = new PointF[] { pNotchRight, pTailRight, pTail, pNotch };

                    Color colorTop = Color.FromArgb(96, 205, 255);      // Vibrant Cyan
                    Color colorBottom = Color.FromArgb(0, 120, 212);    // Windows Blue
                    Color colorTail = Color.FromArgb(252, 142, 56);     // Orange Accent
                    Color colorWhiteBorder = Color.White;

                    using (SolidBrush bTop = new SolidBrush(colorTop))
                        g.FillPolygon(bTop, topFacet);
                    using (SolidBrush bBottom = new SolidBrush(colorBottom))
                        g.FillPolygon(bBottom, bottomFacet);
                    using (SolidBrush bTail = new SolidBrush(colorTail))
                        g.FillPolygon(bTail, tailFacet);

                    using (Pen pWhite = new Pen(colorWhiteBorder, Math.Max(3f, 14f * s)))
                    {
                        pWhite.LineJoin = LineJoin.Round;
                        g.DrawPolygon(pWhite, arrowPoly);
                    }
                }
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    // Icon.FromHandle does not take ownership of the HICON, so clone into
                    // a managed icon and release the GDI handle instead of leaking it.
                    using (Icon tmp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tmp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }

    // ==========================================
    // Main Entry Point
    // ==========================================
    static class Program
    {
        // Local\ scope = per logon session, matching the scope of SetSystemCursor.
        private const string MutexName = "Local\\ShakeToFindCursor_SingleInstance";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (System.Threading.Mutex instanceMutex = new System.Threading.Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Two instances would fight over SetSystemCursor, and whichever exited
                    // first would restore the cursors while the other still believed they
                    // were blanked.
                    MessageBox.Show(
                        "Shake to Find Cursor is already running.\n\nLook for the cursor icon in your system tray, near the clock.",
                        "Shake to Find Cursor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                EnableDpiAwareness();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                InstallCursorSafetyNets();

                // Self-heal. If a previous run was killed while the cursors were blanked,
                // the user is staring at an invisible pointer right now; this reloads their
                // saved scheme before we do anything else.
                NativeCursorHelper.ForceSystemCursorReload();

                try
                {
                    Application.Run(new TrayApplicationContext());
                }
                finally
                {
                    NativeCursorHelper.RestoreNativeCursor();
                }

                GC.KeepAlive(instanceMutex);
            }
        }

        /// <summary>
        /// SetSystemCursor changes the pointer for the whole logon session, not just this
        /// process. If we die while the cursors are blanked the user is left with no
        /// visible pointer at all -- and no way to see what they are clicking in order to
        /// fix it. Every abnormal exit path therefore has to restore first.
        /// </summary>
        private static void InstallCursorSafetyNets()
        {
            Application.ThreadException += (s, e) =>
            {
                NativeCursorHelper.RestoreNativeCursor();
                MessageBox.Show(
                    "Shake to Find Cursor hit an unexpected error and will close.\n\nYour cursor has been restored.\n\n" + e.Exception.Message,
                    "Shake to Find Cursor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) => NativeCursorHelper.RestoreNativeCursor();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => NativeCursorHelper.RestoreNativeCursor();

            // Logging off, shutting down, locking, or switching user: the blanked cursors
            // would otherwise follow the session to the lock screen.
            SystemEvents.SessionEnding += (s, e) => NativeCursorHelper.RestoreNativeCursor();
            SystemEvents.SessionSwitch += (s, e) => NativeCursorHelper.RestoreNativeCursor();
        }

        /// <summary>
        /// Without this the process is DPI-unaware and Windows bitmap-stretches the overlay
        /// on any scaled display, which is exactly the high-DPI case the overlay exists for.
        /// </summary>
        private static void EnableDpiAwareness()
        {
            try
            {
                if (NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                {
                    return;
                }
            }
            catch (EntryPointNotFoundException) { } // pre-Windows 10 1703
            catch (DllNotFoundException) { }

            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException) { }
        }
    }
}
