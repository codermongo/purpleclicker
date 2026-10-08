using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PurpleClicker
{
    // ------------------------------------------------------------------ Win32
    static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int id, HookProc cb, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint ms);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder sb, ref int size);

        // Dateiname (klein geschrieben) der exe, zu der ein Fenster gehoert
        public static string ExeOfWindow(IntPtr hwnd, out uint pid)
        {
            pid = 0;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return null;
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return null;
                return Path.GetFileName(sb.ToString()).ToLowerInvariant();
            }
            finally { CloseHandle(h); }
        }
    }

    // ------------------------------------------------------------------ Hotkey
    class Hotkey
    {
        public bool IsMouse;
        public int Code; // Maus: 1..5 (Links, Rechts, Mitte, X1, X2) - Tastatur: Virtual-Key-Code

        public Hotkey(bool isMouse, int code) { IsMouse = isMouse; Code = code; }

        public string Name()
        {
            if (IsMouse)
            {
                switch (Code)
                {
                    case 1: return "Maus 1 · Links";
                    case 2: return "Maus 2 · Rechts";
                    case 3: return "Maus 3 · Mitte";
                    case 4: return "Maus 4 · Zurück";
                    default: return "Maus 5 · Vor";
                }
            }
            return KeyName();
        }

        public string Short()
        {
            return IsMouse ? "Maus " + Code : KeyName();
        }

        string KeyName()
        {
            Keys k = (Keys)Code;
            switch (k)
            {
                case Keys.Prior: return "Bild auf";
                case Keys.Next: return "Bild ab";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Capital: return "Feststell";
                case Keys.LControlKey: return "Strg links";
                case Keys.RControlKey: return "Strg rechts";
                case Keys.LShiftKey: return "Shift links";
                case Keys.RShiftKey: return "Shift rechts";
                case Keys.LMenu: return "Alt links";
                case Keys.RMenu: return "Alt rechts";
            }
            return k.ToString();
        }
    }

    // ------------------------------------------------------------------ Klick-Engine
    class Clicker : IDisposable
    {
        public volatile int IntervalMs = 100;
        public volatile bool Right;
        public volatile bool Double;

        volatile bool running;
        volatile bool quit;
        readonly ManualResetEvent gate = new ManualResetEvent(false);
        readonly Thread thread;

        public bool Running { get { return running; } }

        // Multiplikator: zusaetzliche Klicks nach einem echten Klick (eigener Thread, stoert die Auto-Klick-Schleife nicht)
        int pendingBurst;
        readonly AutoResetEvent burstEvent = new AutoResetEvent(false);
        readonly Thread burstThread;
        const int BurstGapMs = 8;

        public Clicker()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();

            burstThread = new Thread(BurstLoop);
            burstThread.IsBackground = true;
            burstThread.Priority = ThreadPriority.AboveNormal;
            burstThread.Start();
        }

        public void Burst(int extraClicks)
        {
            if (extraClicks <= 0) return;
            if (Thread.VolatileRead(ref pendingBurst) > 200) return; // nicht unbegrenzt aufstauen
            Interlocked.Add(ref pendingBurst, extraClicks);
            burstEvent.Set();
        }

        void BurstLoop()
        {
            while (!quit)
            {
                burstEvent.WaitOne();
                Thread.Sleep(15); // echtes Loslassen zuerst durchlassen
                while (!quit && Interlocked.Add(ref pendingBurst, -1) >= 0)
                {
                    Click();
                    Thread.Sleep(BurstGapMs);
                }
                Interlocked.Exchange(ref pendingBurst, 0);
            }
        }

        public void SetRunning(bool v)
        {
            running = v;
            if (v) gate.Set(); else gate.Reset();
        }

        public void Dispose()
        {
            quit = true;
            running = false;
            gate.Set();
            burstEvent.Set();
        }

        void Loop()
        {
            Stopwatch sw = new Stopwatch();
            while (!quit)
            {
                gate.WaitOne();
                if (quit) break;
                if (!running) continue;

                sw.Reset();
                sw.Start();
                Click();
                if (Double)
                {
                    Thread.Sleep(10);
                    if (!running) continue;
                    Click();
                }

                while (running && !quit)
                {
                    double rem = IntervalMs - sw.Elapsed.TotalMilliseconds;
                    if (rem <= 0) break;
                    if (rem > 20) Thread.Sleep(10);
                    else if (rem > 2) Thread.Sleep(1);
                    else Thread.Yield();
                }
            }
        }

        void Click()
        {
            uint down = Right ? 0x0008u : 0x0002u; // RIGHTDOWN / LEFTDOWN
            uint up = Right ? 0x0010u : 0x0004u;   // RIGHTUP / LEFTUP
            Native.INPUT[] a = new Native.INPUT[2];
            a[0].type = 0; // INPUT_MOUSE
            a[0].mi.dwFlags = down;
            a[1].type = 0;
            a[1].mi.dwFlags = up;
            Native.SendInput(2, a, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }

    class ProcItem
    {
        public string Exe;
        public string Title;
        public ProcItem(string exe, string title) { Exe = exe; Title = title; }
    }

    // Dunkles Kontextmenue fuer das Tray-Icon
    class DarkColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(16, 16, 16);
        static readonly Color Sel = Color.FromArgb(44, 44, 44);
        static readonly Color Line = Color.FromArgb(40, 40, 40);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Sel; } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Line; } }
    }

    // Kleine Einblendung oben rechts: ohne Ton, klickdurchlaessig, nimmt dem Spiel keinen Fokus
    class OverlayForm : Form
    {
        const int W = 300, H = 64;
        static readonly Color Bg = Color.FromArgb(14, 14, 14);
        static readonly Color Green = Color.FromArgb(52, 211, 100);
        float cornerRadius = 10f; // logische Pixel; mit DWM-Rundung 8

        readonly float scale;
        readonly Bitmap logo;
        readonly Font fTitle = new Font("Segoe UI Semibold", 10.5f);
        readonly Font fSub = new Font("Segoe UI", 9f);
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        string title = "", sub = "";
        int phase; // 0 = einblenden, 1 = halten, 2 = ausblenden
        int holdUntil;

        public OverlayForm(Bitmap logo, float scale)
        {
            this.logo = logo;
            this.scale = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Bg;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size((int)Math.Round(W * scale), (int)Math.Round(H * scale));
            Opacity = 0;
            timer.Interval = 16;
            timer.Tick += Tick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // TOPMOST | TRANSPARENT (klickdurchlaessig) | TOOLWINDOW (nicht in Alt-Tab) | LAYERED | NOACTIVATE
                cp.ExStyle |= 0x8 | 0x20 | 0x80 | 0x80000 | 0x08000000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = 2; // DWMWCP_ROUND
            if (Native.DwmSetWindowAttribute(Handle, 33, ref pref, 4) == 0)
            {
                int border = 0x64D334; // Gruen (COLORREF = BGR)
                cornerRadius = 8f;
                Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            else
            {
                GraphicsPath p = new GraphicsPath();
                float d = 20f * scale;
                p.AddArc(0, 0, d, d, 180, 90);
                p.AddArc(Width - d, 0, d, d, 270, 90);
                p.AddArc(Width - d, Height - d, d, d, 0, 90);
                p.AddArc(0, Height - d, d, d, 90, 90);
                p.CloseFigure();
                Region = new Region(p);
            }
        }

        public void ShowMessage(string t, string s, Screen screen)
        {
            title = t;
            sub = s;
            Rectangle b = screen.Bounds;
            int margin = (int)Math.Round(20 * scale);
            Location = new Point(b.Right - Width - margin, b.Top + margin);
            Opacity = 0;
            phase = 0;
            if (!Visible) Show();
            // ganz nach oben, ohne zu aktivieren (0x1 NOSIZE | 0x2 NOMOVE | 0x10 NOACTIVATE | 0x40 SHOWWINDOW)
            Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40);
            Invalidate();
            timer.Start();
        }

        void Tick(object sender, EventArgs e)
        {
            if (phase == 0)
            {
                Opacity = Math.Min(0.96, Opacity + 0.10);
                if (Opacity >= 0.96) { phase = 1; holdUntil = Environment.TickCount + 5000; }
            }
            else if (phase == 1)
            {
                if (Environment.TickCount - holdUntil >= 0) phase = 2;
            }
            else
            {
                Opacity = Math.Max(0, Opacity - 0.07);
                if (Opacity <= 0)
                {
                    timer.Stop();
                    Hide();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(scale, scale);
            // gruener Rahmen = "Programm erkannt"
            RectangleF rf = new RectangleF(1.25f, 1.25f, W - 2.5f, H - 2.5f);
            float d = cornerRadius * 2;
            using (GraphicsPath bp = new GraphicsPath())
            using (Pen bpen = new Pen(Green, 2.5f))
            {
                bp.AddArc(rf.X, rf.Y, d, d, 180, 90);
                bp.AddArc(rf.Right - d, rf.Y, d, d, 270, 90);
                bp.AddArc(rf.Right - d, rf.Bottom - d, d, d, 0, 90);
                bp.AddArc(rf.X, rf.Bottom - d, d, d, 90, 90);
                bp.CloseFigure();
                g.DrawPath(bpen, bp);
            }
            if (logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(16, 16, 32, 32));
            }
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(237, 237, 237)))
                    g.DrawString(title, fTitle, b, new Rectangle(60, 9, W - 72, 24), sf);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(140, 140, 140)))
                    g.DrawString(sub, fSub, b, new Rectangle(60, 32, W - 72, 22), sf);
            }
        }

        // Nur zum Testen des Designs
        public void Snapshot(string path)
        {
            IntPtr h = Handle;
            title = "Purple Clicker aktiv";
            sub = "game.exe erkannt";
            using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    enum Hit
    {
        None, Close, Min, Drag, Minus, Plus, Number, Slider,
        BtnLeft, BtnRight, ClkSingle, ClkDouble, HotChange, Start,
        AutoSw, FilterSw, MulSlider, MulSw, ChipX, ChipAdd, PopItem, PopPanel, PopOutside
    }

    // ------------------------------------------------------------------ Fenster
    class MainForm : Form
    {
        // Layout in logischen Pixeln (wird per ScaleTransform auf den Bildschirm-DPI skaliert)
        const int W = 700, H = 506;
        static readonly Rectangle R_Title = new Rectangle(0, 0, W, 56);
        static readonly Rectangle R_Min = new Rectangle(612, 13, 30, 30);
        static readonly Rectangle R_Close = new Rectangle(650, 13, 30, 30);

        static readonly Rectangle R_CardInt = new Rectangle(20, 64, 330, 118);
        static readonly Rectangle R_Minus = new Rectangle(34, 96, 36, 36);
        static readonly Rectangle R_Plus = new Rectangle(300, 96, 36, 36);
        static readonly Rectangle R_Num = new Rectangle(76, 92, 218, 44);
        static readonly Rectangle R_Slider = new Rectangle(26, 148, 318, 20);
        const int SL_X = 34, SL_W = 302, SL_Y = 158;

        static readonly Rectangle R_CardBtn = new Rectangle(366, 64, 314, 56);
        static readonly Rectangle R_BtnL = new Rectangle(482, 75, 91, 34);
        static readonly Rectangle R_BtnR = new Rectangle(573, 75, 91, 34);

        static readonly Rectangle R_CardClk = new Rectangle(366, 126, 314, 56);
        static readonly Rectangle R_ClkS = new Rectangle(482, 137, 91, 34);
        static readonly Rectangle R_ClkD = new Rectangle(573, 137, 91, 34);

        static readonly Rectangle R_CardHot = new Rectangle(20, 194, 330, 56);
        static readonly Rectangle R_HotPill = new Rectangle(104, 205, 230, 34);
        static readonly Rectangle R_HotBtn = new Rectangle(254, 208, 76, 28);

        static readonly Rectangle R_CardSys = new Rectangle(366, 194, 314, 56);
        static readonly Rectangle R_AutoSw = new Rectangle(620, 210, 44, 24);

        static readonly Rectangle R_CardMul = new Rectangle(20, 262, 660, 56);
        static readonly Rectangle R_MulSlider = new Rectangle(186, 268, 350, 44);
        const int MUL_X = 204, MUL_W = 316, MUL_Y = 290;
        static readonly Rectangle R_MulSw = new Rectangle(620, 278, 44, 24);

        static readonly Rectangle R_CardProg = new Rectangle(20, 330, 660, 108);
        static readonly Rectangle R_FilterSw = new Rectangle(620, 338, 44, 24);
        const int CHIP_X0 = 36, CHIP_X1 = 664, CHIP_Y0 = 372, CHIP_H = 26, CHIP_GAP = 8;
        const int MAX_PROGRAMS = 7;

        static readonly Rectangle R_Start = new Rectangle(20, 450, 660, 42);

        static readonly Rectangle R_Pop = new Rectangle(110, 60, 480, 306);
        static readonly Rectangle R_PopList = new Rectangle(118, 104, 464, 240);
        const int POP_ROW = 40, POP_ROWS = 6;

        // Farben (AMOLED)
        static readonly Color C_Bg = Color.FromArgb(0, 0, 0);
        static readonly Color C_Card = Color.FromArgb(16, 16, 16);
        static readonly Color C_CardLine = Color.FromArgb(32, 32, 32);
        static readonly Color C_Field = Color.FromArgb(8, 8, 8);
        static readonly Color C_Btn = Color.FromArgb(28, 28, 28);
        static readonly Color C_BtnHover = Color.FromArgb(44, 44, 44);
        static readonly Color C_Text = Color.FromArgb(237, 237, 237);
        static readonly Color C_Dim = Color.FromArgb(128, 128, 128);
        static readonly Color C_Accent = Color.FromArgb(124, 108, 255);
        static readonly Color C_AccentHover = Color.FromArgb(146, 132, 255);

        readonly Font fTitle = new Font("Segoe UI Semibold", 11f);
        readonly Font fLabel = new Font("Segoe UI Semibold", 8.25f);
        readonly Font fBig = new Font("Segoe UI Light", 24f);
        readonly Font fUnit = new Font("Segoe UI", 10f);
        readonly Font fBtn = new Font("Segoe UI Semibold", 10f);
        readonly Font fChip = new Font("Segoe UI", 9f);
        readonly Font fGlyph = new Font("Segoe UI", 14f);

        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        readonly Clicker clicker = new Clicker();
        Bitmap logo;
        readonly string settingsPath;
        readonly float scale;

        Native.HookProc mouseProc;
        Native.HookProc keyProc;
        IntPtr mouseHook = IntPtr.Zero;
        IntPtr keyHook = IntPtr.Zero;

        Hotkey hotkey = new Hotkey(false, (int)Keys.F6);
        bool capturing;
        readonly bool[] swallowUp = new bool[6];
        readonly HashSet<int> swallowedKeys = new HashSet<int>();

        int intervalMs = 100;
        bool editing;
        string editBuf = "";
        int dragKind;              // 0 = nichts, 1 = Intervall-Slider, 2 = Multiplikator-Slider
        bool multiOn;
        int multiplier = 5;
        Hit hover = Hit.None;
        int hoverIdx = -1;
        int hitIndex = -1;
        int repeatDir;
        readonly System.Windows.Forms.Timer repeatTimer = new System.Windows.Forms.Timer();

        // Programm-Filter / Autostart / Tray
        bool filterOn;
        readonly List<string> programs = new List<string>();
        bool active = true;        // Hotkey darf reagieren
        bool wasMatch;             // gewaehltes Programm war beim letzten Check im Vordergrund
        OverlayForm overlay;
        bool autostart;
        bool popupOpen;
        int popScroll;
        List<ProcItem> popItems = new List<ProcItem>();
        readonly List<Rectangle> chipRects = new List<Rectangle>();
        Rectangle addRect = Rectangle.Empty;
        readonly System.Windows.Forms.Timer fgTimer = new System.Windows.Forms.Timer();
        NotifyIcon notify;
        bool startHidden;
        bool exiting;
        bool servicesStarted;
        readonly bool noServices;
        EventWaitHandle showEvent;

        public MainForm(bool hidden, bool noServices)
        {
            this.startHidden = hidden;
            this.noServices = noServices;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = C_Bg;
            Text = "Purple Clicker";
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                logo = Icon.ToBitmap();
            }
            catch { }
            MaximizeBox = false;
            ClientSize = new Size((int)Math.Round(W * scale), (int)Math.Round(H * scale));

            repeatTimer.Tick += delegate
            {
                repeatTimer.Interval = 60;
                StepInterval(repeatDir);
            };
            fgTimer.Interval = 250;
            fgTimer.Tick += delegate { ForegroundTick(); };

            LoadSettings();
            autostart = GetAutostart();
            ApplyToClicker();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x20000 | 0x80000; // WS_MINIMIZEBOX | WS_SYSMENU (Taskleisten-Klick minimiert)
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        // ---------------------------------------------------------- Start / Ende
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = 2; // DWMWCP_ROUND
            int hr = Native.DwmSetWindowAttribute(Handle, 33, ref pref, 4);
            if (hr == 0)
            {
                int border = 0x202020; // dezenter Rand (COLORREF)
                Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            else
            {
                using (GraphicsPath p = RR(new RectangleF(0, 0, Width, Height), 16f * scale))
                    Region = new Region(p);
            }
            if (!servicesStarted && !noServices)
            {
                servicesStarted = true;
                StartServices();
            }
        }

        void StartServices()
        {
            Native.timeBeginPeriod(1);
            mouseProc = MouseProc;
            keyProc = KeyProc;
            IntPtr mod = Native.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, mod, 0);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, mod, 0);
            if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero)
                MessageBox.Show("Globale Hotkeys konnten nicht installiert werden.", "Purple Clicker");

            // Tray-Icon
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            menu.BackColor = Color.FromArgb(16, 16, 16);
            menu.ForeColor = C_Text;
            menu.Font = new Font("Segoe UI", 9.5f);
            menu.Items.Add("Öffnen", null, delegate { ShowFromTray(); });
            menu.Items.Add("Beenden", null, delegate { ExitApp(); });
            notify = new NotifyIcon();
            notify.Icon = Icon;
            notify.Text = "Purple Clicker";
            notify.ContextMenuStrip = menu;
            notify.DoubleClick += delegate { ShowFromTray(); };
            notify.Visible = true;

            // Zweiter Start der exe holt das laufende Fenster nach vorn
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "PurpleClicker_Show");
                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        showEvent.WaitOne();
                        if (exiting) break;
                        try { BeginInvoke((MethodInvoker)ShowFromTray); } catch { break; }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch { }

            fgTimer.Start();
            ForegroundTick();
        }

        void ShowFromTray()
        {
            startHidden = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                // X versteckt nur ins Tray (Beenden ueber das Tray-Menue)
                e.Cancel = true;
                if (popupOpen) popupOpen = false;
                SaveSettings();
                Hide();
                return;
            }
            base.OnFormClosing(e);
            exiting = true;
            SaveSettings();
            fgTimer.Stop();
            clicker.SetRunning(false);
            clicker.Dispose();
            if (showEvent != null) { try { showEvent.Set(); } catch { } }
            if (notify != null) { notify.Visible = false; notify.Dispose(); notify = null; }
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            mouseHook = keyHook = IntPtr.Zero;
            if (servicesStarted) Native.timeEndPeriod(1);
        }

        // ---------------------------------------------------------- Globale Hooks
        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.MSLLHOOKSTRUCT s = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                if ((s.flags & 1) == 0) // eigene/injizierte Klicks ignorieren
                {
                    int btn = 0;
                    bool down = false;
                    switch ((int)wParam)
                    {
                        case 0x201: btn = 1; down = true; break;
                        case 0x202: btn = 1; break;
                        case 0x204: btn = 2; down = true; break;
                        case 0x205: btn = 2; break;
                        case 0x207: btn = 3; down = true; break;
                        case 0x208: btn = 3; break;
                        case 0x20B: btn = XButton(s.mouseData); down = true; break;
                        case 0x20C: btn = XButton(s.mouseData); break;
                    }
                    if (btn != 0 && HandleMouse(btn, down, new Point(s.pt.X, s.pt.Y)))
                        return (IntPtr)1;
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        static int XButton(uint mouseData)
        {
            return ((mouseData >> 16) & 0xFFFF) == 1 ? 4 : 5;
        }

        bool HandleMouse(int btn, bool down, Point pt)
        {
            if (!down)
            {
                if (swallowUp[btn]) { swallowUp[btn] = false; return true; }
                // Multiplikator: echter Klick (Hook ignoriert eigene) -> N-1 zusaetzliche Klicks nach dem Loslassen
                if (multiOn && multiplier > 1 && active && !capturing &&
                    btn == (clicker.Right ? 2 : 1) && !(Visible && Bounds.Contains(pt)))
                    clicker.Burst(multiplier - 1);
                return false;
            }
            // Links/Rechts im eigenen sichtbaren Fenster nie abfangen, sonst waere die Bedienung blockiert
            if (btn <= 2 && Visible && Bounds.Contains(pt)) return false;

            if (capturing)
            {
                hotkey = new Hotkey(true, btn);
                capturing = false;
                swallowUp[btn] = true;
                Invalidate();
                return true;
            }
            if (!active) return false;
            if (hotkey.IsMouse && hotkey.Code == btn)
            {
                swallowUp[btn] = true;
                Toggle();
                return true;
            }
            return false;
        }

        IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                if ((k.flags & 0x10) == 0)
                {
                    int msg = (int)wParam;
                    bool down = msg == 0x100 || msg == 0x104;
                    bool up = msg == 0x101 || msg == 0x105;
                    if ((down || up) && HandleKey((int)k.vkCode, down))
                        return (IntPtr)1;
                }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        bool HandleKey(int vk, bool down)
        {
            if (!down)
                return swallowedKeys.Remove(vk);

            if (capturing)
            {
                capturing = false;
                swallowedKeys.Add(vk);
                if (vk != (int)Keys.Escape) hotkey = new Hotkey(false, vk);
                Invalidate();
                return true;
            }
            if (!active) return false;
            if (!hotkey.IsMouse && hotkey.Code == vk)
            {
                if (swallowedKeys.Add(vk)) Toggle(); // Auto-Repeat nur schlucken
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------- Vordergrund-Erkennung
        void ForegroundTick()
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint pid = 0;
            string exe = fg == IntPtr.Zero ? null : Native.ExeOfWindow(fg, out pid);
            bool own = pid != 0 && pid == OwnPid;

            bool match = false;
            if (exe != null)
                foreach (string p in programs)
                    if (p == exe) { match = true; break; }

            bool newActive = !filterOn || own || match;

            // Overlay jedes Mal, wenn ein gewaehltes Programm in den Vordergrund kommt
            if (filterOn && match && !wasMatch)
                ShowOverlay(exe, fg);
            wasMatch = match;

            if (newActive != active)
            {
                active = newActive;
                if (!active) clicker.SetRunning(false); // ohne Meldung deaktivieren
                Invalidate();
            }
        }

        // Kleines Overlay oben rechts (ohne Ton, klickdurchlaessig, nimmt keinen Fokus)
        void ShowOverlay(string exe, IntPtr fg)
        {
            try
            {
                if (overlay == null || overlay.IsDisposed) overlay = new OverlayForm(logo, scale);
                overlay.ShowMessage("Purple Clicker aktiv", exe + " erkannt", Screen.FromHandle(fg));
            }
            catch { }
        }

        // Laufende Programme mit sichtbarem Fenster
        List<ProcItem> BuildProcList()
        {
            List<ProcItem> list = new List<ProcItem>();
            HashSet<string> seen = new HashSet<string>();
            uint ownPid = OwnPid;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h)) return true;
                int len = Native.GetWindowTextLength(h);
                if (len == 0) return true;
                if ((Native.GetWindowLong(h, -20) & 0x80) != 0) return true;   // WS_EX_TOOLWINDOW
                if (Native.GetWindow(h, 4) != IntPtr.Zero) return true;         // Besitzer-Fenster
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                uint pid;
                string exe = Native.ExeOfWindow(h, out pid);
                if (exe == null || pid == ownPid) return true;
                if (programs.Contains(exe) || !seen.Add(exe)) return true;
                StringBuilder sb = new StringBuilder(len + 1);
                Native.GetWindowText(h, sb, sb.Capacity);
                list.Add(new ProcItem(exe, sb.ToString()));
                return true;
            }, IntPtr.Zero);
            list.Sort(delegate(ProcItem a, ProcItem b) { return string.Compare(a.Exe, b.Exe, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        // ---------------------------------------------------------- Autostart
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "PurpleClicker";

        static bool GetAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        static void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        // ---------------------------------------------------------- Logik
        void Toggle()
        {
            CommitEdit();
            clicker.SetRunning(!clicker.Running);
            Invalidate();
        }

        void ApplyToClicker()
        {
            clicker.IntervalMs = intervalMs;
        }

        void SetInterval(int v)
        {
            if (v < 1) v = 1;
            if (v > 600000) v = 600000;
            intervalMs = v;
            ApplyToClicker();
            Invalidate();
        }

        static int StepFor(int v)
        {
            if (v < 20) return 1;
            if (v < 100) return 5;
            if (v < 1000) return 10;
            return 100;
        }

        void StepInterval(int dir)
        {
            int step = StepFor(dir < 0 ? intervalMs - 1 : intervalMs);
            SetInterval(intervalMs + dir * step);
        }

        void CommitEdit()
        {
            if (!editing) return;
            editing = false;
            int v;
            if (editBuf.Length > 0 && int.TryParse(editBuf, out v)) SetInterval(v);
            Invalidate();
        }

        // ---------------------------------------------------------- Eingabe
        Point Logical(Point p)
        {
            return new Point((int)(p.X / scale), (int)(p.Y / scale));
        }

        Hit HitTest(Point p)
        {
            hitIndex = -1;
            if (R_Close.Contains(p)) return Hit.Close;
            if (R_Min.Contains(p)) return Hit.Min;
            if (R_Title.Contains(p)) return Hit.Drag;

            if (popupOpen)
            {
                if (R_PopList.Contains(p))
                {
                    int i = popScroll + (p.Y - R_PopList.Y) / POP_ROW;
                    if (i < popItems.Count) { hitIndex = i; return Hit.PopItem; }
                    return Hit.PopPanel;
                }
                return R_Pop.Contains(p) ? Hit.PopPanel : Hit.PopOutside;
            }

            if (R_Minus.Contains(p)) return Hit.Minus;
            if (R_Plus.Contains(p)) return Hit.Plus;
            if (R_Num.Contains(p)) return Hit.Number;
            if (R_Slider.Contains(p)) return Hit.Slider;
            if (R_MulSw.Contains(p)) return Hit.MulSw;
            if (R_MulSlider.Contains(p)) return Hit.MulSlider;
            if (R_BtnL.Contains(p)) return Hit.BtnLeft;
            if (R_BtnR.Contains(p)) return Hit.BtnRight;
            if (R_ClkS.Contains(p)) return Hit.ClkSingle;
            if (R_ClkD.Contains(p)) return Hit.ClkDouble;
            if (R_HotBtn.Contains(p)) return Hit.HotChange;
            if (R_CardSys.Contains(p)) return Hit.AutoSw;
            if (R_FilterSw.Contains(p)) return Hit.FilterSw;
            for (int i = 0; i < chipRects.Count; i++)
            {
                Rectangle c = chipRects[i];
                if (c.Contains(p))
                {
                    if (p.X >= c.Right - 26) { hitIndex = i; return Hit.ChipX; }
                    return Hit.None;
                }
            }
            if (addRect.Contains(p) && programs.Count < MAX_PROGRAMS) return Hit.ChipAdd;
            if (R_Start.Contains(p)) return Hit.Start;
            return Hit.None;
        }

        void MulTo(int x)
        {
            double t = (x - MUL_X) / (double)MUL_W;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            multiplier = 1 + (int)Math.Round(t * 9);
            Invalidate();
        }

        void SliderTo(int x)
        {
            double t = (x - SL_X) / (double)SL_W;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            SetInterval(1 + (int)Math.Round(t * 999));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Point p = Logical(e.Location);
            Hit h = HitTest(p);
            int idx = hitIndex;

            if (capturing && h != Hit.HotChange) { capturing = false; Invalidate(); }
            if (editing && h != Hit.Number) CommitEdit();

            switch (h)
            {
                case Hit.Close: Close(); break;
                case Hit.Min: WindowState = FormWindowState.Minimized; break;
                case Hit.Drag:
                    Native.ReleaseCapture();
                    Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
                    break;
                case Hit.Minus:
                case Hit.Plus:
                    repeatDir = h == Hit.Minus ? -1 : 1;
                    StepInterval(repeatDir);
                    repeatTimer.Interval = 400;
                    repeatTimer.Start();
                    break;
                case Hit.Number:
                    editing = true;
                    editBuf = "";
                    Focus();
                    break;
                case Hit.Slider:
                    dragKind = 1;
                    SliderTo(p.X);
                    break;
                case Hit.BtnLeft: clicker.Right = false; break;
                case Hit.BtnRight: clicker.Right = true; break;
                case Hit.ClkSingle: clicker.Double = false; break;
                case Hit.ClkDouble: clicker.Double = true; break;
                case Hit.HotChange: capturing = !capturing; break;
                case Hit.Start: Toggle(); break;
                case Hit.MulSw: multiOn = !multiOn; break;
                case Hit.MulSlider:
                    dragKind = 2;
                    MulTo(p.X);
                    break;
                case Hit.AutoSw:
                    SetAutostart(!autostart);
                    autostart = GetAutostart();
                    break;
                case Hit.FilterSw:
                    filterOn = !filterOn;
                    ForegroundTick();
                    break;
                case Hit.ChipX:
                    if (idx >= 0 && idx < programs.Count) programs.RemoveAt(idx);
                    wasMatch = false;
                    ForegroundTick();
                    break;
                case Hit.ChipAdd:
                    popItems = BuildProcList();
                    popScroll = 0;
                    popupOpen = true;
                    break;
                case Hit.PopItem:
                    if (idx >= 0 && idx < popItems.Count && programs.Count < MAX_PROGRAMS)
                    {
                        programs.Add(popItems[idx].Exe);
                        filterOn = true; // Auswahl aktiviert den Filter
                        wasMatch = false;
                        popupOpen = false;
                        ForegroundTick();
                    }
                    break;
                case Hit.PopOutside: popupOpen = false; break;
            }
            hover = HitTest(p);
            hoverIdx = hitIndex;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = Logical(e.Location);
            if (dragKind == 1) SliderTo(p.X);
            else if (dragKind == 2) MulTo(p.X);
            Hit h = HitTest(p);
            int idx = hitIndex;
            if (h != hover || idx != hoverIdx)
            {
                hover = h;
                hoverIdx = idx;
                bool hand = h != Hit.None && h != Hit.Drag && h != Hit.PopPanel && h != Hit.PopOutside;
                Cursor = hand ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragKind = 0;
            repeatTimer.Stop();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != Hit.None) { hover = Hit.None; hoverIdx = -1; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (popupOpen)
            {
                int max = Math.Max(0, popItems.Count - POP_ROWS);
                popScroll = Math.Min(max, Math.Max(0, popScroll + (e.Delta > 0 ? -1 : 1)));
                Invalidate();
                return;
            }
            if (R_CardInt.Contains(Logical(e.Location)))
                StepInterval(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (popupOpen && e.KeyCode == Keys.Escape)
            {
                popupOpen = false;
                Invalidate();
                e.Handled = true;
                return;
            }
            if (!editing) return;
            int d = -1;
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) d = e.KeyCode - Keys.D0;
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) d = e.KeyCode - Keys.NumPad0;

            if (d >= 0 && editBuf.Length < 6) editBuf += d.ToString();
            else if (e.KeyCode == Keys.Back && editBuf.Length > 0) editBuf = editBuf.Substring(0, editBuf.Length - 1);
            else if (e.KeyCode == Keys.Enter) CommitEdit();
            else if (e.KeyCode == Keys.Escape) editing = false;
            Invalidate();
            e.Handled = true;
        }

        // ---------------------------------------------------------- Zeichnen
        static GraphicsPath RR(RectangleF r, float rad)
        {
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void FillRR(Graphics g, Color c, Rectangle r, float rad)
        {
            using (GraphicsPath p = RR(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        static void StrokeRR(Graphics g, Color c, Rectangle r, float rad, float w)
        {
            RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (GraphicsPath p = RR(rf, rad))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, StringAlignment h)
        {
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(c))
            {
                sf.Alignment = h;
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, r, sf);
            }
        }

        // Karte mit Label oben links
        void Card(Graphics g, Rectangle r, string label)
        {
            FillRR(g, C_Card, r, 14);
            StrokeRR(g, C_CardLine, r, 14, 1f);
            DrawText(g, label, fLabel, C_Dim, new Rectangle(r.X + 16, r.Y + 8, 220, 20), StringAlignment.Near);
        }

        // Einzeilige Karte mit Label links, vertikal zentriert
        void CardInline(Graphics g, Rectangle r, string label)
        {
            FillRR(g, C_Card, r, 14);
            StrokeRR(g, C_CardLine, r, 14, 1f);
            DrawText(g, label, fLabel, C_Dim, new Rectangle(r.X + 16, r.Y, 130, r.Height), StringAlignment.Near);
        }

        void RoundBtn(Graphics g, Rectangle r, bool hot, string glyph)
        {
            using (SolidBrush b = new SolidBrush(hot ? C_BtnHover : C_Btn))
                g.FillEllipse(b, r);
            DrawText(g, glyph, fGlyph, C_Text, r, StringAlignment.Center);
        }

        void Segment(Graphics g, Rectangle left, Rectangle right, string tl, string tr, bool rightSel, Hit hl, Hit hr)
        {
            Rectangle all = Rectangle.Union(left, right);
            float rad = all.Height / 2f;
            FillRR(g, C_Field, all, rad);
            StrokeRR(g, C_CardLine, all, rad, 1f);
            Rectangle sel = rightSel ? right : left;
            sel.Inflate(-3, -3);
            FillRR(g, hover == (rightSel ? hr : hl) ? C_AccentHover : C_Accent, sel, rad - 3);
            DrawText(g, tl, fBtn, !rightSel ? Color.White : (hover == hl ? C_Text : C_Dim), left, StringAlignment.Center);
            DrawText(g, tr, fBtn, rightSel ? Color.White : (hover == hr ? C_Text : C_Dim), right, StringAlignment.Center);
        }

        void Switch(Graphics g, Rectangle r, bool on, bool hot)
        {
            Color track = on ? (hot ? C_AccentHover : C_Accent) : (hot ? Color.FromArgb(58, 58, 58) : Color.FromArgb(44, 44, 44));
            FillRR(g, track, r, r.Height / 2f);
            int d = r.Height - 6;
            int kx = on ? r.Right - d - 3 : r.X + 3;
            using (SolidBrush b = new SolidBrush(on ? Color.White : Color.FromArgb(160, 160, 160)))
                g.FillEllipse(b, kx, r.Y + 3, d, d);
        }

        // Chips der gewaehlten Programme + "Hinzufuegen"; speichert die Rechtecke fuer die Treffererkennung
        void DrawChips(Graphics g)
        {
            chipRects.Clear();
            addRect = Rectangle.Empty;
            int x = CHIP_X0, y = CHIP_Y0;
            float alpha = filterOn ? 1f : 0.55f;
            for (int i = 0; i < programs.Count; i++)
            {
                string name = programs[i];
                float tw = g.MeasureString(name, fChip, 2000, StringFormat.GenericTypographic).Width;
                int w = (int)Math.Min(tw + 14 + 40, 220);
                if (x + w > CHIP_X1) { x = CHIP_X0; y += CHIP_H + CHIP_GAP; }
                Rectangle r = new Rectangle(x, y, w, CHIP_H);
                chipRects.Add(r);
                FillRR(g, C_Btn, r, CHIP_H / 2f);
                StrokeRR(g, C_CardLine, r, CHIP_H / 2f, 1f);
                DrawText(g, name, fChip, filterOn ? C_Text : C_Dim, new Rectangle(r.X + 12, r.Y, r.Width - 34, r.Height), StringAlignment.Near);
                // X
                bool xh = hover == Hit.ChipX && hoverIdx == i;
                Rectangle xr = new Rectangle(r.Right - 24, r.Y + 4, 18, 18);
                if (xh) using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 60, 60))) g.FillEllipse(b, xr);
                using (Pen pen = new Pen(xh ? Color.White : C_Dim, 1.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, xr.X + 6, xr.Y + 6, xr.X + 12, xr.Y + 12);
                    g.DrawLine(pen, xr.X + 12, xr.Y + 6, xr.X + 6, xr.Y + 12);
                }
                x += w + CHIP_GAP;
            }
            if (programs.Count < MAX_PROGRAMS)
            {
                const string addText = "+  Hinzufügen";
                float tw = g.MeasureString(addText, fChip, 2000, StringFormat.GenericTypographic).Width;
                int w = (int)tw + 28;
                if (x + w > CHIP_X1) { x = CHIP_X0; y += CHIP_H + CHIP_GAP; }
                addRect = new Rectangle(x, y, w, CHIP_H);
                bool hot = hover == Hit.ChipAdd;
                FillRR(g, hot ? Color.FromArgb(36, 124, 108, 255) : C_Field, addRect, CHIP_H / 2f);
                StrokeRR(g, C_Accent, addRect, CHIP_H / 2f, 1f);
                DrawText(g, addText, fChip, C_Accent, addRect, StringAlignment.Center);
            }
        }

        void DrawPopup(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(b, 0, 56, W, H - 56);
            FillRR(g, C_Card, R_Pop, 16);
            StrokeRR(g, C_Accent, R_Pop, 16, 1f);
            DrawText(g, "LAUFENDES PROGRAMM WÄHLEN", fLabel, C_Dim, new Rectangle(R_Pop.X + 20, R_Pop.Y + 10, 300, 28), StringAlignment.Near);

            if (popItems.Count == 0)
            {
                DrawText(g, "Keine weiteren Programme mit Fenster gefunden.", fBtn, C_Dim, R_PopList, StringAlignment.Center);
            }
            else
            {
                g.SetClip(R_PopList);
                for (int row = 0; row < POP_ROWS; row++)
                {
                    int i = popScroll + row;
                    if (i >= popItems.Count) break;
                    Rectangle r = new Rectangle(R_PopList.X, R_PopList.Y + row * POP_ROW, R_PopList.Width, POP_ROW - 4);
                    bool hot = hover == Hit.PopItem && hoverIdx == i;
                    FillRR(g, hot ? C_BtnHover : C_Btn, r, 10);
                    DrawText(g, popItems[i].Exe, fBtn, C_Text, new Rectangle(r.X + 14, r.Y + 2, 150, r.Height - 4), StringAlignment.Near);
                    DrawText(g, popItems[i].Title, fChip, C_Dim, new Rectangle(r.X + 170, r.Y + 2, r.Width - 184, r.Height - 4), StringAlignment.Near);
                }
                g.ResetClip();
            }
            string hint = popItems.Count > POP_ROWS ? "Mausrad = scrollen  ·  Esc = schließen" : "Esc = schließen";
            DrawText(g, hint, fChip, C_Dim, new Rectangle(R_Pop.X + 20, R_Pop.Bottom - 26, R_Pop.Width - 40, 20), StringAlignment.Far);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(C_Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(scale, scale);

            bool running = clicker.Running;

            // Titelleiste
            int titleX = 24;
            if (logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(22, 16, 24, 24));
                titleX = 54;
            }
            DrawText(g, "Purple Clicker", fTitle, C_Text, new Rectangle(titleX, 14, 160, 28), StringAlignment.Near);
            float titleW = g.MeasureString("Purple Clicker", fTitle, 2000, StringFormat.GenericTypographic).Width;
            Color dot = running ? C_Accent : (active ? Color.FromArgb(200, 200, 200) : Color.FromArgb(64, 64, 64));
            using (SolidBrush b = new SolidBrush(dot))
                g.FillEllipse(b, titleX + titleW + 12, 24, 8, 8);
            string status = running ? "läuft" : (active ? "bereit" : "wartet auf Programm");
            DrawText(g, status, fLabel, C_Dim, new Rectangle((int)(titleX + titleW + 26), 14, 220, 28), StringAlignment.Near);

            using (SolidBrush b = new SolidBrush(hover == Hit.Min ? C_BtnHover : C_Btn))
                g.FillEllipse(b, R_Min);
            using (SolidBrush b = new SolidBrush(hover == Hit.Close ? Color.FromArgb(214, 60, 72) : C_Btn))
                g.FillEllipse(b, R_Close);
            using (Pen pen = new Pen(C_Text, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, R_Min.X + 10, R_Min.Y + 15, R_Min.X + 20, R_Min.Y + 15);
                g.DrawLine(pen, R_Close.X + 10, R_Close.Y + 10, R_Close.X + 20, R_Close.Y + 20);
                g.DrawLine(pen, R_Close.X + 20, R_Close.Y + 10, R_Close.X + 10, R_Close.Y + 20);
            }

            // Intervall
            Card(g, R_CardInt, "INTERVALL");
            RoundBtn(g, R_Minus, hover == Hit.Minus, "−");
            RoundBtn(g, R_Plus, hover == Hit.Plus, "+");

            string num = editing ? (editBuf.Length > 0 ? editBuf : "_") : intervalMs.ToString();
            Color numColor = editing ? C_Accent : C_Text;
            SizeF numSz = g.MeasureString(num, fBig, 2000, StringFormat.GenericTypographic);
            SizeF unitSz = g.MeasureString("ms", fUnit, 2000, StringFormat.GenericTypographic);
            float total = numSz.Width + 6 + unitSz.Width;
            float x0 = R_Num.X + (R_Num.Width - total) / 2f;
            float ny = R_Num.Y + (R_Num.Height - numSz.Height) / 2f;
            using (SolidBrush b = new SolidBrush(numColor))
                g.DrawString(num, fBig, b, x0, ny, StringFormat.GenericTypographic);
            using (SolidBrush b = new SolidBrush(C_Dim))
                g.DrawString("ms", fUnit, b, x0 + numSz.Width + 6, ny + numSz.Height - unitSz.Height - 4, StringFormat.GenericTypographic);

            // Slider
            double t = (Math.Min(Math.Max(intervalMs, 1), 1000) - 1) / 999.0;
            int tx = SL_X + (int)Math.Round(t * SL_W);
            FillRR(g, Color.FromArgb(34, 34, 34), new Rectangle(SL_X, SL_Y - 3, SL_W, 6), 3);
            FillRR(g, C_Accent, new Rectangle(SL_X, SL_Y - 3, Math.Max(tx - SL_X, 6), 6), 3);
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, tx - 8, SL_Y - 8, 16, 16);
            using (Pen pen = new Pen(C_Accent, 2f))
                g.DrawEllipse(pen, tx - 8, SL_Y - 8, 16, 16);

            // Maustaste / Klick-Art
            CardInline(g, R_CardBtn, "MAUSTASTE");
            Segment(g, R_BtnL, R_BtnR, "Links", "Rechts", clicker.Right, Hit.BtnLeft, Hit.BtnRight);
            CardInline(g, R_CardClk, "KLICK-ART");
            Segment(g, R_ClkS, R_ClkD, "Single", "Double", clicker.Double, Hit.ClkSingle, Hit.ClkDouble);

            // Hotkey
            CardInline(g, R_CardHot, "HOTKEY");
            FillRR(g, C_Field, R_HotPill, R_HotPill.Height / 2f);
            StrokeRR(g, capturing ? C_Accent : C_CardLine, R_HotPill, R_HotPill.Height / 2f, capturing ? 1.5f : 1f);
            if (capturing)
                DrawText(g, "Taste drücken…", fBtn, C_Accent,
                    new Rectangle(R_HotPill.X + 16, R_HotPill.Y, 128, R_HotPill.Height), StringAlignment.Near);
            else
                DrawText(g, hotkey.Name(), fBtn, C_Text,
                    new Rectangle(R_HotPill.X + 16, R_HotPill.Y, 128, R_HotPill.Height), StringAlignment.Near);
            FillRR(g, hover == Hit.HotChange ? C_BtnHover : C_Btn, R_HotBtn, 14);
            DrawText(g, capturing ? "Abbruch" : "Ändern", fBtn, C_Text, R_HotBtn, StringAlignment.Center);

            // System: Autostart
            CardInline(g, R_CardSys, "AUTOSTART");
            DrawText(g, "Mit Windows starten", fChip, autostart ? C_Text : C_Dim,
                new Rectangle(R_CardSys.X + 100, R_CardSys.Y, 200, R_CardSys.Height), StringAlignment.Near);
            Switch(g, R_AutoSw, autostart, hover == Hit.AutoSw);

            // Multiplikator: 1 echter Klick = N Klicks
            CardInline(g, R_CardMul, "MULTIPLIKATOR");
            double mt = (multiplier - 1) / 9.0;
            int mx = MUL_X + (int)Math.Round(mt * MUL_W);
            Color mulColor = multiOn ? C_Accent : Color.FromArgb(70, 70, 70);
            FillRR(g, Color.FromArgb(34, 34, 34), new Rectangle(MUL_X, MUL_Y - 3, MUL_W, 6), 3);
            FillRR(g, mulColor, new Rectangle(MUL_X, MUL_Y - 3, Math.Max(mx - MUL_X, 6), 6), 3);
            using (SolidBrush b = new SolidBrush(multiOn ? Color.White : Color.FromArgb(150, 150, 150)))
                g.FillEllipse(b, mx - 8, MUL_Y - 8, 16, 16);
            using (Pen pen = new Pen(mulColor, 2f))
                g.DrawEllipse(pen, mx - 8, MUL_Y - 8, 16, 16);
            for (int i = 0; i < 10; i++)
            {
                int tx2 = MUL_X + (int)Math.Round(i / 9.0 * MUL_W);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(i < multiplier ? 90 : 50, 150, 150, 150)))
                    g.FillEllipse(b, tx2 - 1, MUL_Y + 12, 3, 3);
            }
            DrawText(g, multiplier + "×", fBtn, multiOn ? C_Accent : C_Dim, new Rectangle(R_CardMul.X + 508, R_CardMul.Y, 70, R_CardMul.Height), StringAlignment.Center);
            Switch(g, R_MulSw, multiOn, hover == Hit.MulSw);

            // Nur in Programmen
            Card(g, R_CardProg, "NUR IN PROGRAMMEN");
            string hint = !filterOn ? "Aus – der Hotkey wirkt überall"
                : (programs.Count == 0 ? "Noch kein Programm gewählt" : "Hotkey nur aktiv, wenn eines davon im Vordergrund ist");
            DrawText(g, hint, fChip, C_Dim, new Rectangle(R_CardProg.X + 150, R_CardProg.Y + 8, 440, 20), StringAlignment.Near);
            Switch(g, R_FilterSw, filterOn, hover == Hit.FilterSw);
            DrawChips(g);

            // Start/Stop
            if (running)
            {
                for (int i = 4; i >= 1; i--)
                {
                    Rectangle gr = R_Start;
                    gr.Inflate(i * 2, i * 2 > 6 ? 3 : i);
                    using (GraphicsPath p = RR(gr, 21 + i * 2))
                    using (Pen pen = new Pen(Color.FromArgb(18 * (5 - i), C_Accent), 2f))
                        g.DrawPath(pen, p);
                }
                FillRR(g, hover == Hit.Start ? Color.FromArgb(20, 20, 24) : C_Bg, R_Start, 21);
                StrokeRR(g, C_Accent, R_Start, 21, 1.5f);
                DrawText(g, "STOP  ·  " + hotkey.Short(), fBtn, C_Accent, R_Start, StringAlignment.Center);
            }
            else
            {
                FillRR(g, hover == Hit.Start ? C_AccentHover : C_Accent, R_Start, 21);
                DrawText(g, "START  ·  " + hotkey.Short(), fBtn, Color.White, R_Start, StringAlignment.Center);
            }

            if (popupOpen) DrawPopup(g);
        }

        // ---------------------------------------------------------- Einstellungen
        void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string raw = line.Substring(i + 1).Trim();
                    int v;
                    int.TryParse(raw, out v);
                    switch (k)
                    {
                        case "Interval": intervalMs = Math.Min(Math.Max(v, 1), 600000); break;
                        case "Right": clicker.Right = v != 0; break;
                        case "Double": clicker.Double = v != 0; break;
                        case "HotMouse": hotkey.IsMouse = v != 0; break;
                        case "HotCode": hotkey.Code = v; break;
                        case "FilterOn": filterOn = v != 0; break;
                        case "MultiOn": multiOn = v != 0; break;
                        case "Multi": multiplier = Math.Min(Math.Max(v, 1), 10); break;
                        case "Programs":
                            programs.Clear();
                            foreach (string s in raw.Split(';'))
                            {
                                string n = s.Trim().ToLowerInvariant();
                                if (n.Length > 0 && !programs.Contains(n) && programs.Count < MAX_PROGRAMS) programs.Add(n);
                            }
                            break;
                    }
                }
                if (hotkey.IsMouse && (hotkey.Code < 1 || hotkey.Code > 5)) hotkey = new Hotkey(false, (int)Keys.F6);
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                File.WriteAllLines(settingsPath, new string[] {
                    "Interval=" + intervalMs,
                    "Right=" + (clicker.Right ? 1 : 0),
                    "Double=" + (clicker.Double ? 1 : 0),
                    "HotMouse=" + (hotkey.IsMouse ? 1 : 0),
                    "HotCode=" + hotkey.Code,
                    "FilterOn=" + (filterOn ? 1 : 0),
                    "MultiOn=" + (multiOn ? 1 : 0),
                    "Multi=" + multiplier,
                    "Programs=" + string.Join(";", programs.ToArray())
                });
            }
            catch { }
        }

        // Nur zum Testen des Designs: rendert das Fenster in eine PNG-Datei
        public void Snapshot(string path, bool demo)
        {
            IntPtr h = Handle;
            if (demo)
            {
                filterOn = true;
                programs.Clear();
                programs.Add("game.exe");
                programs.Add("notepad.exe");
                programs.Add("a-very-long-program-name.exe");
                autostart = true;
                multiOn = true;
                multiplier = 10;
                popupOpen = true;
                popItems = new List<ProcItem>();
                popItems.Add(new ProcItem("chrome.exe", "Neuer Tab - Google Chrome"));
                popItems.Add(new ProcItem("explorer.exe", "Dieser PC"));
                popItems.Add(new ProcItem("minecraft.exe", "Minecraft 1.21"));
            }
            using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                if (demo)
                {
                    // Chips werden beim ersten Zeichnen berechnet; fuer die Ansicht ohne Popup nochmal rendern
                    popupOpen = false;
                    using (Bitmap bmp2 = new Bitmap(ClientSize.Width, ClientSize.Height))
                    {
                        DrawToBitmap(bmp2, new Rectangle(0, 0, bmp2.Width, bmp2.Height));
                        bmp2.Save(Path.ChangeExtension(path, null) + "_nopopup.png", ImageFormat.Png);
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool tray = false, snapshot = false, demo = false;
            string snapPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--overlay-snapshot" && i + 1 < args.Length)
                {
                    Native.SetProcessDPIAware();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    float sc;
                    using (Graphics g0 = Graphics.FromHwnd(IntPtr.Zero)) sc = g0.DpiX / 96f;
                    Bitmap lg = null;
                    try { lg = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { }
                    new OverlayForm(lg, sc).Snapshot(args[i + 1]);
                    return;
                }
                if (args[i] == "--tray") tray = true;
                else if (args[i] == "--snapshot" && i + 1 < args.Length) { snapshot = true; snapPath = args[++i]; }
                else if (args[i] == "--demo") demo = true;
            }

            bool created;
            using (Mutex m = new Mutex(true, "PurpleClicker_SingleInstance", out created))
            {
                if (!created && !snapshot)
                {
                    // Laufende Instanz in den Vordergrund holen (ausser beim Autostart)
                    if (!tray)
                    {
                        try { EventWaitHandle.OpenExisting("PurpleClicker_Show").Set(); } catch { }
                    }
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm(tray && !snapshot, snapshot);
                if (snapshot)
                {
                    f.Snapshot(snapPath, demo);
                    return;
                }
                Application.Run(f);
            }
        }
    }
}
