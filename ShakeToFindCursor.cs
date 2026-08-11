using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShakeToFindCursor
{
    public enum RenderMode
    {
        OverlayOnly = 0, // Standard Overlay Mode (Zero modification to system cursors!)
        HideNativeCursor = 1 // Hide Original Cursor Mode (Blanks all system cursors during shake)
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

        public const uint OCR_NORMAL = 32512;
        public const uint SPI_SETCURSORS = 0x0057;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr hDesktop);

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
        public static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }

    // ==========================================
    // App Settings
    // ==========================================
    public class Settings
    {
        public int MaxCursorSize { get; set; }
        public double Sensitivity { get; set; }
        public double TriggerThreshold { get; set; } // Required shake effort to start enlarging
        public double ShrinkSpeed { get; set; }
        public bool Enabled { get; set; }
        public bool StartWithWindows { get; set; }
        public RenderMode Mode { get; set; }

        // Custom Cursor Image Properties
        public bool UseCustomCursor { get; set; }
        public string CustomCursorPath { get; set; }
        public HotspotMode CustomHotspot { get; set; }
        public double CustomHotspotXPercent { get; set; } // Manual X offset (0.0 to 0.5)
        public double CustomHotspotYPercent { get; set; } // Manual Y offset (0.0 to 0.5)

        public Settings()
        {
            MaxCursorSize = 300; // Cap around 300px by default
            Sensitivity = 1.0;
            TriggerThreshold = 14.0; // Default activation threshold
            ShrinkSpeed = 0.20;
            Enabled = true;
            StartWithWindows = false;
            Mode = RenderMode.HideNativeCursor; // Default mode

            UseCustomCursor = false;
            CustomCursorPath = "";
            CustomHotspot = HotspotMode.AutoTip; // Auto-detect tip of graphic by default!
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

                using (StreamWriter sw = new StreamWriter(ConfigPath))
                {
                    sw.WriteLine(string.Format("MaxCursorSize={0}", MaxCursorSize));
                    sw.WriteLine(string.Format("Sensitivity={0}", Sensitivity));
                    sw.WriteLine(string.Format("TriggerThreshold={0}", TriggerThreshold));
                    sw.WriteLine(string.Format("ShrinkSpeed={0}", ShrinkSpeed));
                    sw.WriteLine(string.Format("Enabled={0}", Enabled));
                    sw.WriteLine(string.Format("StartWithWindows={0}", StartWithWindows));
                    sw.WriteLine(string.Format("RenderMode={0}", (int)Mode));
                    sw.WriteLine(string.Format("UseCustomCursor={0}", UseCustomCursor));
                    sw.WriteLine(string.Format("CustomCursorPath={0}", CustomCursorPath));
                    sw.WriteLine(string.Format("CustomHotspot={0}", (int)CustomHotspot));
                    sw.WriteLine(string.Format("CustomHotspotXPercent={0}", CustomHotspotXPercent));
                    sw.WriteLine(string.Format("CustomHotspotYPercent={0}", CustomHotspotYPercent));
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
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string val = parts[1].Trim();
                            if (key == "MaxCursorSize") { int mcs; if (int.TryParse(val, out mcs)) settings.MaxCursorSize = Math.Max(100, Math.Min(600, mcs)); }
                            if (key == "Sensitivity") { double s; if (double.TryParse(val, out s)) settings.Sensitivity = Math.Max(0.2, Math.Min(3.0, s)); }
                            if (key == "TriggerThreshold") { double tt; if (double.TryParse(val, out tt)) settings.TriggerThreshold = Math.Max(4.0, Math.Min(40.0, tt)); }
                            if (key == "ShrinkSpeed") { double ss; if (double.TryParse(val, out ss)) settings.ShrinkSpeed = Math.Max(0.05, Math.Min(0.5, ss)); }
                            if (key == "Enabled") { bool en; if (bool.TryParse(val, out en)) settings.Enabled = en; }
                            if (key == "StartWithWindows") { bool sww; if (bool.TryParse(val, out sww)) settings.StartWithWindows = sww; }
                            if (key == "RenderMode") { int rm; if (int.TryParse(val, out rm)) settings.Mode = (RenderMode)rm; }
                            if (key == "UseCustomCursor") { bool ucc; if (bool.TryParse(val, out ucc)) settings.UseCustomCursor = ucc; }
                            if (key == "CustomCursorPath") { settings.CustomCursorPath = val; }
                            if (key == "CustomHotspot") { int ch; if (int.TryParse(val, out ch)) settings.CustomHotspot = (HotspotMode)ch; }
                            if (key == "CustomHotspotXPercent") { double hx; if (double.TryParse(val, out hx)) settings.CustomHotspotXPercent = hx; }
                            if (key == "CustomHotspotYPercent") { double hy; if (double.TryParse(val, out hy)) settings.CustomHotspotYPercent = hy; }
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
        private static bool _isHidden = false;
        private static IntPtr _hBlankCursor = IntPtr.Zero;
        private static IntPtr _hCachedArrowCursor = IntPtr.Zero;

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

        public static bool IsHidden { get { return _isHidden; } }

        public static IntPtr GetDefaultArrowCursor()
        {
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

        public static IntPtr GetBlankCursor()
        {
            if (_hBlankCursor == IntPtr.Zero)
            {
                byte[] andMask = new byte[32];
                for (int i = 0; i < andMask.Length; i++) andMask[i] = 0xFF; // 1s = transparent

                byte[] xorMask = new byte[32];
                for (int i = 0; i < xorMask.Length; i++) xorMask[i] = 0x00; // 0s = zero color

                _hBlankCursor = NativeMethods.CreateCursor(IntPtr.Zero, 0, 0, 16, 16, andMask, xorMask);
            }
            return _hBlankCursor;
        }

        public static void HideNativeCursor()
        {
            GetDefaultArrowCursor();

            IntPtr hBlank = GetBlankCursor();
            if (hBlank != IntPtr.Zero)
            {
                foreach (uint id in SystemCursorIds)
                {
                    IntPtr hBlankCopy = NativeMethods.CopyIcon(hBlank);
                    NativeMethods.SetSystemCursor(hBlankCopy, id);
                }
                _isHidden = true;
            }
        }

        public static void RestoreNativeCursor()
        {
            if (!_isHidden) return;

            // Notify Windows 11 DWM with SPIF_SENDCHANGE | SPIF_UPDATEINIFILE to restore full 32-bit hardware alpha cursors & shadows!
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
            _isHidden = false;
        }
    }

    // ==========================================
    // Mouse Shake Detection & Progressive Scale Engine
    // ==========================================
    public class ShakeDetector
    {
        private NativeMethods.POINT _lastPos;
        private int _lastDx = 0;
        private int _lastDy = 0;
        private double _shakeEnergy = 0.0;
        private double _currentScale = 1.0;
        private bool _isFirstSample = true;

        public double CurrentScale { get { return _currentScale; } }

        public void Reset()
        {
            _shakeEnergy = 0;
            _currentScale = 1.0;
            _isFirstSample = true;
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

            // Velocity threshold (minimum 6.0 px per 10ms frame)
            if (dist > 6.0)
            {
                int dotProduct = dx * _lastDx + dy * _lastDy;
                if (dotProduct < 0) // Sharp direction reversal!
                {
                    double addEnergy = dist * 1.5;
                    _shakeEnergy += addEnergy;
                }

                _lastDx = dx;
                _lastDy = dy;
            }

            _lastPos = currentPos;

            // Natural energy decay per sample frame (~10ms)
            _shakeEnergy *= 0.88;
            if (_shakeEnergy < 0.1) _shakeEnergy = 0.0;

            double triggerThreshold = settings.TriggerThreshold;
            double targetScale = 1.0;

            if (_shakeEnergy > triggerThreshold)
            {
                double excess = _shakeEnergy - triggerThreshold;
                double maxScaleFactor = settings.MaxCursorSize / 32.0;

                double scaleAdd = Math.Min(maxScaleFactor - 1.0, excess * 0.035 * settings.Sensitivity);
                targetScale = 1.0 + scaleAdd;
            }

            // Smooth spring / interpolation towards target scale
            if (targetScale > _currentScale)
            {
                _currentScale += (targetScale - _currentScale) * 0.25;
            }
            else
            {
                _currentScale += (targetScale - _currentScale) * settings.ShrinkSpeed;
            }

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

        private PointF AutoDetectGraphicTip(Bitmap bmp)
        {
            if (bmp == null) return new PointF(0, 0);

            int width = bmp.Width;
            int height = bmp.Height;
            int bestX = 0;
            int bestY = 0;
            double minDistanceSquare = double.MaxValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A > 30)
                    {
                        double distSq = x * x + y * y;
                        if (distSq < minDistanceSquare)
                        {
                            minDistanceSquare = distSq;
                            bestX = x;
                            bestY = y;
                        }
                    }
                }
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
                if (_cachedCustomBmp != null) _cachedCustomBmp.Dispose();
                
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
                NativeMethods.CURSORINFO pci = new NativeMethods.CURSORINFO();
                pci.cbSize = Marshal.SizeOf(typeof(NativeMethods.CURSORINFO));
                if (NativeMethods.GetCursorInfo(ref pci))
                {
                    IntPtr hBlank = NativeCursorHelper.GetBlankCursor();
                    if (pci.hCursor != hBlank || !NativeCursorHelper.IsHidden)
                    {
                        NativeCursorHelper.HideNativeCursor();
                    }
                }
            }

            IntPtr hDesk = NativeMethods.OpenInputDesktop(0, false, 0x0100);
            if (hDesk != IntPtr.Zero)
            {
                NativeMethods.SetThreadDesktop(hDesk);
                NativeMethods.CloseDesktop(hDesk);
            }

            NativeMethods.CURSORINFO ci = new NativeMethods.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(NativeMethods.CURSORINFO));
            if (!NativeMethods.GetCursorInfo(ref ci))
            {
                ClearAndHideOverlay();
                return;
            }

            int baseSize = 32;
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
                else // Top-Left (0, 0)
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

            if (!this.Visible)
            {
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

            if (NativeCursorHelper.IsHidden)
            {
                NativeCursorHelper.RestoreNativeCursor();
            }
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
    // Modern Windows 11 Styled Settings Dialog
    // ==========================================
    public class SettingsForm : Form
    {
        private Settings _settings;
        private ShakeDetector _detector;

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
        private Button btnApply;
        private Button btnCancel;
        private Label lblStatus;

        public SettingsForm(Settings settings, ShakeDetector detector)
        {
            _settings = settings;
            _detector = detector;

            InitializeComponent();
            LoadSettingsToUI();
        }

        private void InitializeComponent()
        {
            this.Text = "Shake to Find Cursor - Settings";
            this.Size = new Size(580, 875);
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
            this.Controls.Add(chkStartup);

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
            rbUseDefaultCursor.CheckedChanged += (s, e) => ToggleCustomControls();
            pnlCursorGraphicGroup.Controls.Add(rbUseDefaultCursor);

            rbUseCustomCursor = new RadioButton
            {
                Text = "Custom Image / Cursor File (.png, .cur, .ico, .jpg)",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(200, 2),
                Size = new Size(270, 25)
            };
            rbUseCustomCursor.CheckedChanged += (s, e) => ToggleCustomControls();
            pnlCursorGraphicGroup.Controls.Add(rbUseCustomCursor);

            currentY += 32;
            // Panel container for custom image upload controls
            pnlCustomControls = new Panel
            {
                Location = new Point(leftMargin + 10, currentY),
                Size = new Size(480, 100),
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
                Size = new Size(210, 22),
                Checked = true
            };
            pnlCustomControls.Controls.Add(rbHotspotAutoTip);

            rbHotspotCenter = new RadioButton
            {
                Text = "Center (Target)",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.White,
                Location = new Point(300, 68),
                Size = new Size(110, 22)
            };
            pnlCustomControls.Controls.Add(rbHotspotCenter);

            rbHotspotTopLeft = new RadioButton
            {
                Text = "(0,0) Corner",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.White,
                Location = new Point(410, 68),
                Size = new Size(80, 22)
            };
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
            pnlDisplayStyleGroup.Controls.Add(rbHideNative);

            rbStandardOverlay = new RadioButton
            {
                Text = "Standard Pointer Overlay (Leave native cursor underneath)",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(200, 200, 200),
                Location = new Point(0, 30),
                Size = new Size(460, 25)
            };
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
            tbMaxSize.ValueChanged += (s, e) => { lblMaxSizeVal.Text = string.Format("{0} px", tbMaxSize.Value); };
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
            tbTriggerThreshold.ValueChanged += (s, e) => {
                int val = tbTriggerThreshold.Value;
                if (val <= 8) lblTriggerThresholdVal.Text = "Light Shake";
                else if (val <= 18) lblTriggerThresholdVal.Text = "Medium Effort";
                else lblTriggerThresholdVal.Text = "Vigorous Shake";
            };
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
            tbSensitivity.ValueChanged += (s, e) => { lblSensitivityVal.Text = string.Format("{0:0.0} x", tbSensitivity.Value / 10.0); };
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
                Text = "Medium",
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(96, 205, 255),
                Location = new Point(rightAlign - 60, currentY),
                Size = new Size(70, 20),
                TextAlign = ContentAlignment.TopRight
            };
            this.Controls.Add(lblShrinkSpeedVal);

            currentY += 25;
            tbShrinkSpeed = new TrackBar
            {
                Minimum = 5,
                Maximum = 40,
                SmallChange = 5,
                LargeChange = 10,
                TickFrequency = 5,
                Location = new Point(leftMargin - 5, currentY),
                Size = new Size(490, 45),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            tbShrinkSpeed.ValueChanged += (s, e) => {
                int val = tbShrinkSpeed.Value;
                if (val <= 10) lblShrinkSpeedVal.Text = "Slow";
                else if (val <= 25) lblShrinkSpeedVal.Text = "Normal";
                else lblShrinkSpeedVal.Text = "Fast";
            };
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
                Text = "Ready",
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = Color.FromArgb(160, 160, 160),
                Location = new Point(leftMargin + 215, currentY + 8),
                Size = new Size(260, 25)
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

            // Bottom Right: "Apply" and "Cancel" buttons
            btnApply = new Button
            {
                Text = "Apply",
                UseMnemonic = false,
                Font = new Font("Segoe UI Semibold", 10F),
                BackColor = Color.FromArgb(55, 55, 55),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(rightAlign - 180, currentY),
                Size = new Size(85, 36),
                Cursor = Cursors.Hand
            };
            btnApply.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            btnApply.Click += BtnApply_Click;
            this.Controls.Add(btnApply);

            btnCancel = new Button
            {
                Text = "Cancel",
                UseMnemonic = false,
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(rightAlign - 85, currentY),
                Size = new Size(85, 36),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btnCancel.Click += (s, e) => { this.Close(); };
            this.Controls.Add(btnCancel);
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
                picPreview.Image = null;
                return;
            }

            try
            {
                using (Image img = Image.FromFile(path))
                {
                    picPreview.Image = new Bitmap(img);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading thumbnail preview: " + ex.Message);
                picPreview.Image = null;
            }
        }

        private void LoadSettingsToUI()
        {
            chkEnabled.Checked = _settings.Enabled;
            chkStartup.Checked = _settings.StartWithWindows;

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

            tbMaxSize.Value = Math.Max(100, Math.Min(500, _settings.MaxCursorSize));
            lblMaxSizeVal.Text = string.Format("{0} px", tbMaxSize.Value);

            tbTriggerThreshold.Value = Math.Max(5, Math.Min(35, (int)_settings.TriggerThreshold));
            int ttVal = tbTriggerThreshold.Value;
            if (ttVal <= 8) lblTriggerThresholdVal.Text = "Light Shake";
            else if (ttVal <= 18) lblTriggerThresholdVal.Text = "Medium Effort";
            else lblTriggerThresholdVal.Text = "Vigorous Shake";

            int sensVal = (int)(_settings.Sensitivity * 10.0);
            tbSensitivity.Value = Math.Max(2, Math.Min(30, sensVal));
            lblSensitivityVal.Text = string.Format("{0:0.0} x", tbSensitivity.Value / 10.0);

            int shrinkVal = (int)(_settings.ShrinkSpeed * 100.0);
            tbShrinkSpeed.Value = Math.Max(5, Math.Min(40, shrinkVal));

            if (tbShrinkSpeed.Value <= 10) lblShrinkSpeedVal.Text = "Slow";
            else if (tbShrinkSpeed.Value <= 25) lblShrinkSpeedVal.Text = "Normal";
            else lblShrinkSpeedVal.Text = "Fast";
        }

        private void ApplyUIToSettings()
        {
            _settings.Enabled = chkEnabled.Checked;
            _settings.StartWithWindows = chkStartup.Checked;
            _settings.Mode = rbHideNative.Checked ? RenderMode.HideNativeCursor : RenderMode.OverlayOnly;
            
            _settings.UseCustomCursor = rbUseCustomCursor.Checked;
            _settings.CustomCursorPath = txtCustomPath.Text;
            
            if (rbHotspotAutoTip.Checked) _settings.CustomHotspot = HotspotMode.AutoTip;
            else if (rbHotspotCenter.Checked) _settings.CustomHotspot = HotspotMode.Center;
            else _settings.CustomHotspot = HotspotMode.TopLeftCorner;

            _settings.MaxCursorSize = tbMaxSize.Value;
            _settings.TriggerThreshold = tbTriggerThreshold.Value;
            _settings.Sensitivity = tbSensitivity.Value / 10.0;
            _settings.ShrinkSpeed = tbShrinkSpeed.Value / 100.0;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            ApplyUIToSettings();
            
            // Force immediate refresh of native cursor table if Mode is StandardOverlay or if restoring
            if (_settings.Mode == RenderMode.OverlayOnly && NativeCursorHelper.IsHidden)
            {
                NativeCursorHelper.RestoreNativeCursor();
            }

            lblStatus.Text = "✓ Settings Applied! (Shake to test)";
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

        public TrayApplicationContext()
        {
            _settings = Settings.Load();
            _detector = new ShakeDetector();
            _overlayForm = new OverlayForm(_settings);

            // Pre-cache standard arrow cursor at startup
            NativeCursorHelper.GetDefaultArrowCursor();

            InitializeTray();

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
            ToolStripMenuItem itemEnabled = new ToolStripMenuItem("Shake Detection Enabled", null, (s, e) => ToggleEnabled(s as ToolStripMenuItem));
            itemEnabled.Checked = _settings.Enabled;

            ToolStripMenuItem itemExit = new ToolStripMenuItem("❌ Exit", null, (s, e) => ExitApp());

            menu.Items.Add(itemSettings);
            menu.Items.Add(itemTest);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemEnabled);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemExit);

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateAppIcon(),
                ContextMenuStrip = menu,
                Text = "Shake to Find Cursor (Active)",
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => ShowSettings();
        }

        private Icon CreateAppIcon()
        {
            // Procedurally generate a stylish 32x32 mouse cursor icon for System Tray
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    // Draw a modern cyan cursor arrow with white outline
                    PointF[] arrow = new PointF[]
                    {
                        new PointF(4, 4),
                        new PointF(4, 26),
                        new PointF(10, 20),
                        new PointF(15, 29),
                        new PointF(19, 27),
                        new PointF(14, 18),
                        new PointF(23, 18)
                    };

                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddPolygon(arrow);
                        using (Pen p = new Pen(Color.White, 3))
                        {
                            p.LineJoin = LineJoin.Round;
                            g.DrawPath(p, path);
                        }
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(0, 162, 232)))
                        {
                            g.FillPath(b, path);
                        }
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void ToggleEnabled(ToolStripMenuItem item)
        {
            _settings.Enabled = !_settings.Enabled;
            if (item != null) item.Checked = _settings.Enabled;
            _notifyIcon.Text = _settings.Enabled ? "Shake to Find Cursor (Active)" : "Shake to Find Cursor (Disabled)";
            _settings.Save();
        }

        private void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new SettingsForm(_settings, _detector);
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
            NativeCursorHelper.RestoreNativeCursor();
            _notifyIcon.Visible = false;
            _overlayForm.Close();
            Application.Exit();
        }
    }

    // ==========================================
    // Main Entry Point
    // ==========================================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
        }
    }
}
