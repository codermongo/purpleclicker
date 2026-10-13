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
        public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
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

    // ------------------------------------------------------------------ Maus-Umbelegung
    // Eingaben (physisch): 0 Links, 1 Rechts, 2 Mitte, 3 Maus 4, 4 Maus 5, 5 Rad hoch, 6 Rad runter, 7 Rad links kippen, 8 Rad rechts kippen
    // Aktionen: 0 Standard, 1 Links, 2 Rechts, 3 Mitte, 4 Maus 4, 5 Maus 5, 6 Rad hoch, 7 Rad runter, 8 Doppelklick, 9 Deaktiviert
    class Remapper
    {
        public const int Inputs = 9;
        public static readonly string[] InputNames = {
            "Linke Taste", "Rechte Taste", "Mausrad-Klick", "Seitentaste hinten (Maus 4)", "Seitentaste vorne (Maus 5)",
            "Rad nach oben", "Rad nach unten", "Rad nach links kippen", "Rad nach rechts kippen" };
        public static readonly string[] ActionNames = {
            "Standard", "Linksklick", "Rechtsklick", "Mittelklick", "Maus 4 (Zurück)", "Maus 5 (Vor)",
            "Rad hoch", "Rad runter", "Doppelklick", "Deaktiviert" };

        public volatile bool On;
        public readonly int[] Map = new int[Inputs];

        // Sendet ein Mausereignis (flags, mouseData); austauschbar fuer Tests
        public Action<uint, uint> Send;
        // Wird gemeldet, wenn die Umbelegung einen Klick der Taste 1..5 beendet hat (fuer den Multiplikator)
        public Action<int> OnClick;

        readonly int[] held = new int[6];     // Aktion, die zur gerade gedrueckten Taste 1..5 gehoert (0 = keine)
        readonly int[] wheelAcc = new int[Inputs];

        // SendInput darf nicht im Hook-Thread laufen: die eingespeisten Ereignisse laufen wieder durch unseren eigenen
        // Hook, der Thread wuerde sich selbst blockieren (Fenster reagiert nicht mehr). Darum eigener Sende-Thread.
        readonly Queue<ulong> outQ = new Queue<ulong>();
        readonly AutoResetEvent outEv = new AutoResetEvent(false);
        Thread outThread;

        public Remapper() { Send = Post; }

        void Post(uint flags, uint data)
        {
            lock (outQ)
            {
                outQ.Enqueue(((ulong)flags << 32) | data);
                if (outThread == null)
                {
                    outThread = new Thread(OutLoop);
                    outThread.IsBackground = true;
                    outThread.Priority = ThreadPriority.AboveNormal;
                    outThread.Start();
                }
            }
            outEv.Set();
        }

        void OutLoop()
        {
            while (true)
            {
                outEv.WaitOne();
                while (true)
                {
                    ulong v;
                    lock (outQ)
                    {
                        if (outQ.Count == 0) break;
                        v = outQ.Dequeue();
                    }
                    SendMouse((uint)(v >> 32), (uint)(v & 0xFFFFFFFF));
                }
            }
        }

        // Beim Beenden (Hooks sind schon entfernt): noch wartende Ereignisse direkt senden
        public void Flush()
        {
            while (true)
            {
                ulong v;
                lock (outQ)
                {
                    if (outQ.Count == 0) return;
                    v = outQ.Dequeue();
                }
                SendMouse((uint)(v >> 32), (uint)(v & 0xFFFFFFFF));
            }
        }

        // Aktion, die der Eingabe selbst entspricht (= "Standard"), wird beim Durchschalten uebersprungen
        public static bool IsSelf(int input, int act)
        {
            if (input <= 4) return act == input + 1;
            if (input == 5) return act == 6;
            if (input == 6) return act == 7;
            return false;
        }

        public void Cycle(int input, int dir)
        {
            int a = Map[input];
            for (int i = 0; i < ActionNames.Length; i++)
            {
                a = (a + dir + ActionNames.Length) % ActionNames.Length;
                if (!IsSelf(input, a)) break;
            }
            Map[input] = a;
        }

        public bool IsHeld(int btn) { return btn >= 1 && btn <= 5 && held[btn] != 0; }

        public bool Any()
        {
            for (int i = 0; i < Inputs; i++) if (Map[i] != 0) return true;
            return false;
        }

        // Taste 1..5. Gibt true zurueck, wenn das Original-Ereignis verschluckt werden soll.
        public bool Button(int btn, bool down, bool allowed)
        {
            if (btn < 1 || btn > 5) return false;
            if (!down)
            {
                int h = held[btn];
                if (h == 0) return false;
                held[btn] = 0;
                Release(h);
                return true;
            }
            if (!On || !allowed) return false;
            int t = Map[btn - 1];
            if (t == 0 || IsSelf(btn - 1, t)) return false;
            held[btn] = t;
            Press(t);
            return true;
        }

        // Rad: horizontal = Kippen. Gibt true zurueck, wenn das Original verschluckt werden soll.
        public bool Wheel(bool horizontal, int delta, bool allowed)
        {
            if (delta == 0) return false;
            int input = horizontal ? (delta > 0 ? 8 : 7) : (delta > 0 ? 5 : 6);
            int other = horizontal ? (delta > 0 ? 7 : 8) : (delta > 0 ? 6 : 5);
            wheelAcc[other] = 0;
            if (!On || !allowed) return false;
            int t = Map[input];
            if (t == 0 || IsSelf(input, t)) { wheelAcc[input] = 0; return false; }
            wheelAcc[input] += Math.Abs(delta);
            while (wheelAcc[input] >= 120)
            {
                wheelAcc[input] -= 120;
                Pulse(t);
            }
            return true;
        }

        // Gedrueckte umbelegte Tasten sauber loslassen (Umbelegung aus, Programmende)
        public void ReleaseAll()
        {
            for (int b = 1; b <= 5; b++)
                if (held[b] != 0) { int h = held[b]; held[b] = 0; Release(h); }
            Array.Clear(wheelAcc, 0, wheelAcc.Length);
        }

        void Press(int t)
        {
            if (t >= 1 && t <= 5) Btn(t, true);
            else if (t >= 6) Pulse(t);
        }

        void Release(int t)
        {
            if (t >= 1 && t <= 5) { Btn(t, false); if (OnClick != null) OnClick(t); }
        }

        // Einmalige Aktion (fuer Rad-Schritte und Tasten, die keine Haltedauer haben)
        void Pulse(int t)
        {
            if (t >= 1 && t <= 5) { Btn(t, true); Btn(t, false); if (OnClick != null) OnClick(t); }
            else if (t == 6) Send(0x0800, 120);
            else if (t == 7) Send(0x0800, unchecked((uint)(-120)));
            else if (t == 8)
            {
                Btn(1, true); Btn(1, false);
                Btn(1, true); Btn(1, false);
                if (OnClick != null) OnClick(1);
            }
        }

        void Btn(int b, bool down)
        {
            switch (b)
            {
                case 1: Send(down ? 0x0002u : 0x0004u, 0); break;
                case 2: Send(down ? 0x0008u : 0x0010u, 0); break;
                case 3: Send(down ? 0x0020u : 0x0040u, 0); break;
                case 4: Send(down ? 0x0080u : 0x0100u, 1); break;
                case 5: Send(down ? 0x0080u : 0x0100u, 2); break;
            }
        }

        static void SendMouse(uint flags, uint data)
        {
            Native.INPUT[] a = new Native.INPUT[1];
            a[0].type = 0; // INPUT_MOUSE
            a[0].mi.dwFlags = flags;
            a[0].mi.mouseData = data;
            Native.SendInput(1, a, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public string Serialize()
        {
            string[] p = new string[Inputs];
            for (int i = 0; i < Inputs; i++) p[i] = Map[i].ToString();
            return string.Join(",", p);
        }

        public void Load(string raw)
        {
            string[] p = raw.Split(',');
            for (int i = 0; i < Inputs && i < p.Length; i++)
            {
                int v;
                int.TryParse(p[i].Trim(), out v);
                Map[i] = v < 0 || v >= ActionNames.Length || IsSelf(i, v) ? 0 : v;
            }
        }
    }

    // Erkennt die angeschlossenen Mäuse (Raw Input) und liefert Tastenzahl + ggf. ein bekanntes Foto
    static class MouseDetect
    {
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
        [StructLayout(LayoutKind.Sequential)] struct RID_DEVICE_INFO_MOUSE { public uint dwId; public uint dwNumberOfButtons; public uint dwSampleRate; public bool fHasHorizontalWheel; }
        [StructLayout(LayoutKind.Explicit, Size = 32)] struct RID_DEVICE_INFO { [FieldOffset(0)] public uint cbSize; [FieldOffset(4)] public uint dwType; [FieldOffset(8)] public RID_DEVICE_INFO_MOUSE mouse; }

        [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([In, Out] RAWINPUTDEVICELIST[] list, ref uint n, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, StringBuilder data, ref uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, ref RID_DEVICE_INFO data, ref uint size);

        // bekannte Mäuse mit eigenem Foto: Geräte-Kennung -> Name der eingebetteten Bilddatei
        static readonly string[] KnownIds = { "VID_0B05&PID_1ACE" };
        static readonly string[] KnownPhotos = { "mouse-photo.png" };

        public static bool Found;       // mindestens eine Maus gefunden
        public static int Buttons;      // Tastenzahl laut Windows (groesste unter den Maeusen)
        public static string Photo;     // Bildname, wenn eine bekannte Maus gefunden wurde, sonst null
        public static string Id = "";   // "0B05:1ACE" der bekannten bzw. ersten echten Maus

        public static void Refresh()
        {
            Found = false; Buttons = 0; Photo = null; Id = "";
            try
            {
                uint n = 0;
                uint sz = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
                GetRawInputDeviceList(null, ref n, sz);
                if (n == 0) return;
                RAWINPUTDEVICELIST[] list = new RAWINPUTDEVICELIST[n];
                if (GetRawInputDeviceList(list, ref n, sz) == uint.MaxValue) return;
                for (int i = 0; i < n; i++)
                {
                    if (list[i].dwType != 0) continue; // RIM_TYPEMOUSE
                    uint len = 0;
                    GetRawInputDeviceInfo(list[i].hDevice, 0x20000007, null, ref len); // RIDI_DEVICENAME
                    StringBuilder name = new StringBuilder((int)len + 1);
                    GetRawInputDeviceInfo(list[i].hDevice, 0x20000007, name, ref len);
                    string path = name.ToString().ToUpperInvariant();
                    if (path.IndexOf("VID_") < 0) continue; // virtuelle Geraete (Remote-Desktop usw.) ueberspringen

                    RID_DEVICE_INFO info = new RID_DEVICE_INFO();
                    info.cbSize = (uint)Marshal.SizeOf(typeof(RID_DEVICE_INFO));
                    uint isz = info.cbSize;
                    int btn = 0;
                    if (GetRawInputDeviceInfo(list[i].hDevice, 0x2000000b, ref info, ref isz) != uint.MaxValue)
                        btn = (int)info.mouse.dwNumberOfButtons;
                    Found = true;
                    if (btn > Buttons) Buttons = btn;
                    int v = path.IndexOf("VID_"), p = path.IndexOf("PID_");
                    string id = v >= 0 && p >= 0 && path.Length >= p + 8 ? path.Substring(v + 4, 4) + ":" + path.Substring(p + 4, 4) : "";
                    if (Id.Length == 0) Id = id;
                    for (int k = 0; k < KnownIds.Length; k++)
                        if (path.IndexOf(KnownIds[k]) >= 0) { Photo = KnownPhotos[k]; Id = id; }
                }
            }
            catch { }
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
        int holdMs = 5000;

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

        public void ShowMessage(string t, string s, Screen screen, int seconds)
        {
            title = t;
            holdMs = Math.Max(1, seconds) * 1000;
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
                if (Opacity >= 0.96) { phase = 1; holdUntil = Environment.TickCount + holdMs; }
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
        AutoSw, FilterSw, MulSlider, MulSw, MulKey, DurSlider, ChipX, ChipAdd, PopItem, PopPanel, PopOutside,
        Gear, ThemeItem, ThemePanel, ThemeOutside,
        MouseBtn, RmSw, RmPrev, RmNext, RmMid, RmReset, RmBadge, RmRow, RmModel, RmRec
    }

    // ------------------------------------------------------------------ Fenster
    class MainForm : Form
    {
        // Layout in logischen Pixeln (wird per ScaleTransform auf den Bildschirm-DPI skaliert)
        const int W = 700, H = 506;
        static readonly Rectangle R_Title = new Rectangle(0, 0, W, 56);
        static readonly Rectangle R_Gear = new Rectangle(574, 13, 30, 30);
        static readonly Rectangle R_ThemePanel = new Rectangle(500, 50, 180, 268);
        const int THEME_ROW = 40, THEME_Y0 = 62;
        static readonly Rectangle R_Min = new Rectangle(612, 13, 30, 30);
        static readonly Rectangle R_Close = new Rectangle(650, 13, 30, 30);

        // Eine Box fuer alles rund um den Autoclicker: links Intervall, rechts Maustaste / Klick-Art / Hotkey
        static readonly Rectangle R_CardAuto = new Rectangle(20, 64, 660, 186);
        static readonly Rectangle R_CardInt = new Rectangle(20, 64, 338, 186);   // linke Haelfte (Mausrad aendert das Intervall)
        const int AUTO_DIV_X = 358, AUTO_LBL_X = 382, AUTO_LBL_W = 96;
        static readonly Rectangle R_Minus = new Rectangle(34, 122, 36, 36);
        static readonly Rectangle R_Plus = new Rectangle(300, 122, 36, 36);
        static readonly Rectangle R_Num = new Rectangle(76, 118, 218, 44);
        static readonly Rectangle R_Slider = new Rectangle(26, 174, 318, 20);
        const int SL_X = 34, SL_W = 302, SL_Y = 184;

        static readonly Rectangle R_BtnL = new Rectangle(482, 83, 91, 34);
        static readonly Rectangle R_BtnR = new Rectangle(573, 83, 91, 34);

        static readonly Rectangle R_ClkS = new Rectangle(482, 140, 91, 34);
        static readonly Rectangle R_ClkD = new Rectangle(573, 140, 91, 34);

        static readonly Rectangle R_HotPill = new Rectangle(446, 197, 218, 34);
        static readonly Rectangle R_HotBtn = new Rectangle(582, 200, 76, 28);

        // Autostart sitzt in der Titelleiste
        static readonly Rectangle R_AutoHit = new Rectangle(360, 13, 158, 30);
        static readonly Rectangle R_AutoSw = new Rectangle(474, 16, 44, 24);
        // Maus-Seite (Umbelegung)
        static readonly Rectangle R_MouseBtn = new Rectangle(536, 13, 30, 30);
        static readonly Rectangle R_RmCard = new Rectangle(20, 64, 660, 374);
        static readonly Rectangle R_RmSw = new Rectangle(620, 72, 44, 24);
        const int RM_Y0 = 108, RM_ROW = 35, RM_H = 30, RM_PILL_X = 424, RM_PILL_W = 236, RM_ARROW = 34;
        const int RM_LIST_X = 246;   // Zeilen beginnen hier (links davon: Mausbild)
        // Mausbild: Zeichenflaeche 236 x 437 (Koordinaten des Fotos), skaliert in die linke Haelfte der Karte
        const float MS_X = 36f, MS_Y = 98f, MS_S = 0.70f, MS_W = 236f, MS_H = 437f;
        static readonly Rectangle R_RmRec = new Rectangle(40, 384, 176, 26);     // "Tasten aufnehmen" / "Fertig"
        static readonly Rectangle R_RmModel = new Rectangle(28, 414, 212, 20);
        // Markerpositionen je Eingabe im Bild (-1 = kein Marker auf der Maus) und Radius (logische px)
        static readonly float[] BadgeX = { 70, 166, 118, 20, 20, 118, 118, -1, -1 };
        static readonly float[] BadgeY = { 88, 88, 88, 160, 219, 36, 136, -1, -1 };
        static readonly float[] BadgeR = { 11, 11, 11, 11, 11, 8, 8, 0, 0 };
        static readonly string[] ShortNames = { "Linke Taste", "Rechte Taste", "Mausrad-Klick", "Maus 4 (hinten)", "Maus 5 (vorne)", "Rad hoch", "Rad runter", "Rad kippen links", "Rad kippen rechts" };

        static readonly Rectangle R_CardMul = new Rectangle(20, 262, 420, 56);
        static readonly Rectangle R_MulSlider = new Rectangle(140, 268, 116, 44);
        const int MUL_X = 150, MUL_W = 96, MUL_Y = 290;
        static readonly Rectangle R_MulKey = new Rectangle(298, 273, 66, 30);
        static readonly Rectangle R_CardDur = new Rectangle(456, 262, 224, 56);
        static readonly Rectangle R_DurSlider = new Rectangle(538, 268, 104, 44);
        const int DUR_X = 548, DUR_W = 84;
        static readonly Rectangle R_MulSw = new Rectangle(376, 278, 44, 24);

        static readonly Rectangle R_CardProg = new Rectangle(20, 330, 660, 108);
        static readonly Rectangle R_FilterSw = new Rectangle(620, 338, 44, 24);
        const int CHIP_X0 = 36, CHIP_X1 = 664, CHIP_Y0 = 372, CHIP_H = 26, CHIP_GAP = 8;
        const int MAX_PROGRAMS = 7;

        static readonly Rectangle R_Start = new Rectangle(20, 450, 660, 42);

        static readonly Rectangle R_Pop = new Rectangle(110, 60, 480, 306);
        static readonly Rectangle R_PopList = new Rectangle(118, 104, 464, 240);
        const int POP_ROW = 40, POP_ROWS = 6;

        // Farben (AMOLED)
        static Color C_Bg = Color.FromArgb(0, 0, 0);
        static Color C_Card = Color.FromArgb(16, 16, 16);
        static Color C_CardLine = Color.FromArgb(32, 32, 32);
        static Color C_Field = Color.FromArgb(8, 8, 8);
        static Color C_Btn = Color.FromArgb(28, 28, 28);
        static Color C_BtnHover = Color.FromArgb(44, 44, 44);
        static Color C_Text = Color.FromArgb(237, 237, 237);
        static Color C_Dim = Color.FromArgb(128, 128, 128);
        static Color C_Accent = Color.FromArgb(124, 108, 255);
        static Color C_AccentHover = Color.FromArgb(146, 132, 255);

        // Themes: 0 Original, 1 Terminal, 2 Neon, 3 Mono, 4 Sunset
        // Jedes Theme aendert Farben, Formen, Schrift, Kartenstil, Schalter, Slider-Griffe, Start-Button und Hintergrund.
        static bool fancy;
        static Color C_Accent2 = Color.FromArgb(124, 108, 255);
        static Color C_OnAccent = Color.White;          // Text auf Akzentflaechen
        static Color C_Thumb = Color.White;             // Schalter-Knopf (an)
        static float radCard = 14;                      // Eckenradius der Karten
        static float radCtl = -1;                       // Eckenradius der Bedienelemente (-1 = Pille)
        static float radStart = 21;                     // Eckenradius Start-Button
        static bool cardGlow;                           // Kartenstil: Neon-Leuchten
        static bool squareThumb;                        // eckige Slider-/Schalter-Griffe
        static bool segOutline;                         // Auswahl nur als Umriss statt gefuellt
        static bool startOutline;                       // Start-Button als Umriss (Stop gefuellt)
        static bool startGlow = true;                   // Leuchten um den Start-Button
        static int backdrop;                            // 0 schwarz, 2 Scanlines, 3 Gitter, 5 Sonnenglut
        static string famRegular = "Segoe UI", famSemi = "Segoe UI Semibold", famBig = "Segoe UI Light", famBtn = "Segoe UI Semibold";
        static FontStyle styleSemi = FontStyle.Regular;
        static float fontScale = 1f;
        static string titleText = "Purple Clicker";
        static string startFmt = "START  ·  {0}";
        static string stopFmt = "STOP  ·  {0}";

        public static bool snapThemePop;
        public static int snapPage;
        public static int snapView = -1;
        public static int snapRec;
        public static int themeArg = -1;   // --theme N ueberschreibt die gespeicherte Auswahl
        int theme;
        bool themeOpen;

        static readonly string[] ThemeNames = { "Original", "Terminal", "Neon", "Mono", "Sunset", "Rainbow" };
        static readonly Color[] ThemeA = {
            Color.FromArgb(124, 108, 255), Color.FromArgb(57, 255, 120), Color.FromArgb(0, 229, 255),
            Color.FromArgb(240, 240, 240), Color.FromArgb(255, 94, 160), Color.FromArgb(255, 77, 77) };
        static readonly Color[] ThemeB = {
            Color.FromArgb(124, 108, 255), Color.FromArgb(57, 255, 120), Color.FromArgb(255, 43, 214),
            Color.FromArgb(240, 240, 240), Color.FromArgb(255, 166, 77), Color.FromArgb(77, 160, 255) };
        const int THEME_RAINBOW = 5;
        static float rbPhase;   // Farbwinkel 0..360 des Rainbow-Themes, wandert per Timer

        static Color A(int a, Color c) { return Color.FromArgb(a, c.R, c.G, c.B); }

        static Color Hsv(float h, float s, float v)
        {
            h = ((h % 360f) + 360f) % 360f;
            float c = v * s, x = c * (1f - Math.Abs((h / 60f) % 2f - 1f)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255f), (int)((g + m) * 255f), (int)((b + m) * 255f));
        }

        // Rainbow: Akzentfarben aus dem aktuellen Farbwinkel setzen
        static void RainbowColors()
        {
            C_Accent = Hsv(rbPhase, 0.68f, 1f);
            C_Accent2 = Hsv(rbPhase + 100f, 0.68f, 1f);
            C_AccentHover = Lighten(C_Accent, 0.2f);
            C_CardLine = Color.FromArgb(30 + (int)((C_Accent.R - 30) * 0.22f), 30 + (int)((C_Accent.G - 30) * 0.22f), 30 + (int)((C_Accent.B - 30) * 0.22f));
        }

        public static void ApplyTheme(int n)
        {
            // Grundzustand = Original
            fancy = n != 0;
            C_Bg = Color.FromArgb(0, 0, 0);
            C_Card = Color.FromArgb(16, 16, 16);
            C_CardLine = Color.FromArgb(32, 32, 32);
            C_Field = Color.FromArgb(8, 8, 8);
            C_Btn = Color.FromArgb(28, 28, 28);
            C_BtnHover = Color.FromArgb(44, 44, 44);
            C_Text = Color.FromArgb(237, 237, 237);
            C_Dim = Color.FromArgb(128, 128, 128);
            C_Accent = Color.FromArgb(124, 108, 255);
            C_Accent2 = C_Accent;
            C_AccentHover = Color.FromArgb(146, 132, 255);
            C_OnAccent = Color.White;
            C_Thumb = Color.White;
            radCard = 8; radCtl = 8; radStart = 8;   // Ecken und Kanten wie Mono
            cardGlow = false; squareThumb = false; segOutline = false;
            startOutline = false; startGlow = true; backdrop = 0;
            famRegular = "Segoe UI"; famSemi = "Segoe UI Semibold"; famBig = "Segoe UI Light"; famBtn = "Segoe UI Semibold";
            styleSemi = FontStyle.Regular; fontScale = 1f;
            titleText = "Purple Clicker"; startFmt = "START  ·  {0}"; stopFmt = "STOP  ·  {0}";

            switch (n)
            {
                case 1: // Terminal: Monospace, eckig, Gruen auf Schwarz, Scanlines
                    C_Accent = ThemeA[1]; C_Accent2 = ThemeB[1];
                    C_AccentHover = Color.FromArgb(130, 255, 170);
                    C_OnAccent = Color.FromArgb(0, 18, 6);
                    C_Thumb = C_OnAccent;
                    C_Card = Color.FromArgb(215, 3, 12, 6);
                    C_CardLine = Color.FromArgb(24, 86, 48);
                    C_Field = Color.FromArgb(0, 6, 2);
                    C_Btn = Color.FromArgb(10, 28, 16);
                    C_BtnHover = Color.FromArgb(18, 52, 30);
                    C_Text = Color.FromArgb(178, 240, 196);
                    C_Dim = Color.FromArgb(72, 140, 96);
                    radCard = 3; radCtl = 2; radStart = 3;
                    squareThumb = true; startGlow = false; backdrop = 2;
                    famRegular = famSemi = famBig = famBtn = "Consolas";
                    styleSemi = FontStyle.Bold; fontScale = 0.95f;
                    titleText = "> purple_clicker"; startFmt = "[ START  {0} ]"; stopFmt = "[ STOP  {0} ]";
                    break;
                case 2: // Neon: Umrisse mit Leuchten, Cyan/Magenta, Raster
                    C_Accent = ThemeA[2]; C_Accent2 = ThemeB[2];
                    C_AccentHover = Color.FromArgb(110, 240, 255);
                    C_OnAccent = Color.FromArgb(6, 6, 14);
                    C_Card = Color.FromArgb(215, 7, 7, 15);
                    C_CardLine = Color.FromArgb(0, 128, 150);
                    C_Field = Color.FromArgb(3, 3, 9);
                    C_Btn = Color.FromArgb(20, 20, 34);
                    C_BtnHover = Color.FromArgb(34, 34, 56);
                    C_Text = Color.FromArgb(232, 246, 255);
                    C_Dim = Color.FromArgb(110, 130, 160);
                    radCard = 10; radCtl = -1; radStart = 21;
                    cardGlow = true; segOutline = true; startOutline = true; backdrop = 3;
                    famRegular = "Bahnschrift"; famSemi = "Bahnschrift SemiBold"; famBig = "Bahnschrift Light"; famBtn = "Bahnschrift SemiBold";
                    break;
                case 3: // Mono: schlicht, Weiss, kleine Radien, keine Effekte
                    C_Accent = ThemeA[3]; C_Accent2 = ThemeB[3];
                    C_AccentHover = Color.White;
                    C_OnAccent = Color.FromArgb(8, 8, 8);
                    C_Thumb = C_OnAccent;
                    C_Card = Color.FromArgb(14, 14, 14);
                    C_CardLine = Color.FromArgb(44, 44, 44);
                    C_Field = Color.FromArgb(4, 4, 4);
                    C_Btn = Color.FromArgb(26, 26, 26);
                    C_BtnHover = Color.FromArgb(46, 46, 46);
                    C_Dim = Color.FromArgb(120, 120, 120);
                    radCard = 8; radCtl = 8; radStart = 8;
                    startGlow = false;
                    famBig = "Segoe UI Semilight";
                    break;
                case 4: // Sunset: warm, Mono-Ecken, Glut im Hintergrund
                    C_Accent = ThemeA[4]; C_Accent2 = ThemeB[4];
                    C_AccentHover = Color.FromArgb(255, 130, 185);
                    C_Card = Color.FromArgb(26, 15, 20);
                    C_CardLine = Color.FromArgb(70, 38, 48);
                    C_Field = Color.FromArgb(14, 8, 11);
                    C_Btn = Color.FromArgb(44, 26, 33);
                    C_BtnHover = Color.FromArgb(68, 40, 50);
                    C_Text = Color.FromArgb(255, 238, 232);
                    C_Dim = Color.FromArgb(160, 118, 126);
                    radCard = 8; radCtl = 8; radStart = 8;   // Ecken wie Mono
                    backdrop = 5;
                    famBtn = "Segoe UI Black"; famBig = "Segoe UI";
                    break;
                case THEME_RAINBOW: // Rainbow: Regenbogen-Verlauf, Farben wandern langsam durch
                    C_OnAccent = Color.FromArgb(12, 12, 18);
                    C_Thumb = C_OnAccent;
                    C_Card = Color.FromArgb(16, 16, 20);
                    C_Btn = Color.FromArgb(28, 28, 34);
                    C_BtnHover = Color.FromArgb(46, 46, 56);
                    radCard = 8; radCtl = 8; radStart = 8;   // Ecken wie Mono
                    famBtn = "Segoe UI Black";
                    RainbowColors();
                    break;
            }
        }

        // Gespeichert wird der Name; alte Zahlen (0 Original, 1 Aurora, 2 Mint, 3 Sunset) werden umgesetzt
        static int ThemeFromSetting(string raw)
        {
            for (int i = 0; i < ThemeNames.Length; i++)
                if (string.Equals(raw, ThemeNames[i], StringComparison.OrdinalIgnoreCase)) return i;
            int v;
            if (int.TryParse(raw, out v))
            {
                if (v == 2) return 1;
                if (v == 3) return 4;
            }
            return 0;
        }

        static float RadCtl(float h) { return radCtl < 0 ? h / 2f : Math.Min(radCtl, h / 2f); }

        Font fTitle, fLabel, fBig, fUnit, fBtn, fChip, fGlyph;

        void BuildFonts()
        {
            Font[] old = { fTitle, fLabel, fBig, fUnit, fBtn, fChip, fGlyph };
            fTitle = new Font(famSemi, 11f * fontScale, styleSemi);
            fLabel = new Font(famSemi, 8.25f * fontScale, styleSemi);
            fBig = new Font(famBig, 24f * (fontScale < 1f ? 0.92f : 1f));
            fUnit = new Font(famRegular, 10f * fontScale);
            fBtn = new Font(famBtn, 10f * fontScale, styleSemi);
            fChip = new Font(famRegular, 9f * fontScale);
            fGlyph = new Font(famRegular, 14f);
            foreach (Font f in old) if (f != null) f.Dispose();
        }

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
        readonly Remapper remap = new Remapper();   // Maus-Umbelegung
        int page;                                   // 0 = Hauptseite, 1 = Maus-Umbelegung
        int rmSel = -1;                             // im Mausbild gewaehlte Eingabe
        int mouseView;                              // 0 Auto, 1 Foto, 2 gezeichnet (5 Tasten), 3 gezeichnet (3 Tasten)
        Bitmap mousePhoto;                          // aus der exe, null wenn nicht eingebettet
        // Aufnahme-Modus: sammelt, welche Eingaben Windows von der Maus sieht
        bool recording;
        readonly bool[] recSeen = new bool[Remapper.Inputs];
        int recStart;
        readonly bool[] rmSeen = new bool[Remapper.Inputs];   // Ergebnis der letzten Aufnahme
        bool rmSeenKnown;
        string rmNote = "";
        Hotkey mulKey = new Hotkey(false, 0);   // Hotkey fuer den Multiplikator (Code 0 = keiner)
        bool capturing;
        int capTarget;                          // 0 = Start/Stop-Hotkey, 1 = Multiplikator-Hotkey
        readonly bool[] swallowUp = new bool[6];
        readonly HashSet<int> swallowedKeys = new HashSet<int>();

        int intervalMs = 100;
        bool editing;
        string editBuf = "";
        int dragKind;              // 0 = nichts, 1 = Intervall-Slider, 2 = Multiplikator-Slider, 3 = Meldungsdauer
        int overlaySec = 5;        // Anzeigedauer des Overlays (1-10 s)
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
        readonly System.Windows.Forms.Timer rainbowTimer = new System.Windows.Forms.Timer();
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
            rainbowTimer.Interval = 70;
            rainbowTimer.Tick += delegate
            {
                // nur animieren, solange das Fenster sichtbar ist (im Tray kostet es nichts)
                if (!Visible || WindowState == FormWindowState.Minimized) return;
                rbPhase = (rbPhase + 2.2f) % 360f;
                RainbowColors();
                Invalidate();
            };

            remap.OnClick = delegate(int b)
            {
                // Von der Umbelegung erzeugter Klick zaehlt fuer den Multiplikator wie ein echter
                if (multiOn && multiplier > 1 && active && b == (clicker.Right ? 2 : 1))
                    clicker.Burst(multiplier - 1);
            };
            LoadSettings();
            if (snapView >= 0) mouseView = snapView;
            mousePhoto = LoadPhoto("mouse-photo.png");
            MouseDetect.Refresh();
            if (themeArg >= 0) theme = themeArg;
            theme = Math.Min(Math.Max(theme, 0), ThemeNames.Length - 1);
            ApplyTheme(theme);
            BuildFonts();
            rainbowTimer.Enabled = theme == THEME_RAINBOW && !noServices;
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
            // Notausgang: Umbelegung jederzeit ueber das Tray-Menue an-/ausschalten
            ToolStripMenuItem rmItem = new ToolStripMenuItem("Maus-Umbelegung aktiv");
            rmItem.CheckOnClick = true;
            rmItem.Click += delegate { SetRemapOn(rmItem.Checked); };
            menu.Opening += delegate { rmItem.Checked = remap.On; };
            menu.Items.Add(rmItem);
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

        void SetRemapOn(bool on)
        {
            remap.On = on;
            if (!on) remap.ReleaseAll();
            Invalidate();
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
            rainbowTimer.Stop();
            clicker.SetRunning(false);
            clicker.Dispose();
            remap.On = false;
            remap.ReleaseAll();
            if (showEvent != null) { try { showEvent.Set(); } catch { } }
            if (notify != null) { notify.Visible = false; notify.Dispose(); notify = null; }
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            mouseHook = keyHook = IntPtr.Zero;
            remap.Flush();   // wartende Loslass-Ereignisse der Umbelegung noch senden
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
                    int wmsg = (int)wParam;
                    if (wmsg == 0x20A || wmsg == 0x20E)   // Rad / Kippen
                    {
                        int delta = (short)((s.mouseData >> 16) & 0xFFFF);
                        if (HandleWheel(wmsg == 0x20E, delta, new Point(s.pt.X, s.pt.Y)))
                            return (IntPtr)1;
                    }
                    switch (wmsg)
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

        // Liegt unter dem Klickpunkt wirklich unser Fenster? (nicht nur dessen Koordinaten:
        // ein Spiel mit festgehaltener Maus klickt immer in der Fenstermitte, die unter unserem Fenster liegen kann)
        bool IsOwnWindowAt(Point pt)
        {
            if (!IsHandleCreated || !Visible) return false;
            Native.POINT p = new Native.POINT();
            p.X = pt.X;
            p.Y = pt.Y;
            IntPtr h = Native.WindowFromPoint(p);
            return h != IntPtr.Zero && Native.GetAncestor(h, 2) == Handle; // GA_ROOT
        }

        // Umbelegung nur, wenn eingeschaltet, der Programm-Filter passt und der Zeiger nicht ueber unserem Fenster liegt
        bool RemapAllowed(Point pt)
        {
            return remap.On && active && !capturing && !IsOwnWindowAt(pt);
        }

        // ---------------------------------------------------------- Aufnahme-Modus
        void StartRecording()
        {
            capturing = false;
            editing = false;
            remap.ReleaseAll();
            Array.Clear(recSeen, 0, recSeen.Length);
            recSeen[0] = true;          // der Klick auf "Aufnehmen" war die linke Taste
            recStart = Environment.TickCount;
            rmNote = "";
            recording = true;
            Invalidate();
        }

        void EndRecording(bool apply)
        {
            if (!recording) return;
            recording = false;
            int buttons = 0;
            for (int i = 0; i < 5; i++) if (recSeen[i]) buttons++;
            bool wheel = recSeen[5] || recSeen[6], tilt = recSeen[7] || recSeen[8];
            if (!apply) rmNote = "Aufnahme abgebrochen";
            else if (buttons <= 1 && !wheel && !tilt) rmNote = "Nichts erkannt – Aufnahme verworfen";
            else
            {
                Array.Copy(recSeen, rmSeen, rmSeen.Length);
                rmSeenKnown = true;
                // Ohne bekanntes Foto passend zur Tastenzahl das gezeichnete Modell waehlen
                if (!(MouseDetect.Photo != null && mousePhoto != null))
                    mouseView = recSeen[3] || recSeen[4] ? 2 : 3;
                rmNote = "Erkannt: " + buttons + " Tasten" + (wheel ? " + Rad" : "") + (tilt ? " + Kippen" : "");
                SaveSettings();
            }
            Invalidate();
        }

        bool HandleWheel(bool horizontal, int delta, Point pt)
        {
            if (recording)
            {
                int w = horizontal ? (delta < 0 ? 7 : 8) : (delta > 0 ? 5 : 6);
                recSeen[w] = true;
                Invalidate();
                return true;
            }
            if (!remap.On) return false;
            return remap.Wheel(horizontal, delta, RemapAllowed(pt));
        }

        bool HandleMouse(int btn, bool down, Point pt)
        {
            if (!down)
            {
                if (swallowUp[btn]) { swallowUp[btn] = false; return true; }
                if (recording) return false;
                if (remap.Button(btn, false, false)) return true;   // Loslassen einer umbelegten Taste
                // Multiplikator: echter Klick (Hook ignoriert eigene) -> N-1 zusaetzliche Klicks nach dem Loslassen
                if (multiOn && multiplier > 1 && active && !capturing &&
                    btn == (clicker.Right ? 2 : 1) && !IsOwnWindowAt(pt))
                    clicker.Burst(multiplier - 1);
                return false;
            }
            if (recording)
            {
                // Links/Rechts nur mitzaehlen und durchlassen (Bedienung des Fensters), alles andere schlucken
                recSeen[btn - 1] = true;
                Invalidate();
                if (btn <= 2) return false;
                swallowUp[btn] = true;
                return true;
            }
            // Links/Rechts im eigenen sichtbaren Fenster nie abfangen, sonst waere die Bedienung blockiert
            if (btn <= 2 && IsOwnWindowAt(pt)) return false;

            if (capturing)
            {
                AssignKey(true, btn);
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
            if (mulKey.Code != 0 && mulKey.IsMouse && mulKey.Code == btn)
            {
                swallowUp[btn] = true;
                ToggleMulti();
                return true;
            }
            return remap.On && remap.Button(btn, true, RemapAllowed(pt));
        }

        // Belegt den gerade gewaehlten Hotkey (Start/Stop oder Multiplikator); keine Doppelbelegung
        void AssignKey(bool isMouse, int code)
        {
            if (capTarget == 0)
            {
                hotkey = new Hotkey(isMouse, code);
                if (mulKey.IsMouse == isMouse && mulKey.Code == code) mulKey = new Hotkey(false, 0);
            }
            else if (!(hotkey.IsMouse == isMouse && hotkey.Code == code))
                mulKey = new Hotkey(isMouse, code);
        }

        void ToggleMulti()
        {
            multiOn = !multiOn;
            Invalidate();
            try
            {
                if (overlay == null || overlay.IsDisposed) overlay = new OverlayForm(logo, scale);
                IntPtr fg = Native.GetForegroundWindow();
                overlay.ShowMessage("Multiplikator " + (multiOn ? "an" : "aus"), multiplier + "× pro Klick",
                    fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen, 2);
            }
            catch { }
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

            if (recording)
            {
                if (vk == (int)Keys.Return || vk == (int)Keys.Escape)
                {
                    swallowedKeys.Add(vk);
                    EndRecording(vk == (int)Keys.Return);
                    return true;
                }
                return false;
            }
            if (capturing)
            {
                capturing = false;
                swallowedKeys.Add(vk);
                if (vk != (int)Keys.Escape) AssignKey(false, vk);
                else if (capTarget == 1) mulKey = new Hotkey(false, 0);   // Esc entfernt den Multiplikator-Hotkey
                Invalidate();
                return true;
            }
            if (!active) return false;
            if (!hotkey.IsMouse && hotkey.Code == vk)
            {
                if (swallowedKeys.Add(vk)) Toggle(); // Auto-Repeat nur schlucken
                return true;
            }
            if (mulKey.Code != 0 && !mulKey.IsMouse && mulKey.Code == vk)
            {
                if (swallowedKeys.Add(vk)) ToggleMulti();
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

            if (recording && Environment.TickCount - recStart > 90000) { EndRecording(false); rmNote = "Zeit abgelaufen"; }
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

        // Vorschau beim Loslassen des Dauer-Schiebers
        void ShowPreview()
        {
            try
            {
                if (overlay == null || overlay.IsDisposed) overlay = new OverlayForm(logo, scale);
                overlay.ShowMessage("Purple Clicker aktiv", "Vorschau · " + overlaySec + " s", Screen.FromHandle(Handle), overlaySec);
            }
            catch { }
        }

        // Kleines Overlay oben rechts (ohne Ton, klickdurchlaessig, nimmt keinen Fokus)
        void ShowOverlay(string exe, IntPtr fg)
        {
            try
            {
                if (overlay == null || overlay.IsDisposed) overlay = new OverlayForm(logo, scale);
                overlay.ShowMessage("Purple Clicker aktiv", exe + " erkannt", Screen.FromHandle(fg), overlaySec);
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
            if (R_Gear.Contains(p)) return Hit.Gear;
            if (themeOpen)
            {
                if (R_ThemePanel.Contains(p))
                {
                    int ti = (p.Y - THEME_Y0) / THEME_ROW;
                    if (p.Y >= THEME_Y0 && ti >= 0 && ti < ThemeNames.Length) { hitIndex = ti; return Hit.ThemeItem; }
                    return Hit.ThemePanel;
                }
                return Hit.ThemeOutside;
            }
            if (page == 0 && !popupOpen && R_AutoHit.Contains(p)) return Hit.AutoSw;
            if (R_MouseBtn.Contains(p)) return Hit.MouseBtn;
            if (R_Title.Contains(p)) return Hit.Drag;
            if (page == 1) return HitRemap(p);

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
            if (R_MulKey.Contains(p)) return Hit.MulKey;
            if (R_MulSlider.Contains(p)) return Hit.MulSlider;
            if (R_DurSlider.Contains(p)) return Hit.DurSlider;
            if (R_BtnL.Contains(p)) return Hit.BtnLeft;
            if (R_BtnR.Contains(p)) return Hit.BtnRight;
            if (R_ClkS.Contains(p)) return Hit.ClkSingle;
            if (R_ClkD.Contains(p)) return Hit.ClkDouble;
            if (R_HotBtn.Contains(p)) return Hit.HotChange;
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

        string SeenString()
        {
            if (!rmSeenKnown) return "";
            char[] c = new char[rmSeen.Length];
            for (int i = 0; i < c.Length; i++) c[i] = rmSeen[i] ? '1' : '0';
            return new string(c);
        }

        static Bitmap LoadPhoto(string name)
        {
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (Bitmap raw = new Bitmap(s)) return new Bitmap(raw); // Kopie, damit der Stream frei wird
                }
            }
            catch { return null; }
        }

        // Welches Mausbild gezeigt wird: 0 Foto, 1 gezeichnet mit 5 Tasten, 2 gezeichnet mit 3 Tasten
        int RmModelKind()
        {
            switch (mouseView)
            {
                case 1: return mousePhoto != null ? 0 : 1;
                case 2: return 1;
                case 3: return 2;
            }
            if (MouseDetect.Photo != null && mousePhoto != null) return 0;
            return MouseDetect.Found && MouseDetect.Buttons < 5 ? 2 : 1;
        }

        static bool BadgeShown(int i, int kind)
        {
            return BadgeX[i] >= 0 && !(kind == 2 && (i == 3 || i == 4));
        }

        static PointF BadgePt(int i)
        {
            return new PointF(MS_X + BadgeX[i] * MS_S, MS_Y + BadgeY[i] * MS_S);
        }

        string RmModelText()
        {
            string t;
            if (mouseView == 0)
            {
                if (!MouseDetect.Found) t = "Keine Maus erkannt";
                else t = "Erkannt: " + MouseDetect.Id + " · " + MouseDetect.Buttons + " Tasten";
            }
            else t = "Ansicht: " + (mouseView == 1 ? "Foto" : mouseView == 2 ? "gezeichnet, 5 Tasten" : "gezeichnet, 3 Tasten");
            return t;
        }

        Hit HitRemap(Point p)
        {
            if (R_RmSw.Contains(p)) return Hit.RmSw;
            if (R_Start.Contains(p)) return Hit.RmReset;
            if (R_RmRec.Contains(p)) return Hit.RmRec;
            if (R_RmModel.Contains(p)) return Hit.RmModel;
            int kind = RmModelKind();
            for (int i = 0; i < Remapper.Inputs; i++)
            {
                if (!BadgeShown(i, kind)) continue;
                PointF c = BadgePt(i);
                float dx = p.X - c.X, dy = p.Y - c.Y, r = BadgeR[i] + 3;
                if (dx * dx + dy * dy <= r * r) { hitIndex = i; return Hit.RmBadge; }
            }
            for (int i = 0; i < Remapper.Inputs; i++)
            {
                int y = RM_Y0 + i * RM_ROW;
                if (p.Y < y || p.Y >= y + RM_H) continue;
                if (p.X >= RM_LIST_X && p.X < RM_PILL_X) { hitIndex = i; return Hit.RmRow; }
                if (p.X >= RM_PILL_X && p.X < RM_PILL_X + RM_ARROW) { hitIndex = i; return Hit.RmPrev; }
                if (p.X >= RM_PILL_X + RM_PILL_W - RM_ARROW && p.X < RM_PILL_X + RM_PILL_W) { hitIndex = i; return Hit.RmNext; }
                if (p.X >= RM_PILL_X && p.X < RM_PILL_X + RM_PILL_W) { hitIndex = i; return Hit.RmMid; }
            }
            return Hit.None;
        }

        void DurTo(int x)
        {
            double t = (x - DUR_X) / (double)DUR_W;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            overlaySec = 1 + (int)Math.Round(t * 9);
            Invalidate();
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

            if (capturing && h != (capTarget == 0 ? Hit.HotChange : Hit.MulKey)) { capturing = false; Invalidate(); }
            if (editing && h != Hit.Number) CommitEdit();

            switch (h)
            {
                case Hit.Close: Close(); break;
                case Hit.Min: WindowState = FormWindowState.Minimized; break;
                case Hit.Gear:
                    themeOpen = !themeOpen;
                    if (themeOpen) popupOpen = false;
                    break;
                case Hit.ThemeItem:
                    if (idx >= 0 && idx < ThemeNames.Length)
                    {
                        theme = idx;
                        ApplyTheme(theme);
                        BuildFonts();
                        rainbowTimer.Enabled = theme == THEME_RAINBOW;
                        SaveSettings();
                    }
                    break;
                case Hit.ThemeOutside: themeOpen = false; break;
                case Hit.MouseBtn:
                    EndRecording(false);
                    page = page == 1 ? 0 : 1;
                    if (page == 1) MouseDetect.Refresh(); // Maus evtl. inzwischen gewechselt
                    popupOpen = false;
                    themeOpen = false;
                    break;
                case Hit.RmSw: SetRemapOn(!remap.On); break;
                case Hit.RmPrev: if (idx >= 0) remap.Cycle(idx, -1); break;
                case Hit.RmNext:
                case Hit.RmMid: if (idx >= 0) remap.Cycle(idx, 1); break;
                case Hit.RmReset: Array.Clear(remap.Map, 0, remap.Map.Length); break;
                case Hit.RmBadge: rmSel = rmSel == idx ? -1 : idx; break;
                case Hit.RmRow: rmSel = rmSel == idx ? -1 : idx; break;
                case Hit.RmRec:
                    if (recording) EndRecording(true); else StartRecording();
                    break;
                case Hit.RmModel:
                    if (recording) break;
                    mouseView = (mouseView + 1) % 4;
                    if (mouseView == 0) MouseDetect.Refresh();
                    if (rmSel >= 0 && !BadgeShown(rmSel, RmModelKind()) && rmSel < 7 && BadgeX[rmSel] >= 0) rmSel = -1;
                    SaveSettings();
                    break;
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
                case Hit.HotChange: capTarget = 0; capturing = !capturing; break;
                case Hit.MulKey: capTarget = 1; capturing = !capturing; break;
                case Hit.Start: Toggle(); break;
                case Hit.MulSw: multiOn = !multiOn; break;
                case Hit.DurSlider:
                    dragKind = 3;
                    DurTo(p.X);
                    break;
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
            else if (dragKind == 3) DurTo(p.X);
            Hit h = HitTest(p);
            int idx = hitIndex;
            if (h != hover || idx != hoverIdx)
            {
                hover = h;
                hoverIdx = idx;
                bool hand = h != Hit.None && h != Hit.Drag && h != Hit.PopPanel && h != Hit.PopOutside
                    && h != Hit.ThemePanel && h != Hit.ThemeOutside;
                Cursor = hand ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDur = dragKind == 3;
            dragKind = 0;
            repeatTimer.Stop();
            if (wasDur) ShowPreview(); // Vorschau mit der eingestellten Dauer
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

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            EndRecording(false);   // sonst wuerde die Aufnahme global Tasten schlucken
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (page == 1 && !themeOpen && e.KeyCode == Keys.Escape)
            {
                page = 0;
                Invalidate();
                e.Handled = true;
                return;
            }
            if (page == 1 && !themeOpen && rmSel >= 0 && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                remap.Cycle(rmSel, e.KeyCode == Keys.Right ? 1 : -1);
                Invalidate();
                e.Handled = true;
                return;
            }
            if (themeOpen && e.KeyCode == Keys.Escape)
            {
                themeOpen = false;
                Invalidate();
                e.Handled = true;
                return;
            }
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

        // ---- Verlaufs-Akzent, Karten, Glow ----
        static Color Lighten(Color c, float t)
        {
            return Color.FromArgb(c.R + (int)((255 - c.R) * t), c.G + (int)((255 - c.G) * t), c.B + (int)((255 - c.B) * t));
        }

        static void FillAccent(Graphics g, Rectangle r, float rad, bool hot)
        {
            if (!fancy || r.Width < 2 || r.Height < 2)
            {
                FillRR(g, hot ? C_AccentHover : C_Accent, r, rad);
                return;
            }
            Color a = hot ? Lighten(C_Accent, 0.18f) : C_Accent;
            Color b = hot ? Lighten(C_Accent2, 0.18f) : C_Accent2;
            using (GraphicsPath p = RR(r, rad))
            using (LinearGradientBrush br = new LinearGradientBrush(r, a, b, LinearGradientMode.Horizontal))
                g.FillPath(br, p);
        }

        static void FillCard(Graphics g, Rectangle r)
        {
            FillRR(g, C_Card, r, radCard);
            if (cardGlow)
            {
                // Neon: weiches Leuchten ausserhalb des Umrisses
                for (int i = 3; i >= 1; i--)
                {
                    Rectangle gr = r;
                    gr.Inflate(i, i);
                    using (GraphicsPath p = RR(gr, radCard + i))
                    using (Pen pen = new Pen(A(10 * (4 - i), C_Accent), 2f))
                        g.DrawPath(pen, p);
                }
            }
        }

        // Hintergrund-Deko je Theme
        void DrawBackdrop(Graphics g)
        {
            if (backdrop == 0 || backdrop == 4) return;
            if (backdrop == 2)
            {
                using (Pen pen = new Pen(A(16, C_Accent), 1f))
                    for (int y = 0; y < H; y += 4) g.DrawLine(pen, 0, y, W, y);
                return;
            }
            if (backdrop == 3)
            {
                using (Pen pen = new Pen(A(14, C_Accent2), 1f))
                {
                    for (int x = 0; x < W; x += 28) g.DrawLine(pen, x, 0, x, H);
                    for (int y = 0; y < H; y += 28) g.DrawLine(pen, 0, y, W, y);
                }
                using (Pen pen = new Pen(A(20, C_Accent), 1f))
                    for (int x = 0; x < W; x += 112) g.DrawLine(pen, x, 0, x, H);
                return;
            }
            // 5: Sonnenglut
            Glow(g, 350, H + 30, 460, 260, A(90, C_Accent));
            Glow(g, 690, -20, 260, 190, A(56, C_Accent2));
        }

        static void Glow(Graphics g, int cx, int cy, int rx, int ry, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                using (PathGradientBrush pb = new PathGradientBrush(p))
                {
                    pb.CenterColor = c;
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, c.R, c.G, c.B) };
                    g.FillPath(pb, p);
                }
            }
        }

        // Griff (Slider/Schalter): rund oder eckig
        static void Thumb(Graphics g, Color fill, Color line, float cx, float cy, float d, float lw)
        {
            RectangleF r = new RectangleF(cx - d / 2f, cy - d / 2f, d, d);
            if (squareThumb)
            {
                using (GraphicsPath p = RR(r, 2.5f))
                {
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                    if (lw > 0) using (Pen pen = new Pen(line, lw)) g.DrawPath(pen, p);
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, r);
                if (lw > 0) using (Pen pen = new Pen(line, lw)) g.DrawEllipse(pen, r);
            }
        }

        // Runder bzw. eckiger Knopf (+/-, Minimieren, Schliessen, Zahnrad)
        static void BtnShape(Graphics g, Color c, Rectangle r)
        {
            if (radCtl < 0)
            {
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r);
            }
            else FillRR(g, c, r, Math.Min(radCtl + 2, r.Height / 2f));
        }

        static void StartGlow(Graphics g)
        {
            if (!fancy || !startGlow) return;
            for (int i = 3; i >= 1; i--)
            {
                Rectangle gr = R_Start;
                gr.Inflate(i * 3, i * 2);
                using (GraphicsPath p = RR(gr, radStart + i * 3))
                using (Pen pen = new Pen(Color.FromArgb(14 * (4 - i), C_Accent), 3f))
                    g.DrawPath(pen, p);
            }
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
            FillCard(g, r);
            StrokeRR(g, C_CardLine, r, radCard, 1f);
            DrawText(g, label, fLabel, C_Dim, new Rectangle(r.X + 16, r.Y + 8, 220, 20), StringAlignment.Near);
        }

        // Einzeilige Karte mit Label links, vertikal zentriert
        void CardInline(Graphics g, Rectangle r, string label)
        {
            FillCard(g, r);
            StrokeRR(g, C_CardLine, r, radCard, 1f);
            DrawText(g, label, fLabel, C_Dim, new Rectangle(r.X + 16, r.Y, 130, r.Height), StringAlignment.Near);
        }

        void RoundBtn(Graphics g, Rectangle r, bool hot, string glyph)
        {
            BtnShape(g, hot ? C_BtnHover : C_Btn, r);
            DrawText(g, glyph, fGlyph, C_Text, r, StringAlignment.Center);
        }

        void Segment(Graphics g, Rectangle left, Rectangle right, string tl, string tr, bool rightSel, Hit hl, Hit hr)
        {
            Rectangle all = Rectangle.Union(left, right);
            float rad = RadCtl(all.Height);
            FillRR(g, C_Field, all, rad);
            StrokeRR(g, C_CardLine, all, rad, 1f);
            Rectangle sel = rightSel ? right : left;
            sel.Inflate(-3, -3);
            Color onSel = segOutline ? C_Accent : C_OnAccent;
            if (segOutline)
            {
                FillRR(g, A(hover == (rightSel ? hr : hl) ? 60 : 38, C_Accent), sel, Math.Max(rad - 3, 1));
                StrokeRR(g, C_Accent, sel, Math.Max(rad - 3, 1), 1.5f);
            }
            else FillAccent(g, sel, Math.Max(rad - 3, 1), hover == (rightSel ? hr : hl));
            DrawText(g, tl, fBtn, !rightSel ? onSel : (hover == hl ? C_Text : C_Dim), left, StringAlignment.Center);
            DrawText(g, tr, fBtn, rightSel ? onSel : (hover == hr ? C_Text : C_Dim), right, StringAlignment.Center);
        }

        void Switch(Graphics g, Rectangle r, bool on, bool hot)
        {
            Color track = on ? (hot ? C_AccentHover : C_Accent) : (hot ? Color.FromArgb(58, 58, 58) : Color.FromArgb(44, 44, 44));
            if (on && segOutline)
            {
                FillRR(g, A(60, C_Accent), r, RadCtl(r.Height));
                StrokeRR(g, C_Accent, r, RadCtl(r.Height), 1.5f);
            }
            else if (on && fancy) FillAccent(g, r, RadCtl(r.Height), hot);
            else FillRR(g, track, r, RadCtl(r.Height));
            int d = r.Height - 6;
            int kx = on ? r.Right - d - 3 : r.X + 3;
            Thumb(g, on ? (segOutline ? C_Accent : C_Thumb) : Color.FromArgb(160, 160, 160), Color.Empty, kx + d / 2f, r.Y + 3 + d / 2f, d, 0);
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
                FillRR(g, C_Btn, r, RadCtl(CHIP_H));
                StrokeRR(g, C_CardLine, r, RadCtl(CHIP_H), 1f);
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
                FillRR(g, hot ? A(36, C_Accent) : C_Field, addRect, RadCtl(CHIP_H));
                StrokeRR(g, C_Accent, addRect, RadCtl(CHIP_H), 1f);
                DrawText(g, addText, fChip, C_Accent, addRect, StringAlignment.Center);
            }
        }

        void DrawPopup(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(b, 0, 56, W, H - 56);
            FillRR(g, A(255, C_Card), R_Pop, Math.Min(radCard, 16f));
            StrokeRR(g, C_Accent, R_Pop, Math.Min(radCard, 16f), 1f);
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
            DrawBackdrop(g);

            bool running = clicker.Running;

            // Titelleiste
            int titleX = 24;
            if (logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(22, 16, 24, 24));
                titleX = 54;
            }
            DrawText(g, titleText, fTitle, C_Text, new Rectangle(titleX, 14, 190, 28), StringAlignment.Near);
            float titleW = g.MeasureString(titleText, fTitle, 2000, StringFormat.GenericTypographic).Width;
            Color dot = running ? C_Accent : (active ? Color.FromArgb(200, 200, 200) : Color.FromArgb(64, 64, 64));
            using (SolidBrush b = new SolidBrush(dot))
                g.FillEllipse(b, titleX + titleW + 12, 24, 8, 8);
            string status = running ? "läuft" : (active ? "bereit" : "wartet auf Programm");
            DrawText(g, status, fLabel, C_Dim, new Rectangle((int)(titleX + titleW + 26), 14, 220, 28), StringAlignment.Near);

            BtnShape(g, hover == Hit.Min ? C_BtnHover : C_Btn, R_Min);
            BtnShape(g, hover == Hit.Close ? Color.FromArgb(214, 60, 72) : C_Btn, R_Close);
            using (Pen pen = new Pen(C_Text, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, R_Min.X + 10, R_Min.Y + 15, R_Min.X + 20, R_Min.Y + 15);
                g.DrawLine(pen, R_Close.X + 10, R_Close.Y + 10, R_Close.X + 20, R_Close.Y + 20);
                g.DrawLine(pen, R_Close.X + 20, R_Close.Y + 10, R_Close.X + 10, R_Close.Y + 20);
            }

            DrawGear(g, hover == Hit.Gear || themeOpen);
            DrawMouseIcon(g, hover == Hit.MouseBtn);
            if (page == 1)
            {
                DrawRemapPage(g);
                if (themeOpen) DrawThemeMenu(g);
                return;
            }

            // Intervall
            Card(g, R_CardAuto, "INTERVALL");
            using (Pen pen = new Pen(C_CardLine, 1f))
                g.DrawLine(pen, AUTO_DIV_X, R_CardAuto.Y + 18, AUTO_DIV_X, R_CardAuto.Bottom - 18);
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
            FillAccent(g, new Rectangle(SL_X, SL_Y - 3, Math.Max(tx - SL_X, 6), 6), 3, false);
            Thumb(g, Color.White, C_Accent, tx, SL_Y, 16, 2f);

            // Maustaste / Klick-Art
            DrawText(g, "MAUSTASTE", fLabel, C_Dim, new Rectangle(AUTO_LBL_X, R_BtnL.Y, AUTO_LBL_W, R_BtnL.Height), StringAlignment.Near);
            Segment(g, R_BtnL, R_BtnR, "Links", "Rechts", clicker.Right, Hit.BtnLeft, Hit.BtnRight);
            DrawText(g, "KLICK-ART", fLabel, C_Dim, new Rectangle(AUTO_LBL_X, R_ClkS.Y, AUTO_LBL_W, R_ClkS.Height), StringAlignment.Near);
            Segment(g, R_ClkS, R_ClkD, "Single", "Double", clicker.Double, Hit.ClkSingle, Hit.ClkDouble);

            // Hotkey
            DrawText(g, "HOTKEY", fLabel, C_Dim, new Rectangle(AUTO_LBL_X, R_HotPill.Y, AUTO_LBL_W, R_HotPill.Height), StringAlignment.Near);
            FillRR(g, C_Field, R_HotPill, RadCtl(R_HotPill.Height));
            bool capMain = capturing && capTarget == 0;
            bool capMul = capturing && capTarget == 1;
            StrokeRR(g, capMain ? C_Accent : C_CardLine, R_HotPill, RadCtl(R_HotPill.Height), capMain ? 1.5f : 1f);
            if (capMain)
                DrawText(g, "Taste drücken…", fBtn, C_Accent,
                    new Rectangle(R_HotPill.X + 16, R_HotPill.Y, 118, R_HotPill.Height), StringAlignment.Near);
            else
                DrawText(g, hotkey.Name(), fBtn, C_Text,
                    new Rectangle(R_HotPill.X + 16, R_HotPill.Y, 118, R_HotPill.Height), StringAlignment.Near);
            FillRR(g, hover == Hit.HotChange ? C_BtnHover : C_Btn, R_HotBtn, RadCtl(R_HotBtn.Height));
            DrawText(g, capMain ? "Abbruch" : "Ändern", fBtn, C_Text, R_HotBtn, StringAlignment.Center);

            // Autostart (Titelleiste): mit Windows starten
            DrawText(g, "Autostart", fChip, autostart ? C_Text : C_Dim,
                new Rectangle(R_AutoHit.X, R_AutoHit.Y, R_AutoSw.X - R_AutoHit.X - 8, R_AutoHit.Height), StringAlignment.Far);
            Switch(g, R_AutoSw, autostart, hover == Hit.AutoSw);

            // Multiplikator: 1 echter Klick = N Klicks
            CardInline(g, R_CardMul, "MULTIPLIKATOR");
            double mt = (multiplier - 1) / 9.0;
            int mx = MUL_X + (int)Math.Round(mt * MUL_W);
            Color mulColor = multiOn ? C_Accent : Color.FromArgb(70, 70, 70);
            FillRR(g, Color.FromArgb(34, 34, 34), new Rectangle(MUL_X, MUL_Y - 3, MUL_W, 6), 3);
            if (multiOn) FillAccent(g, new Rectangle(MUL_X, MUL_Y - 3, Math.Max(mx - MUL_X, 6), 6), 3, false); else FillRR(g, mulColor, new Rectangle(MUL_X, MUL_Y - 3, Math.Max(mx - MUL_X, 6), 6), 3);
            Thumb(g, multiOn ? Color.White : Color.FromArgb(150, 150, 150), mulColor, mx, MUL_Y, 16, 2f);
            for (int i = 0; i < 10; i++)
            {
                int tx2 = MUL_X + (int)Math.Round(i / 9.0 * MUL_W);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(i < multiplier ? 90 : 50, 150, 150, 150)))
                    g.FillEllipse(b, tx2 - 1, MUL_Y + 12, 3, 3);
            }
            DrawText(g, multiplier + "×", fBtn, multiOn ? C_Accent : C_Dim, new Rectangle(R_CardMul.X + 236, R_CardMul.Y, 38, R_CardMul.Height), StringAlignment.Center);
            // Hotkey-Feld: Klick, dann Taste oder Maustaste druecken (Esc entfernt)
            FillRR(g, C_Field, R_MulKey, RadCtl(R_MulKey.Height));
            StrokeRR(g, capMul ? C_Accent : (hover == Hit.MulKey ? C_Dim : C_CardLine), R_MulKey, RadCtl(R_MulKey.Height), capMul ? 1.5f : 1f);
            DrawText(g, capMul ? "Taste…" : (mulKey.Code == 0 ? "–" : mulKey.Short()), fChip,
                capMul ? C_Accent : (mulKey.Code == 0 ? C_Dim : C_Text),
                new Rectangle(R_MulKey.X + 3, R_MulKey.Y, R_MulKey.Width - 6, R_MulKey.Height), StringAlignment.Center);
            Switch(g, R_MulSw, multiOn, hover == Hit.MulSw);

            // Dauer der Overlay-Meldung (1-10 s)
            CardInline(g, R_CardDur, "MELDUNG");
            int dx = DUR_X + (int)Math.Round((overlaySec - 1) / 9.0 * DUR_W);
            FillRR(g, Color.FromArgb(34, 34, 34), new Rectangle(DUR_X, MUL_Y - 3, DUR_W, 6), 3);
            FillAccent(g, new Rectangle(DUR_X, MUL_Y - 3, Math.Max(dx - DUR_X, 6), 6), 3, false);
            Thumb(g, Color.White, C_Accent, dx, MUL_Y, 16, 2f);
            for (int i = 0; i < 10; i++)
            {
                int tx3 = DUR_X + (int)Math.Round(i / 9.0 * DUR_W);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(i < overlaySec ? 90 : 50, 150, 150, 150)))
                    g.FillEllipse(b, tx3 - 1, MUL_Y + 12, 3, 3);
            }
            DrawText(g, overlaySec + " s", fBtn, C_Accent, new Rectangle(R_CardDur.X + 186, R_CardDur.Y, 36, R_CardDur.Height), StringAlignment.Center);

            // Nur in Programmen
            Card(g, R_CardProg, "NUR IN PROGRAMMEN");
            string hint = !filterOn ? "Aus – der Hotkey wirkt überall"
                : (programs.Count == 0 ? "Noch kein Programm gewählt" : "Hotkey nur aktiv, wenn eines davon im Vordergrund ist");
            DrawText(g, hint, fChip, C_Dim, new Rectangle(R_CardProg.X + 150, R_CardProg.Y + 8, 440, 20), StringAlignment.Near);
            Switch(g, R_FilterSw, filterOn, hover == Hit.FilterSw);
            DrawChips(g);

            // Start/Stop
            string startLabel = string.Format(running ? stopFmt : startFmt, hotkey.Short());
            if (running != startOutline)
            {
                // Umriss-Variante (Original: laufend; Neon: bereit)
                if (startGlow || !fancy)
                    for (int i = 4; i >= 1; i--)
                    {
                        Rectangle gr = R_Start;
                        gr.Inflate(i * 2, i * 2 > 6 ? 3 : i);
                        using (GraphicsPath p = RR(gr, radStart + i * 2))
                        using (Pen pen = new Pen(Color.FromArgb(18 * (5 - i), C_Accent), 2f))
                            g.DrawPath(pen, p);
                    }
                FillRR(g, hover == Hit.Start ? A(255, Lighten(C_Field, 0.08f)) : A(255, fancy ? C_Field : C_Bg), R_Start, radStart);
                StrokeRR(g, C_Accent, R_Start, radStart, 1.5f);
                DrawText(g, startLabel, fBtn, C_Accent, R_Start, StringAlignment.Center);
            }
            else
            {
                StartGlow(g);
                FillAccent(g, R_Start, radStart, hover == Hit.Start);
                DrawText(g, startLabel, fBtn, C_OnAccent, R_Start, StringAlignment.Center);
            }

            if (popupOpen) DrawPopup(g);
            if (themeOpen) DrawThemeMenu(g);
        }


        // Farbwechsel-Symbol oben rechts (Theme-Auswahl): Farbrad aus sechs Segmenten
        void DrawGear(Graphics g, bool hot)
        {
            BtnShape(g, hot ? C_BtnHover : C_Btn, R_Gear);
            float cx = R_Gear.X + R_Gear.Width / 2f, cy = R_Gear.Y + R_Gear.Height / 2f;
            Color[] wheel = {
                Color.FromArgb(255, 94, 160), Color.FromArgb(255, 166, 77), Color.FromArgb(57, 255, 120),
                Color.FromArgb(0, 229, 255), Color.FromArgb(124, 108, 255), Color.FromArgb(255, 43, 214) };
            float rad = hot ? 9.6f : 9f;
            for (int k = 0; k < wheel.Length; k++)
                using (SolidBrush b = new SolidBrush(wheel[k]))
                    g.FillPie(b, cx - rad, cy - rad, rad * 2, rad * 2, -90 + k * 60, 60);
            using (SolidBrush b = new SolidBrush(hot ? C_BtnHover : C_Btn))
                g.FillEllipse(b, cx - 3.6f, cy - 3.6f, 7.2f, 7.2f);
        }

        // Maus-Symbol in der Titelleiste (oeffnet die Umbelegung)
        void DrawMouseIcon(Graphics g, bool hot)
        {
            BtnShape(g, hot || page == 1 ? C_BtnHover : C_Btn, R_MouseBtn);
            float cx = R_MouseBtn.X + R_MouseBtn.Width / 2f, cy = R_MouseBtn.Y + R_MouseBtn.Height / 2f;
            Color c = page == 1 ? C_Accent : C_Text;
            using (GraphicsPath p = RR(new RectangleF(cx - 6f, cy - 9f, 12f, 18f), 6f))
            using (Pen pen = new Pen(c, 1.6f))
            {
                g.DrawPath(pen, p);
                g.DrawLine(pen, cx, cy - 9f, cx, cy - 2.5f);
                g.DrawLine(pen, cx - 6f, cy - 2.5f, cx + 6f, cy - 2.5f);
            }
        }

        // Maus-Seite: Liste "Eingabe -> Aktion"
        void DrawRemapPage(Graphics g)
        {
            bool on = remap.On;
            Card(g, R_RmCard, "MAUS-UMBELEGUNG");
            string hint = recording ? "Alle Tasten drücken, Rad drehen und kippen – Enter = fertig"
                : rmNote.Length > 0 ? rmNote
                : !on ? "Aus – die Maus verhält sich normal"
                : (filterOn ? "Gilt nur, wenn eines der gewählten Programme im Vordergrund ist" : "Gilt in allen Programmen");
            DrawText(g, hint, fChip, recording ? C_Accent : C_Dim, new Rectangle(R_RmCard.X + 170, R_RmCard.Y + 8, 420, 20), StringAlignment.Near);
            Switch(g, R_RmSw, on, hover == Hit.RmSw);

            // Mausbild links, Trennlinie, Liste rechts
            int kind = RmModelKind();
            DrawMouseModel(g, kind);
            using (Pen sep = new Pen(C_CardLine, 1f))
                g.DrawLine(sep, RM_LIST_X - 12, RM_Y0, RM_LIST_X - 12, RM_Y0 + Remapper.Inputs * RM_ROW - 5);
            for (int i = 0; i < Remapper.Inputs; i++)
                if (BadgeShown(i, kind)) DrawBadge(g, i);
            DrawText(g, RmModelText(), fChip, hover == Hit.RmModel && !recording ? C_Text : C_Dim, R_RmModel, StringAlignment.Center);

            // Aufnahme-Knopf
            bool hr = hover == Hit.RmRec;
            if (recording)
            {
                FillAccent(g, R_RmRec, RadCtl(R_RmRec.Height), hr);
                DrawText(g, "■  Fertig (Enter)", fChip, C_OnAccent, R_RmRec, StringAlignment.Center);
            }
            else
            {
                FillRR(g, hr ? C_BtnHover : C_Btn, R_RmRec, RadCtl(R_RmRec.Height));
                StrokeRR(g, C_CardLine, R_RmRec, RadCtl(R_RmRec.Height), 1f);
                DrawText(g, "●  Tasten aufnehmen", fChip, C_Text, R_RmRec, StringAlignment.Center);
            }
            DrawText(g, rmSeenKnown ? "Tasten, die Windows nicht meldet (z. B. DPI), lassen sich nicht umbelegen"
                                    : "„Tasten aufnehmen“ zeigt, welche Tasten deine Maus an Windows meldet",
                fChip, C_Dim, new Rectangle(RM_LIST_X, 420, RM_PILL_X + RM_PILL_W - RM_LIST_X, 16), StringAlignment.Near);

            string[] glyph = { "1", "2", "3", "4", "5", "↑", "↓", "←", "→" };
            for (int i = 0; i < Remapper.Inputs; i++)
            {
                int y = RM_Y0 + i * RM_ROW;
                int act = remap.Map[i];
                bool changed = act != 0;
                bool lit = rmSel == i || (hoverIdx == i && (hover == Hit.RmBadge || hover == Hit.RmRow || hover == Hit.RmPrev || hover == Hit.RmNext || hover == Hit.RmMid));
                if (lit)
                {
                    Rectangle row = new Rectangle(RM_LIST_X - 2, y - 2, RM_PILL_X + RM_PILL_W - RM_LIST_X + 4, RM_H + 4);
                    FillRR(g, rmSel == i ? Color.FromArgb(40, C_Accent) : Color.FromArgb(22, 255, 255, 255), row, 10);
                    if (rmSel == i) StrokeRR(g, Color.FromArgb(120, C_Accent), row, 10, 1f);
                }
                // Nummern-Marker wie im Mausbild
                Rectangle badge = new Rectangle(RM_LIST_X + 6, y + 4, 22, 22);
                bool seenNow = recording && recSeen[i];
                bool miss = !recording && rmSeenKnown && !rmSeen[i];
                using (SolidBrush b = new SolidBrush(seenNow ? SeenGreen : changed && on ? C_Accent : C_Btn))
                    g.FillEllipse(b, badge);
                DrawText(g, glyph[i], fChip, seenNow ? Color.White : changed && on ? C_OnAccent : miss ? C_Dim : C_Text, badge, StringAlignment.Center);
                DrawText(g, ShortNames[i], fChip, !on || miss ? C_Dim : C_Text, new Rectangle(RM_LIST_X + 36, y, RM_PILL_X - RM_LIST_X - 40, RM_H), StringAlignment.Near);
                if (seenNow)
                    DrawText(g, "✓", fBtn, SeenGreen, new Rectangle(RM_PILL_X - 30, y, 24, RM_H), StringAlignment.Center);
                else if (miss)
                    DrawText(g, "–", fBtn, C_Dim, new Rectangle(RM_PILL_X - 30, y, 24, RM_H), StringAlignment.Center);

                Rectangle pill = new Rectangle(RM_PILL_X, y, RM_PILL_W, RM_H);
                FillRR(g, C_Field, pill, RadCtl(RM_H));
                StrokeRR(g, changed && on ? C_Accent : C_CardLine, pill, RadCtl(RM_H), 1f);
                bool hp = hover == Hit.RmPrev && hoverIdx == i, hn = hover == Hit.RmNext && hoverIdx == i;
                bool hm = hover == Hit.RmMid && hoverIdx == i;
                DrawText(g, "‹", fGlyph, hp ? C_Text : C_Dim, new Rectangle(RM_PILL_X, y, RM_ARROW, RM_H), StringAlignment.Center);
                DrawText(g, "›", fGlyph, hn ? C_Text : C_Dim, new Rectangle(RM_PILL_X + RM_PILL_W - RM_ARROW, y, RM_ARROW, RM_H), StringAlignment.Center);
                DrawText(g, Remapper.ActionNames[act], fBtn, changed ? (on ? C_Accent : C_Text) : (hm ? C_Text : C_Dim),
                    new Rectangle(RM_PILL_X + RM_ARROW, y, RM_PILL_W - 2 * RM_ARROW, RM_H), StringAlignment.Center);
            }

            FillRR(g, hover == Hit.RmReset ? C_BtnHover : C_Btn, R_Start, radStart);
            StrokeRR(g, C_CardLine, R_Start, radStart, 1f);
            DrawText(g, "Alle auf Standard zurücksetzen", fBtn, C_Text, R_Start, StringAlignment.Center);
        }

        static readonly Color SeenGreen = Color.FromArgb(46, 160, 90);

        // Marker auf dem Mausbild: Nummer bzw. Pfeil, Akzentfarbe wenn umbelegt, Ring wenn gewaehlt
        void DrawBadge(Graphics g, int i)
        {
            string[] glyph = { "1", "2", "3", "4", "5", "↑", "↓" };
            PointF c = BadgePt(i);
            float r = BadgeR[i];
            bool changed = remap.On && remap.Map[i] != 0;
            bool seenNow = recording && recSeen[i];
            bool miss = !recording && rmSeenKnown && !rmSeen[i];
            bool hot = hoverIdx == i && (hover == Hit.RmBadge || hover == Hit.RmRow || hover == Hit.RmPrev || hover == Hit.RmNext || hover == Hit.RmMid);
            RectangleF rc = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
            if (rmSel == i)
            {
                using (Pen ring = new Pen(C_Accent, 2.2f))
                    g.DrawEllipse(ring, c.X - r - 3.5f, c.Y - r - 3.5f, 2 * r + 7f, 2 * r + 7f);
            }
            using (SolidBrush b = new SolidBrush(seenNow ? SeenGreen : changed ? C_Accent : hot ? Color.FromArgb(84, 84, 98) : miss ? Color.FromArgb(18, 18, 22) : Color.FromArgb(30, 30, 36)))
                g.FillEllipse(b, rc);
            using (Pen o = new Pen(Color.FromArgb(miss ? 80 : 210, 255, 255, 255), 1.3f))
                g.DrawEllipse(o, rc);
            DrawText(g, glyph[i], fChip, seenNow ? Color.White : changed ? C_OnAccent : miss ? Color.FromArgb(120, 120, 130) : Color.White,
                new Rectangle((int)Math.Round(c.X - r), (int)Math.Round(c.Y - r), (int)Math.Round(2 * r), (int)Math.Round(2 * r)), StringAlignment.Center);
        }

        // Foto der eigenen Maus oder gezeichnetes Standardmodell (5 oder 3 Tasten)
        void DrawMouseModel(Graphics g, int kind)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(MS_X, MS_Y);
            g.ScaleTransform(MS_S, MS_S);
            if (kind == 0 && mousePhoto != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(mousePhoto, new RectangleF(0, 0, MS_W, MS_H));
                g.Restore(st);
                return;
            }

            // Koerper (rechte Haelfte als Punktliste, links gespiegelt)
            PointF[] right = { new PointF(118, 6), new PointF(165, 14), new PointF(198, 44), new PointF(212, 95), new PointF(213, 160),
                               new PointF(210, 230), new PointF(209, 290), new PointF(216, 335), new PointF(214, 370), new PointF(190, 392), new PointF(150, 397) };
            List<PointF> pts = new List<PointF>(right);
            pts.Add(new PointF(118, 399));
            for (int k = right.Length - 1; k >= 1; k--) pts.Add(new PointF(236 - right[k].X, right[k].Y));
            using (GraphicsPath body = new GraphicsPath())
            {
                body.AddClosedCurve(pts.ToArray(), 0.5f);
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(0, 0, 236, 437), Color.FromArgb(250, 250, 253), Color.FromArgb(214, 214, 226), LinearGradientMode.Vertical))
                    g.FillPath(fill, body);

                // gewaehlte Taste tint-faerben (Flaechen werden auf den Koerper beschnitten)
                int s = rmSel >= 0 ? rmSel : -1;
                int h2 = (hover == Hit.RmBadge || hover == Hit.RmRow) ? hoverIdx : -1;
                GraphicsState clipSt = g.Save();
                g.SetClip(body, CombineMode.Intersect);
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(70, C_Accent)))
                {
                    if (s == 0 || h2 == 0) g.FillRectangle(tint, 0, 0, 118, 182);
                    if (s == 1 || h2 == 1) g.FillRectangle(tint, 118, 0, 118, 182);
                }
                g.Restore(clipSt);
                using (Pen outline = new Pen(Color.FromArgb(70, 70, 82), 2.2f))
                {
                    outline.LineJoin = LineJoin.Round;
                    g.DrawPath(outline, body);
                    // Tastentrennung: Mittelnaht oben/unten und Bogen unter den Haupttasten
                    using (Pen seam = new Pen(Color.FromArgb(110, 110, 124), 1.6f))
                    {
                        g.DrawLine(seam, 118, 8, 118, 50);
                        g.DrawLine(seam, 118, 120, 118, 180);
                        g.DrawBezier(seam, 27, 174, 70, 190, 166, 190, 209, 174);
                    }

                    // Mausrad
                    RectangleF wheel = new RectangleF(104, 50, 28, 70);
                    bool wsel = rmSel == 2 || h2 == 2;
                    using (GraphicsPath wp = RR(wheel, 9f))
                    {
                        using (SolidBrush wb = new SolidBrush(wsel ? Color.FromArgb(50, 40, 80) : Color.FromArgb(30, 30, 36)))
                            g.FillPath(wb, wp);
                        using (Pen wo = new Pen(wsel ? C_Accent : Color.FromArgb(90, 90, 100), 1.8f)) g.DrawPath(wo, wp);
                    }
                    using (Pen groove = new Pen(Color.FromArgb(90, 90, 104), 1.2f))
                        for (int gy = 60; gy < 112; gy += 7) g.DrawLine(groove, 108, gy, 128, gy);
                    using (SolidBrush led = new SolidBrush(C_Accent))
                        g.FillEllipse(led, 115.5f, 144f, 5f, 10f);

                    // Seitentasten (nur 5-Tasten-Modell)
                    if (kind == 1)
                    {
                        for (int k = 3; k <= 4; k++)
                        {
                            bool ssel = rmSel == k || h2 == k;
                            RectangleF sb = new RectangleF(12, BadgeY[k] - 19, 15, 38);
                            using (GraphicsPath sp = RR(sb, 6f))
                            {
                                using (SolidBrush sbr = new SolidBrush(ssel ? Color.FromArgb(200, C_Accent) : Color.FromArgb(240, 240, 246)))
                                    g.FillPath(sbr, sp);
                                g.DrawPath(outline, sp);
                            }
                        }
                    }
                }
            }
            g.Restore(st);
        }

        void DrawThemeMenu(Graphics g)
        {
            FillRR(g, Color.FromArgb(18, 18, 22), R_ThemePanel, 14);
            StrokeRR(g, Color.FromArgb(70, 70, 82), R_ThemePanel, 14, 1f);
            for (int i = 0; i < ThemeNames.Length; i++)
            {
                Rectangle r = new Rectangle(R_ThemePanel.X + 8, THEME_Y0 + i * THEME_ROW, R_ThemePanel.Width - 16, THEME_ROW - 4);
                bool sel = i == theme;
                bool hot = hover == Hit.ThemeItem && hoverIdx == i;
                Color selCol = i == THEME_RAINBOW ? C_Accent : ThemeA[i];
                if (sel) FillRR(g, Color.FromArgb(40, selCol), r, 10);
                else if (hot) FillRR(g, C_BtnHover, r, 10);
                Rectangle sw = new Rectangle(r.X + 12, r.Y + (r.Height - 18) / 2, 18, 18);
                if (i == THEME_RAINBOW)
                {
                    Color[] wheel = { Color.FromArgb(255, 94, 94), Color.FromArgb(255, 200, 70), Color.FromArgb(90, 240, 120),
                                      Color.FromArgb(70, 210, 255), Color.FromArgb(110, 120, 255), Color.FromArgb(230, 90, 255) };
                    for (int k = 0; k < wheel.Length; k++)
                        using (SolidBrush b = new SolidBrush(wheel[k]))
                            g.FillPie(b, sw.X, sw.Y, sw.Width, sw.Height, -90 + k * 60, 60);
                }
                else if (i == 0)
                {
                    using (SolidBrush b = new SolidBrush(ThemeA[0])) g.FillEllipse(b, sw);
                }
                else
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(sw, ThemeA[i], ThemeB[i], LinearGradientMode.ForwardDiagonal))
                        g.FillEllipse(br, sw);
                }
                DrawText(g, ThemeNames[i], fBtn, sel || hot ? C_Text : C_Dim, new Rectangle(r.X + 40, r.Y, 90, r.Height), StringAlignment.Near);
                if (sel)
                {
                    using (Pen pen = new Pen(selCol, 2f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        int ox = r.Right - 26, oy = r.Y + r.Height / 2;
                        g.DrawLines(pen, new Point[] { new Point(ox, oy), new Point(ox + 4, oy + 4), new Point(ox + 11, oy - 4) });
                    }
                }
            }
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
                        case "MouseView": mouseView = v >= 0 && v <= 3 ? v : 0; break;
                        case "MouseSeen":
                            if (raw.Length == Remapper.Inputs)
                            {
                                for (int si = 0; si < raw.Length; si++) rmSeen[si] = raw[si] == '1';
                                rmSeenKnown = true;
                            }
                            break;
                        case "RemapOn": remap.On = v != 0; break;
                        case "Remap": remap.Load(raw); break;
                        case "MulHotMouse": mulKey.IsMouse = v != 0; break;
                        case "MulHotCode": mulKey.Code = v; break;
                        case "Multi": multiplier = Math.Min(Math.Max(v, 1), 10); break;
                        case "OverlaySec": overlaySec = Math.Min(Math.Max(v, 1), 10); break;
                        case "Theme": theme = ThemeFromSetting(raw); break;
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
                if ((mulKey.IsMouse && (mulKey.Code < 1 || mulKey.Code > 5)) || mulKey.Code < 0 ||
                    (mulKey.IsMouse == hotkey.IsMouse && mulKey.Code == hotkey.Code))
                    mulKey = new Hotkey(false, 0);
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
                    "MouseView=" + mouseView,
                    "MouseSeen=" + SeenString(),
                    "RemapOn=" + (remap.On ? 1 : 0),
                    "Remap=" + remap.Serialize(),
                    "MulHotMouse=" + (mulKey.IsMouse ? 1 : 0),
                    "MulHotCode=" + mulKey.Code,
                    "Multi=" + multiplier,
                    "OverlaySec=" + overlaySec,
                    "Theme=" + ThemeNames[theme],
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
                mulKey = new Hotkey(true, 5);
                remap.On = true;
                remap.Map[5] = 1;   // Rad hoch -> Linksklick
                remap.Map[3] = 9;   // Maus 4 -> Deaktiviert
                remap.Map[2] = 8;   // Mausrad-Klick -> Doppelklick
                page = snapPage;
                if (snapRec == 1) { recording = true; recSeen[0] = recSeen[2] = recSeen[3] = recSeen[5] = true; }
                if (snapRec == 2)
                {
                    rmSeenKnown = true;
                    for (int ri = 0; ri < 7; ri++) rmSeen[ri] = true;
                    rmNote = "Erkannt: 5 Tasten + Rad";
                }
                popupOpen = !snapThemePop;
                themeOpen = snapThemePop;
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
                    themeOpen = false;
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
                else if (args[i] == "--theme" && i + 1 < args.Length) { int tn; int.TryParse(args[++i], out tn); MainForm.themeArg = tn; }
                else if (args[i] == "--themepop") MainForm.snapThemePop = true;
                else if (args[i] == "--rec" && i + 1 < args.Length) { int rn; int.TryParse(args[++i], out rn); MainForm.snapRec = rn; }
                else if (args[i] == "--view" && i + 1 < args.Length) { int vn; int.TryParse(args[++i], out vn); MainForm.snapView = vn; }
                else if (args[i] == "--page" && i + 1 < args.Length) { int pn; int.TryParse(args[++i], out pn); MainForm.snapPage = pn; }
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
