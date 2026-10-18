using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

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
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        public static extern bool PlaySound(string name, IntPtr hmod, uint flags);
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
                Native.timeBeginPeriod(1);   // genaue Abstaende nur waehrend der Klickfolge
                try
                {
                    Thread.Sleep(15); // echtes Loslassen zuerst durchlassen
                    while (!quit && Interlocked.Add(ref pendingBurst, -1) >= 0)
                    {
                        Click();
                        Thread.Sleep(BurstGapMs);
                    }
                    Interlocked.Exchange(ref pendingBurst, 0);
                }
                finally { Native.timeEndPeriod(1); }
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

                // 1-ms-Zeitaufloesung von Windows nur anfordern, solange geklickt wird (spart sonst Strom)
                Native.timeBeginPeriod(1);
                try
                {
                    while (running && !quit)
                    {
                        sw.Reset();
                        sw.Start();
                        Click();
                        if (Double)
                        {
                            Thread.Sleep(10);
                            if (!running) break;
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
                finally { Native.timeEndPeriod(1); }
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
        static readonly string[] KnownIds = { "VID_1D57&PID_FA60" };   // Attack Shark X3 (Funk-Dongle)
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

    // ------------------------------------------------------------------ Akku
    enum DevKind { Headset, Mouse, Keyboard }

    struct BattReading
    {
        public bool Ok;          // Geraet hat einen gueltigen Wert geliefert
        public int Percent;      // 1-100
        public bool Charging;
    }

    class BattDev
    {
        public string Name;
        public DevKind Kind;
        public bool Coarse;                  // meldet nur 10-%-Schritte
        public Func<BattReading> Query;
        // Zustand fuer Anzeige und Meldungen (nur im UI-Thread benutzt)
        public bool Polled;                  // mindestens einmal abgefragt
        public bool Online;                  // letzte Abfrage hatte einen Wert
        public bool Known;                   // es gab schon einen gueltigen Wert
        public BattReading Last;
        public int LastSeen;                 // TickCount des letzten gueltigen Werts
        public readonly List<int> Announced = new List<int>();   // in dieser Entladung schon gemeldete Stufen
        public bool FullShown;
    }

    // Liest den Akkustand der Funkgeraete direkt ueber HID, ohne Herstellersoftware.
    // Es werden nur Lese-Befehle gesendet; jede Ein-/Ausgabe hat einen Timeout.
    static class Battery
    {
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr w, int f);
        [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr dev);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, IntPtr b, int n, IntPtr w, IntPtr o);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, IntPtr b, int n, IntPtr r, IntPtr o);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr o, out int n, bool wait);
        [DllImport("kernel32.dll")] static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);

        [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }
        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Res;
            public ushort NLinkColl, NInBtn, NInVal, NInData, NOutBtn, NOutVal, NOutData, NFeatBtn, NFeatVal, NFeatData;
        }

        public static readonly BattDev[] Devices = {
            new BattDev { Name = "Corsair HS80 MAX", Kind = DevKind.Headset, Query = QueryCorsair },
            new BattDev { Name = "ROG Strix Scope II 96", Kind = DevKind.Keyboard, Query = QueryRog },
            new BattDev { Name = "Attack Shark X3", Kind = DevKind.Mouse, Coarse = true, Query = QueryShark },
        };

        public static volatile bool Fast;      // beim Hochfahren alle 5 s statt alle 60 s
        public static bool Sim;                // --battery-sim: erfundene Werte im Sekundentakt
        static volatile bool stop;
        static int pokeAt;
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static Action<BattDev, BattReading> onReading;

        // Hintergrund-Thread: fragt alle Geraete ab und meldet jedes Ergebnis (aus dem Thread heraus)
        public static void Start(Action<BattDev, BattReading> cb)
        {
            onReading = cb;
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        public static void Stop() { stop = true; wake.Set(); }

        // Neu abfragen, z. B. nach dem An-/Abstecken; mehrere Anstoesse kurz hintereinander zaehlen als einer
        public static void Poke(int delayMs)
        {
            pokeAt = Environment.TickCount + delayMs;
            wake.Set();
        }

        static void Loop()
        {
            int tick = 0;
            while (!stop)
            {
                foreach (BattDev d in Devices)
                {
                    BattReading r;
                    try { r = Sim ? SimQuery(d, tick) : d.Query(); }
                    catch { r = new BattReading(); }
                    if (stop) return;
                    onReading(d, r);
                }
                tick++;
                if (wake.WaitOne(Sim ? 1000 : Fast ? 5000 : 60000))
                {
                    int left;
                    while (!stop && (left = pokeAt - Environment.TickCount) > 0) wake.WaitOne(left);
                }
            }
        }

        // ---- Geraete ----
        // Corsair (Bragi-Protokoll): Eigenschaft 0x0F = Akku in Promille
        static BattReading QueryCorsair()
        {
            BattReading r = new BattReading();
            byte[] a = Transfer("vid_1b1c&pid_0a97&mi_04&col01", 0xFF42, new byte[] { 0x02, 0x09, 0x02, 0x0F }, 300,
                delegate(byte[] x) { return x[0] == 0x01 && x[2] == 0x02; });
            if (a == null || a[3] != 0x00) return r;
            int permille = a[4] | (a[5] << 8);
            if (permille <= 0 || permille > 1000) return r;
            r.Ok = true;
            r.Percent = Math.Max(1, (permille + 5) / 10);
            return r;
        }

        // ASUS ROG Tastatur am Omni Receiver: 12 01 -> Byte 6 Prozent, Byte 9 laedt; FF AA = nicht erreichbar
        static BattReading QueryRog()
        {
            BattReading r = new BattReading();
            byte[] a = Transfer("vid_0b05&pid_1ace&mi_02&col02", 0xFF00, new byte[] { 0x02, 0x12, 0x01 }, 300,
                delegate(byte[] x) { return x[0] == 0x02 && ((x[1] == 0x12 && x[2] == 0x01) || (x[1] == 0xFF && x[2] == 0xAA)); });
            if (a == null || a[1] != 0x12 || a[6] < 1 || a[6] > 100) return r;
            r.Ok = true;
            r.Percent = a[6];
            r.Charging = a[9] == 1;
            return r;
        }

        // Attack Shark X3: nur lesen, der Dongle sendet alle 2 s "03 10 40 xx Stufe" (Stufe 1-10 = 10-100 %);
        // deshalb laenger als einen Takt warten
        static BattReading QueryShark()
        {
            BattReading r = new BattReading();
            byte[] a = Transfer("vid_1d57&pid_fa60&mi_02&col03", 0x000A, null, 2500,
                delegate(byte[] x) { return x[0] == 0x03 && x[1] == 0x10 && x[2] == 0x40 && x.Length > 4 && x[4] >= 1 && x[4] <= 10; });
            if (a == null) return r;
            r.Ok = true;
            r.Percent = a[4] * 10;
            return r;
        }

        // Testfolgen fuer --battery-sim: Fallen, Laden, wieder Fallen; Tastatur zwischendurch weg
        static BattReading SimQuery(BattDev d, int t)
        {
            BattReading r = new BattReading();
            r.Ok = true;
            if (d.Kind == DevKind.Headset)
            {
                int[] seq = { 80, 77, 74, 66, 58, 51, 49, 41, 33, 31, 29, 30, 29, 24, 21, 19, 15, 11, 9,
                              16, 24, 37, 52, 68, 79, 90, 97, 100, 100, 99, 96, 88, 80, 74, 70 };
                int i = Math.Min(t, seq.Length - 1);
                r.Percent = seq[i];
                r.Charging = i >= 19 && i <= 28;
            }
            else if (d.Kind == DevKind.Mouse)
            {
                int[] seq = { 100, 100, 90, 80, 70, 60, 50, 50, 40, 30, 20, 10, 10, 20, 40, 60, 80, 100, 100, 90, 80, 70 };
                r.Percent = seq[Math.Min(t, seq.Length - 1)];
            }
            else
            {
                if (t >= 6 && t < 14) r.Ok = false;          // Tastatur aus
                r.Percent = t < 14 ? 53 : 45;                // waehrend sie aus war unter 50 gefallen
            }
            return r;
        }

        // ---- HID ----
        delegate bool Match(byte[] report);

        // Oeffnet die Schnittstelle, sendet optional eine Abfrage und wartet auf die passende Antwort
        static byte[] Transfer(string part, int usagePage, byte[] cmd, int timeoutMs, Match match)
        {
            string path = FindPath(part, usagePage);
            if (path == null) return null;
            // lesen (+ schreiben), andere Programme duerfen die Schnittstelle weiter benutzen, overlapped
            using (SafeFileHandle h = CreateFile(path, cmd != null ? 0xC0000000 : 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                HIDP_CAPS c = Caps(h);
                if (c.InLen == 0) return null;
                if (cmd != null)
                {
                    if (c.OutLen < cmd.Length) return null;
                    byte[] o = new byte[c.OutLen];
                    Array.Copy(cmd, o, cmd.Length);
                    if (!Io(h, o, true, timeoutMs)) return null;
                }
                int end = Environment.TickCount + timeoutMs;
                int left;
                while ((left = end - Environment.TickCount) > 0)
                {
                    byte[] buf = new byte[c.InLen];
                    if (!Io(h, buf, false, left)) return null;
                    if (match(buf)) return buf;
                }
            }
            return null;
        }

        static HIDP_CAPS Caps(SafeFileHandle h)
        {
            HIDP_CAPS c = new HIDP_CAPS();
            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) return c;
            HidP_GetCaps(pp, out c);
            HidD_FreePreparsedData(pp);
            return c;
        }

        // Ein Lese- oder Schreibvorgang mit Timeout; bei Timeout abbrechen, damit nichts haengen bleibt
        static bool Io(SafeFileHandle h, byte[] buf, bool write, int timeoutMs)
        {
            IntPtr mem = Marshal.AllocHGlobal(buf.Length);
            IntPtr ov = Marshal.AllocHGlobal(64);
            ManualResetEvent ev = new ManualResetEvent(false);
            try
            {
                for (int i = 0; i < 64; i++) Marshal.WriteByte(ov, i, 0);
                Marshal.WriteIntPtr(ov, IntPtr.Size == 8 ? 24 : 16, ev.SafeWaitHandle.DangerousGetHandle()); // OVERLAPPED.hEvent
                if (write) Marshal.Copy(buf, 0, mem, buf.Length);
                bool ok = write ? WriteFile(h, mem, buf.Length, IntPtr.Zero, ov) : ReadFile(h, mem, buf.Length, IntPtr.Zero, ov);
                if (!ok && Marshal.GetLastWin32Error() != 997) return false; // 997 = ERROR_IO_PENDING
                int n;
                if (!ev.WaitOne(timeoutMs))
                {
                    CancelIoEx(h, ov);
                    GetOverlappedResult(h, ov, out n, true);   // warten, bis der Abbruch durch ist, erst dann Speicher freigeben
                    return false;
                }
                if (!GetOverlappedResult(h, ov, out n, false)) return false;
                if (!write) Marshal.Copy(mem, buf, 0, buf.Length);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
                Marshal.FreeHGlobal(ov);
                ev.Close();
            }
        }

        // Pfad der HID-Schnittstelle, deren Name "part" enthaelt und die die erwartete Usage-Page hat
        static string FindPath(string part, int usagePage)
        {
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 0x12); // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
            if (set == IntPtr.Zero || set == (IntPtr)(-1)) return null;
            try
            {
                SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
                d.cbSize = Marshal.SizeOf(d);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
                {
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr det = Marshal.AllocHGlobal(req);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);   // cbSize von SP_DEVICE_INTERFACE_DETAIL_DATA
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, det, req, out req, IntPtr.Zero))
                            path = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(det); }
                    if (path == null || path.ToLowerInvariant().IndexOf(part) < 0) continue;
                    // Zugriff 0 reicht, um die Usage-Page zu lesen
                    using (SafeFileHandle h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                    {
                        if (h.IsInvalid) continue;
                        if (Caps(h).UsagePage == usagePage) return path;
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }
    }

    // Einfache Symbole fuer Headset, Maus und Tastatur (Overlay und Akku-Seite)
    static class DevIcons
    {
        public static void Draw(Graphics g, DevKind k, RectangleF r, Color c, float lw)
        {
            float cx = r.X + r.Width / 2f, s = r.Width / 32f;
            using (Pen pen = new Pen(c, lw))
            using (SolidBrush b = new SolidBrush(c))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (k == DevKind.Headset)
                {
                    // Buegel + zwei Ohrmuscheln
                    g.DrawArc(pen, r.X + 5 * s, r.Y + 4 * s, 22 * s, 22 * s, 180, 180);
                    g.DrawLine(pen, r.X + 5 * s, r.Y + 15 * s, r.X + 5 * s, r.Y + 19 * s);
                    g.DrawLine(pen, r.X + 27 * s, r.Y + 15 * s, r.X + 27 * s, r.Y + 19 * s);
                    using (GraphicsPath p = Round(new RectangleF(r.X + 3 * s, r.Y + 17 * s, 7 * s, 11 * s), 2.5f * s)) g.FillPath(b, p);
                    using (GraphicsPath p = Round(new RectangleF(r.X + 22 * s, r.Y + 17 * s, 7 * s, 11 * s), 2.5f * s)) g.FillPath(b, p);
                }
                else if (k == DevKind.Mouse)
                {
                    using (GraphicsPath p = Round(new RectangleF(cx - 8 * s, r.Y + 3 * s, 16 * s, 26 * s), 8 * s)) g.DrawPath(pen, p);
                    g.DrawLine(pen, cx, r.Y + 3 * s, cx, r.Y + 12 * s);
                    g.DrawLine(pen, cx - 8 * s, r.Y + 12 * s, cx + 8 * s, r.Y + 12 * s);
                }
                else
                {
                    using (GraphicsPath p = Round(new RectangleF(r.X + 2 * s, r.Y + 8 * s, 28 * s, 17 * s), 3 * s)) g.DrawPath(pen, p);
                    for (int row = 0; row < 2; row++)
                        for (int col = 0; col < 5; col++)
                            g.FillRectangle(b, r.X + (6.5f + col * 4.5f) * s, r.Y + (12 + row * 4) * s, 2.4f * s, 2.4f * s);
                    g.DrawLine(pen, r.X + 10 * s, r.Y + 21 * s, r.X + 22 * s, r.Y + 21 * s);
                }
            }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Ampelfarbe: ueber 70 gruen, 30-70 gelb, unter 30 rot
        public static Color Level(int percent)
        {
            if (percent > 70) return Color.FromArgb(52, 211, 100);
            if (percent >= 30) return Color.FromArgb(250, 204, 21);
            return Color.FromArgb(239, 68, 68);
        }
    }

    class ProcItem
    {
        public string Exe;
        public string Title;
        public ProcItem(string exe, string title) { Exe = exe; Title = title; }
    }

    // ------------------------------------------------------------------ Artillerie-Rechner (Wardogs, Moerser L81)
    // Kartenkoordinaten: 1 Einheit = 100 m, Y waechst nach Norden
    static class Arty
    {
        public const int MinRange = 130, MaxRange = 685;   // Reichweite L81 laut Community-Guides
        static readonly Regex Num = new Regex(@"[0-9]+(?:[.,][0-9]+)?");
        static readonly string[] Dirs = { "N", "NNO", "NO", "ONO", "O", "OSO", "SO", "SSO", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };

        // Die ersten zwei Zahlen im Text, z. B. "x77.11, y75.32"; Komma direkt zwischen Ziffern = Dezimalzeichen
        public static bool Parse(string s, out double a, out double b)
        {
            a = b = 0;
            MatchCollection m = Num.Matches(s ?? "");
            if (m.Count < 2) return false;
            a = double.Parse(m[0].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            b = double.Parse(m[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            return true;
        }

        public static string Coord(double x, double y)
        {
            return "x" + x.ToString("0.00", CultureInfo.InvariantCulture) + ", y" + y.ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string Num4(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        public static double Distance(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return 100.0 * Math.Sqrt(dx * dx + dy * dy);
        }

        // Richtung in Grad, Norden = 0, im Uhrzeigersinn
        public static double Bearing(double x0, double y0, double x1, double y1)
        {
            double deg = Math.Atan2(x1 - x0, y1 - y0) * 180.0 / Math.PI;
            return deg < 0 ? deg + 360.0 : deg;
        }

        public static string Compass(double deg) { return Dirs[(int)Math.Round(deg / 22.5) % 16]; }

        public static double ToMil(double deg) { return deg * 6400.0 / 360.0; }

        // Schusstafel: nach Entfernung sortierte Paare {m, Mil}, dazwischen linear; NaN ausserhalb der Tafel
        public static double Elevation(List<int[]> t, double d)
        {
            if (t.Count == 0 || d < t[0][0] || d > t[t.Count - 1][0]) return double.NaN;
            for (int i = 1; i < t.Count; i++)
                if (d <= t[i][0])
                {
                    double f = (d - t[i - 1][0]) / (t[i][0] - t[i - 1][0]);
                    return t[i - 1][1] + f * (t[i][1] - t[i - 1][1]);
                }
            return t[0][1];   // nur ein Eintrag, genau getroffen
        }
    }

    class ArTarget
    {
        public int No;
        public double X, Y;
        public ArTarget(int no, double x, double y) { No = no; X = x; Y = y; }
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
        bool dwmRound;
        Color border = Green;
        int icon = -1;            // -1 = Logo, sonst DevKind (Akku-Meldung)

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
                dwmRound = true;
                cornerRadius = 8f;
                ApplyBorder();
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

        void ApplyBorder()
        {
            int colorref = border.R | (border.G << 8) | (border.B << 16); // COLORREF = BGR
            Native.DwmSetWindowAttribute(Handle, 34, ref colorref, 4);
        }

        // Rahmenfarbe und Symbol (Akku-Meldungen); Standard ist gruen mit Logo
        public void SetLook(Color c, int iconKind)
        {
            border = c;
            icon = iconKind;
            if (IsHandleCreated && dwmRound) ApplyBorder();
        }

        // slot 0 = oberster Platz (Klick-Meldung), jeder weitere Platz liegt eine Meldung tiefer
        public void ShowMessage(string t, string s, Screen screen, int seconds, int slot = 0)
        {
            title = t;
            holdMs = Math.Max(1, seconds) * 1000;
            sub = s;
            Rectangle b = screen.Bounds;
            int margin = (int)Math.Round(20 * scale);
            Location = new Point(b.Right - Width - margin, b.Top + margin + slot * (int)Math.Round((H + 8) * scale));
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
            // gruener Rahmen = "Programm erkannt"; bei Akku-Meldungen die Ampelfarbe
            RectangleF rf = new RectangleF(1.25f, 1.25f, W - 2.5f, H - 2.5f);
            float d = cornerRadius * 2;
            using (GraphicsPath bp = new GraphicsPath())
            using (Pen bpen = new Pen(border, 2.5f))
            {
                bp.AddArc(rf.X, rf.Y, d, d, 180, 90);
                bp.AddArc(rf.Right - d, rf.Y, d, d, 270, 90);
                bp.AddArc(rf.Right - d, rf.Bottom - d, d, d, 0, 90);
                bp.AddArc(rf.X, rf.Bottom - d, d, d, 90, 90);
                bp.CloseFigure();
                g.DrawPath(bpen, bp);
            }
            if (icon >= 0)
                DevIcons.Draw(g, (DevKind)icon, new RectangleF(16, 16, 32, 32), border, 2.2f);
            else if (logo != null)
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

        // Nur zum Testen des Designs: drei Akku-Meldungen untereinander (gruen, gelb, rot)
        public void SnapshotBattery(string path)
        {
            IntPtr h = Handle;
            int w = ClientSize.Width, hh = ClientSize.Height, gap = (int)Math.Round(8 * scale);
            string[] names = { "Corsair HS80 MAX", "ROG Strix Scope II 96", "Attack Shark X3" };
            int[] pct = { 80, 50, 20 };
            DevKind[] kinds = { DevKind.Headset, DevKind.Keyboard, DevKind.Mouse };
            using (Bitmap all = new Bitmap(w, hh * 3 + gap * 2))
            {
                using (Graphics g = Graphics.FromImage(all)) g.Clear(Color.FromArgb(40, 40, 40));
                for (int i = 0; i < 3; i++)
                {
                    SetLook(DevIcons.Level(pct[i]), (int)kinds[i]);
                    title = names[i];
                    sub = "Akku " + pct[i] + " %";
                    using (Bitmap bmp = new Bitmap(w, hh))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, w, hh));
                        using (Graphics g = Graphics.FromImage(all)) g.DrawImage(bmp, 0, i * (hh + gap));
                    }
                }
                all.Save(path, ImageFormat.Png);
            }
        }
    }

    enum Hit
    {
        None, Close, Min, Drag, Minus, Plus, Number, Slider,
        BtnLeft, BtnRight, ClkSingle, ClkDouble, HotChange, Start,
        AutoSw, FilterSw, MulSlider, MulSw, MulKey, DurSlider, ChipX, ChipAdd, PopItem, PopPanel, PopOutside,
        Gear, ThemeItem, ThemePanel, ThemeOutside,
        MouseBtn, RmSw, RmPrev, RmNext, RmMid, RmReset, RmBadge, RmRow, RmModel, RmRec,
        BattBtn, BtDropSw, BtToastSw, BtBootSw, BtPreview,
        ArtyBtn, ArPos, ArTgt, ArAdd, ArRow, ArRowX, ArTable, ArTabField, ArTabAdd, ArTabX,
        LinkDoc, LinkRepo
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
        // Links neben dem Titel; Rechtecke werden beim Zeichnen anhand der Textbreite gesetzt
        Rectangle linkDocRect = Rectangle.Empty, linkRepoRect = Rectangle.Empty;
        const string DocUrl = "https://docu.netpurple.net/docs/PurpleClicker/purple-clicker";
        const string RepoUrl = "https://github.com/codermongo/purpleclicker";
        static readonly Rectangle R_Min = new Rectangle(612, 13, 30, 30);
        static readonly Rectangle R_Close = new Rectangle(650, 13, 30, 30);

        // Eine Box fuer alles rund um den Autoclicker: links Intervall, rechts Maustaste / Klick-Art / Hotkey
        static readonly Rectangle R_CardAuto = new Rectangle(20, 64, 660, 186);
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
        static readonly Rectangle R_AutoHit = new Rectangle(300, 13, 142, 30);
        static readonly Rectangle R_AutoSw = new Rectangle(398, 16, 44, 24);
        // Artillerie-Seite: links Felder + Ergebnis, rechts Zielliste, unten Knopf fuer die Schusstafel
        static readonly Rectangle R_ArtyBtn = new Rectangle(460, 13, 30, 30);
        static readonly Rectangle R_ArCard = new Rectangle(20, 64, 660, 374);
        static readonly Rectangle R_ArPos = new Rectangle(118, 98, 302, 36);
        static readonly Rectangle R_ArTgt = new Rectangle(118, 142, 302, 36);
        const int AR_TILE_Y = 210, AR_TILE_H = 96, AR_TILE_W = 122, AR_TILE_GAP = 9;
        const int AR_DIV_X = 436, AR_LIST_X = 452, AR_LIST_W = 212;
        static readonly Rectangle R_ArAdd = new Rectangle(452, 98, 212, 30);
        const int AR_ROW_Y0 = 138, AR_ROW = 34, AR_ROW_H = 30, AR_MAX = 8;
        // Schusstafel-Popup (gleiche Flaeche wie die Programm-Auswahl)
        static readonly Rectangle R_ArTabField = new Rectangle(130, 100, 300, 34);
        static readonly Rectangle R_ArTabAdd = new Rectangle(440, 100, 130, 34);
        static readonly Rectangle R_ArTabList = new Rectangle(130, 146, 440, 186);
        const int AR_TAB_ROW = 31, AR_TAB_ROWS = 6;
        // Akku-Seite
        static readonly Rectangle R_BattBtn = new Rectangle(498, 13, 30, 30);
        static readonly Rectangle R_BtCard = new Rectangle(20, 64, 660, 374);
        const int BT_Y0 = 100, BT_ROW = 60;                                      // Geraetezeilen
        static readonly Rectangle R_BtDropSw = new Rectangle(620, 290, 44, 24);
        static readonly Rectangle R_BtToastSw = new Rectangle(620, 334, 44, 24);
        static readonly Rectangle R_BtBootSw = new Rectangle(620, 378, 44, 24);
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
        int page;                                   // 0 = Hauptseite, 1 = Maus-Umbelegung, 2 = Akku, 3 = Artillerie
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

        // Akku-Meldungen
        bool battDropMsg = true;          // Meldung, wenn eine Stufe unterschritten wird (und bei voll geladen)
        bool battToastMsg = true;         // bei 30 und 10 % zusaetzlich als Windows-Benachrichtigung
        bool battBootMsg = true;          // Stand aller Geraete, wenn Purple Clicker mit Windows startet
        readonly bool bootStart;          // per Autostart (--tray) gestartet
        bool bootPending;                 // Start-Meldung steht noch aus
        int bootDeadline;
        readonly List<OverlayForm> battOverlays = new List<OverlayForm>();   // Index + 1 = Platz unter der Klick-Meldung
        static readonly int[] Steps = { 70, 50, 30, 20, 10 };
        const int BattMsgSec = 12;        // Anzeigedauer der Akku-Meldungen, unabhaengig vom Schieber "MELDUNG"
        const int ToastSec = 6;           // Anzeigedauer der Windows-Benachrichtigung bei 30/10 %

        // Artillerie-Rechner
        readonly string[] arText = { "", "", "" };                        // Feldinhalt: 0 Position, 1 Ziel, 2 Tafel-Eingabe
        readonly double[] arX = { double.NaN, double.NaN }, arY = { double.NaN, double.NaN };
        double arOldX, arOldY;            // Wert beim Betreten des Feldes (Esc / ungueltige Eingabe)
        int arFocus = -1;                 // Feld mit Eingabe, -1 = keins
        bool arSelAll;                    // Inhalt markiert: Tippen ersetzt ihn
        string arErr = "";
        readonly List<ArTarget> arTargets = new List<ArTarget>();
        readonly List<int[]> arTable = new List<int[]>();                 // Schusstafel {m, Mil}, nach Entfernung sortiert
        bool arTabOpen;
        int arTabScroll;

        public MainForm(bool hidden, bool noServices)
        {
            this.startHidden = hidden;
            this.bootStart = hidden;
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
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Dokumentation", null, delegate { OpenUrl(DocUrl); });
            menu.Items.Add("Quellcode auf GitHub", null, delegate { OpenUrl(RepoUrl); });
            menu.Items.Add(new ToolStripSeparator());
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

            UpdateFgTimer();
            ForegroundTick();

            RegisterToastAppId();
            if (battToastMsg) AllowToastInFullscreen();

            // Akkustand im Hintergrund abfragen; beim Hochfahren oefter, bis alle Geraete da sind
            bootPending = bootStart && battBootMsg;
            bootDeadline = Environment.TickCount + 45000;
            Battery.Fast = bootPending;
            Battery.Start(delegate(BattDev d, BattReading r)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnBattery(d, r); }); } catch { }
            });
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Geraet an-/abgesteckt (WM_DEVICECHANGE) oder PC aufgewacht (PBT_APMRESUMEAUTOMATIC): Akku neu abfragen
            if (servicesStarted && (m.Msg == 0x0219 || (m.Msg == 0x0218 && (int)m.WParam == 0x12)))
                Battery.Poke(2000);
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
            Battery.Stop();
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
        }

        // ---------------------------------------------------------- Globale Hooks
        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Bewegungen (bei 1000 Hz bis zu 1000 pro Sekunde) sofort weiterreichen, nur Tasten und Rad auswerten
            if (nCode >= 0 && (int)wParam != 0x200)   // WM_MOUSEMOVE
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
            UpdateFgTimer();
            Invalidate();
        }

        void EndRecording(bool apply)
        {
            if (!recording) return;
            recording = false;
            UpdateFgTimer();
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
        // Nur noetig fuer "Nur in Programmen" und das Zeitlimit der Tasten-Aufnahme
        void UpdateFgTimer()
        {
            fgTimer.Enabled = servicesStarted && !exiting && (filterOn || recording);
        }

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

        // ---------------------------------------------------------- Akku
        // Ergebnis einer Abfrage (im UI-Thread): Zustand merken, ggf. melden
        void OnBattery(BattDev d, BattReading r)
        {
            if (exiting) return;
            d.Polled = true;
            d.Online = r.Ok;
            if (r.Ok)
            {
                int stage = BatteryStep(d, r);
                d.Last = r;
                d.Known = true;
                d.LastSeen = Environment.TickCount;
                if (stage != 0 && !bootPending)
                {
                    if (battDropMsg) ShowBattery(d, r.Percent, stage == 100 ? "Voll geladen · 100 %" : "Akku " + r.Percent + " %");
                    if (battToastMsg && (stage == 30 || stage == 10)) BatteryToast(d, r.Percent);
                }
            }
            if (bootPending) CheckBoot();
            if (Visible) Invalidate();   // Akku-Seite und Symbol in der Titelleiste
        }

        // Entscheidet, ob ein neuer Wert eine Meldung ausloest: 0 = keine, sonst die Stufe (70..10) oder 100 = voll geladen.
        // Gemeldet wird nur beim Fallen; Steigen (Laden) schaltet Stufen still wieder scharf.
        static int BatteryStep(BattDev d, BattReading r)
        {
            int p = r.Percent;
            if (!d.Known)
            {
                // erster Wert nach dem Start: alles ab dem aktuellen Stand gilt als schon gemeldet
                foreach (int s in Steps) if (p <= s) d.Announced.Add(s);
                d.FullShown = p >= 100;
                return 0;
            }
            // Stufe erst wieder scharf, wenn der Wert deutlich darueber liegt (kein Flattern um die Grenze)
            int hyst = d.Coarse ? 10 : 5;
            for (int i = d.Announced.Count - 1; i >= 0; i--)
                if (p >= d.Announced[i] + hyst) d.Announced.RemoveAt(i);
            int stage = 0;
            if (!r.Charging)
            {
                // auch wenn mehrere Stufen auf einmal unterschritten wurden: nur eine Meldung, mit der niedrigsten
                foreach (int s in Steps)
                    if (p <= s && !d.Announced.Contains(s)) { d.Announced.Add(s); stage = s; }
            }
            if (p >= 100)
            {
                if (!d.FullShown) { d.FullShown = true; stage = 100; }
            }
            else if (p < 95) d.FullShown = false;
            return stage;
        }

        // Start-Meldung: sobald alle Geraete einen Wert hatten, spaetestens nach 45 s
        void CheckBoot()
        {
            bool all = true;
            foreach (BattDev d in Battery.Devices) if (!d.Known) all = false;
            if (!all && Environment.TickCount - bootDeadline < 0) return;
            bootPending = false;
            Battery.Fast = false;
            foreach (BattDev d in Battery.Devices)
                if (d.Online) ShowBattery(d, d.Last.Percent, "Akku " + d.Last.Percent + " %" + (d.Last.Charging ? " · lädt" : ""));
        }

        // Akku-Meldung oben rechts, unter der Klick-Meldung; mehrere stehen untereinander
        void ShowBattery(BattDev d, int percent, string sub)
        {
            try
            {
                // sichtbare Meldung desselben Geraets aktualisieren, sonst ersten freien Platz nehmen
                int slot = -1;
                for (int i = 0; i < battOverlays.Count; i++)
                    if (battOverlays[i].Visible && battOverlays[i].Tag == d) { slot = i; break; }
                if (slot < 0)
                    for (int i = 0; i < battOverlays.Count; i++)
                        if (battOverlays[i].IsDisposed || !battOverlays[i].Visible) { slot = i; break; }
                if (slot < 0) { battOverlays.Add(null); slot = battOverlays.Count - 1; }
                OverlayForm o = battOverlays[slot];
                if (o == null || o.IsDisposed) { o = new OverlayForm(logo, scale); battOverlays[slot] = o; }
                o.Tag = d;
                o.SetLook(DevIcons.Level(percent), (int)d.Kind);
                IntPtr fg = Native.GetForegroundWindow();
                o.ShowMessage(d.Name, sub, fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen, BattMsgSec, slot + 1);
            }
            catch { }
        }

        // Vorschau auf der Akku-Seite: je eine Meldung in Gruen, Gelb und Rot
        void ShowBatteryPreview()
        {
            int[] pct = { 80, 50, 20 };
            for (int i = 0; i < Battery.Devices.Length; i++)
                ShowBattery(Battery.Devices[i], pct[i], "Vorschau · Akku " + pct[i] + " %");
        }

        // Windows-Benachrichtigung bei 30 und 10 %: als "wichtig" (kommt mit Erlaubnis auch bei "Nicht stoeren" durch),
        // sonst ueber das Tray-Symbol
        void BatteryToast(BattDev d, int percent)
        {
            string kind = d.Kind == DevKind.Headset ? "Headset" : d.Kind == DevKind.Mouse ? "Maus" : "Tastatur";
            string title = kind + ": Akku " + percent + " %";
            string text = d.Name + " hat noch " + percent + " % Akku.";
            if (ShowUrgentToast(title, text) || notify == null) return;
            try { notify.ShowBalloonTip(10000, title, text, percent <= 10 ? ToolTipIcon.Warning : ToolTipIcon.Info); }
            catch { }
        }

        const string ToastAppId = "netpurple.PurpleClicker";

        // Eigene App-Kennung fuer moderne Windows-Benachrichtigungen (nur Benutzer-Registry, keine Verknuepfung noetig)
        void RegisterToastAppId()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PurpleClicker");
                string png = Path.Combine(dir, "toast.png");
                if (!File.Exists(png) && logo != null)
                {
                    Directory.CreateDirectory(dir);
                    logo.Save(png, ImageFormat.Png);
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + ToastAppId))
                {
                    k.SetValue("DisplayName", "Purple Clicker");
                    if (File.Exists(png)) k.SetValue("IconUri", png);
                }
            }
            catch { }
        }

        // "Wichtige Benachrichtigungen" fuer Purple Clicker erlauben, damit sie auch bei "Nicht stoeren" (Vollbild)
        // durchkommen; derselbe Wert, den der Schalter in den Windows-Einstellungen setzt (nur Benutzer-Registry)
        static void AllowToastInFullscreen()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + ToastAppId))
                    k.SetValue("AllowUrgentNotifications", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        // Moderne Windows-Benachrichtigung als "Wecker" (scenario="alarm"): nur diese Art zeigt im Vollbild bei
        // "Nicht stoeren" den vollen Inhalt (wichtige/dringende werden dort zu "View notification" verkuerzt).
        // Der Wecker-Typ erzwingt sein Klingeln, deshalb stumm senden und selbst den normalen Windows-Benachrichtigungston
        // (Klangschema "Benachrichtigung") spielen. Ein Wecker bliebe bis zum Klick stehen, deshalb nach ToastSec selbst wegraeumen.
        // WinRT wird zur Laufzeit geladen (keine Build-Aenderung).
        static bool ShowUrgentToast(string title, string text)
        {
            try
            {
                Type mgrT = Type.GetType("Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime");
                Type toastT = Type.GetType("Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType=WindowsRuntime");
                Type notifierT = Type.GetType("Windows.UI.Notifications.ToastNotifier, Windows.UI.Notifications, ContentType=WindowsRuntime");
                Type xmlT = Type.GetType("Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType=WindowsRuntime");
                if (mgrT == null || toastT == null || notifierT == null || xmlT == null) return false;
                object xml = Activator.CreateInstance(xmlT);
                xmlT.GetMethod("LoadXml", new[] { typeof(string) }).Invoke(xml, new object[] {
                    "<toast scenario=\"alarm\"><visual><binding template=\"ToastGeneric\"><text>" +
                    System.Security.SecurityElement.Escape(title) + "</text><text>" +
                    System.Security.SecurityElement.Escape(text) + "</text></binding></visual>" +
                    "<actions><action content=\"OK\" arguments=\"dismiss\" activationType=\"system\"/></actions>" +
                    "<audio silent=\"true\"/></toast>" });
                object toast = Activator.CreateInstance(toastT, xml);
                System.Reflection.PropertyInfo prio = toastT.GetProperty("Priority");
                if (prio != null) prio.SetValue(toast, Enum.ToObject(prio.PropertyType, 1), null);   // ToastNotificationPriority.High
                object notifier = mgrT.GetMethod("CreateToastNotifier", new[] { typeof(string) }).Invoke(null, new object[] { ToastAppId });
                notifierT.GetMethod("Show").Invoke(notifier, new[] { toast });
                Native.PlaySound("Notification.Default", IntPtr.Zero, 0x10000 | 0x0002 | 0x0001);   // SND_ALIAS | SND_NODEFAULT | SND_ASYNC
                // im selben (UI-)Thread wegraeumen, dem die Benachrichtigung gehoert; je Benachrichtigung ein Timer
                System.Reflection.MethodInfo hide = notifierT.GetMethod("Hide");
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = ToastSec * 1000;
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    try { hide.Invoke(notifier, new[] { toast }); } catch { }
                };
                t.Start();
                return true;
            }
            catch { return false; }
        }

        // Text unter dem Geraetenamen auf der Akku-Seite
        static string BattStatus(BattDev d)
        {
            string kind = d.Kind == DevKind.Headset ? "Headset" : d.Kind == DevKind.Mouse ? "Maus" : "Tastatur";
            if (!d.Polled) return kind + " · wird abgefragt …";
            if (d.Online)
            {
                if (d.Last.Charging) return kind + " · lädt";
                return kind + (d.Coarse ? " · meldet 10-%-Schritte" : " · verbunden");
            }
            if (!d.Known) return kind + " · nicht verbunden";
            int min = Math.Max(0, (Environment.TickCount - d.LastSeen) / 60000);
            return kind + " · nicht erreichbar, zuletzt vor " + (min < 1 ? "< 1" : min.ToString()) + " min";
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

        // ---------------------------------------------------------- Artillerie-Rechner
        static Hit ArFieldHit(int i)
        {
            return i == 0 ? Hit.ArPos : i == 1 ? Hit.ArTgt : Hit.ArTabField;
        }

        string ArShow(int i)
        {
            return double.IsNaN(arX[i]) ? "" : Arty.Coord(arX[i], arY[i]);
        }

        void ArFocus(int i)
        {
            if (arFocus == i) { arSelAll = false; return; }   // zweiter Klick: Markierung aufheben, weiterschreiben
            ArBlur();
            arFocus = i;
            arSelAll = arText[i].Length > 0;
            arErr = "";
            if (i < 2) { arOldX = arX[i]; arOldY = arY[i]; }
            Focus();
        }

        // Feld verlassen: Koordinate uebernehmen, leer = keine, ungueltig = alter Wert bleibt
        void ArBlur()
        {
            int i = arFocus;
            if (i < 0) return;
            arFocus = -1;
            arSelAll = false;
            if (i == 2) return;
            double x, y;
            if (arText[i].Trim().Length == 0) { arX[i] = double.NaN; arY[i] = double.NaN; }
            else if (Arty.Parse(arText[i], out x, out y)) { arX[i] = x; arY[i] = y; }
            else
            {
                arX[i] = arOldX;
                arY[i] = arOldY;
                arErr = "Keine Koordinate erkannt, z. B. x77.11, y75.32";
            }
            arText[i] = ArShow(i);
            SaveSettings();
        }

        void ArCancel()
        {
            int i = arFocus;
            arFocus = -1;
            arSelAll = false;
            arErr = "";
            if (i < 0 || i == 2) return;
            arX[i] = arOldX;
            arY[i] = arOldY;
            arText[i] = ArShow(i);
        }

        // Waehrend des Tippens rechnen, sobald zwei Zahlen dastehen
        void ArLive()
        {
            if (arFocus < 0 || arFocus == 2) return;
            double x, y;
            if (Arty.Parse(arText[arFocus], out x, out y)) { arX[arFocus] = x; arY[arFocus] = y; }
        }

        // Strg+V: ersetzt den Inhalt und uebernimmt die Koordinate sofort
        void ArPaste(int i, string clip)
        {
            clip = (clip ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (clip.Length > 40) clip = clip.Substring(0, 40);
            if (i == 2)
            {
                if (arFocus != 2) ArFocus(2);
                if (arSelAll) arText[2] = "";
                arSelAll = false;
                arText[2] = (arText[2] + clip).Length > 40 ? arText[2] : arText[2] + clip;
                return;
            }
            double x, y;
            if (!Arty.Parse(clip, out x, out y))
            {
                arErr = "Zwischenablage enthält keine Koordinate";
                return;
            }
            if (arFocus != i) ArBlur();
            arFocus = -1;
            arSelAll = false;
            arX[i] = x;
            arY[i] = y;
            arText[i] = ArShow(i);
            arErr = "";
            SaveSettings();
        }

        bool ArKey(Keys k, Keys mod)
        {
            if (k == Keys.V && mod == Keys.Control)
            {
                string clip = "";
                try { if (Clipboard.ContainsText()) clip = Clipboard.GetText(); } catch { }
                ArPaste(arFocus >= 0 ? arFocus : (arTabOpen ? 2 : 1), clip);   // ohne Feld: ins Ziel
                return true;
            }
            if (arFocus >= 0)
            {
                if (k == Keys.Back)
                {
                    string s = arText[arFocus];
                    arText[arFocus] = arSelAll ? "" : s.Length > 0 ? s.Substring(0, s.Length - 1) : s;
                    arSelAll = false;
                    arErr = "";
                    ArLive();
                    return true;
                }
                if (k == Keys.Enter) { if (arFocus == 2) ArAddTableEntry(); else ArBlur(); return true; }
                if (k == Keys.Escape)
                {
                    if (arFocus == 2) arTabOpen = false;   // in der Tafel schliesst Esc das Popup direkt
                    ArCancel();
                    return true;
                }
                if (k == Keys.Tab && arFocus < 2) { ArFocus(1 - arFocus); return true; }
                return false;   // Zeichen kommen ueber OnKeyPress
            }
            if (k == Keys.Tab && !arTabOpen) { ArFocus(0); return true; }
            if (k == Keys.Escape)
            {
                if (arTabOpen) arTabOpen = false; else page = 0;
                arErr = "";
                return true;
            }
            return false;
        }

        void ArAddTarget()
        {
            if (double.IsNaN(arX[1]) || arTargets.Count >= AR_MAX) return;
            int no = 0;
            foreach (ArTarget t in arTargets)
            {
                if (t.X == arX[1] && t.Y == arY[1]) return;   // schon in der Liste
                no = Math.Max(no, t.No);
            }
            arTargets.Add(new ArTarget(no + 1, arX[1], arY[1]));
            SaveSettings();
        }

        // Tafel oeffnen; aktuelle Entfernung steht schon im Feld, es fehlt nur noch der Mil-Wert
        void ArOpenTable()
        {
            arTabOpen = true;
            arErr = "";
            ArFocus(2);
            arSelAll = false;
            double d;
            arText[2] = ArDistance(out d) ? Math.Round(d) + " " : "";
        }

        void ArAddTableEntry()
        {
            double a, b;
            if (!Arty.Parse(arText[2], out a, out b))
            {
                arErr = "Entfernung und Mil mit Leerzeichen, z. B. 450 1250";
                return;
            }
            int d = (int)Math.Round(a), m = (int)Math.Round(b);
            if (d < 1 || d > 20000 || m > 6400)
            {
                arErr = "Werte außerhalb des sinnvollen Bereichs";
                return;
            }
            arTable.RemoveAll(delegate(int[] e) { return e[0] == d; });   // gleiche Entfernung: ueberschreiben
            arTable.Add(new int[] { d, m });
            arTable.Sort(delegate(int[] p, int[] q) { return p[0].CompareTo(q[0]); });
            arText[2] = "";
            arErr = "";
            arFocus = 2;
            arSelAll = false;
            SaveSettings();
        }

        bool ArDistance(out double d)
        {
            d = 0;
            if (double.IsNaN(arX[0]) || double.IsNaN(arX[1])) return false;
            d = Arty.Distance(arX[0], arY[0], arX[1], arY[1]);
            return true;
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
            if (R_BattBtn.Contains(p)) return Hit.BattBtn;
            if (R_ArtyBtn.Contains(p)) return Hit.ArtyBtn;
            if (linkDocRect.Contains(p)) return Hit.LinkDoc;
            if (linkRepoRect.Contains(p)) return Hit.LinkRepo;
            if (R_Title.Contains(p)) return Hit.Drag;
            if (page == 1) return HitRemap(p);
            if (page == 3) return HitArty(p);
            if (page == 2)
            {
                if (R_BtDropSw.Contains(p)) return Hit.BtDropSw;
                if (R_BtToastSw.Contains(p)) return Hit.BtToastSw;
                if (R_BtBootSw.Contains(p)) return Hit.BtBootSw;
                if (R_Start.Contains(p)) return Hit.BtPreview;
                return Hit.None;
            }

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

        Hit HitArty(Point p)
        {
            if (arTabOpen)
            {
                if (R_ArTabField.Contains(p)) return Hit.ArTabField;
                if (R_ArTabAdd.Contains(p)) return Hit.ArTabAdd;
                if (R_ArTabList.Contains(p))
                {
                    int i = arTabScroll + (p.Y - R_ArTabList.Y) / AR_TAB_ROW;
                    if (i < arTable.Count && p.X >= R_ArTabList.Right - 34) { hitIndex = i; return Hit.ArTabX; }
                    return Hit.PopPanel;
                }
                return R_Pop.Contains(p) ? Hit.PopPanel : Hit.PopOutside;
            }
            if (R_ArPos.Contains(p)) return Hit.ArPos;
            if (R_ArTgt.Contains(p)) return Hit.ArTgt;
            if (R_ArAdd.Contains(p)) return Hit.ArAdd;
            if (R_Start.Contains(p)) return Hit.ArTable;
            for (int i = 0; i < arTargets.Count; i++)
            {
                Rectangle r = new Rectangle(AR_LIST_X, AR_ROW_Y0 + i * AR_ROW, AR_LIST_W, AR_ROW_H);
                if (!r.Contains(p)) continue;
                hitIndex = i;
                return p.X >= r.Right - 28 ? Hit.ArRowX : Hit.ArRow;
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
            if (arFocus >= 0 && h != ArFieldHit(arFocus) && !(arFocus == 2 && h == Hit.ArTabAdd)) ArBlur();

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
                case Hit.LinkDoc: OpenUrl(DocUrl); break;
                case Hit.LinkRepo: OpenUrl(RepoUrl); break;
                case Hit.MouseBtn:
                    EndRecording(false);
                    page = page == 1 ? 0 : 1;
                    if (page == 1) MouseDetect.Refresh(); // Maus evtl. inzwischen gewechselt
                    popupOpen = false;
                    themeOpen = false;
                    break;
                case Hit.BattBtn:
                    EndRecording(false);
                    page = page == 2 ? 0 : 2;
                    if (page == 2 && servicesStarted) Battery.Poke(0);   // frische Werte holen
                    popupOpen = false;
                    themeOpen = false;
                    break;
                case Hit.ArtyBtn:
                    EndRecording(false);
                    page = page == 3 ? 0 : 3;
                    popupOpen = false;
                    themeOpen = false;
                    arTabOpen = false;
                    break;
                case Hit.ArPos: ArFocus(0); break;
                case Hit.ArTgt: ArFocus(1); break;
                case Hit.ArTabField: ArFocus(2); break;
                case Hit.ArAdd: ArAddTarget(); break;
                case Hit.ArRow:
                    if (idx >= 0 && idx < arTargets.Count)
                    {
                        arX[1] = arTargets[idx].X;
                        arY[1] = arTargets[idx].Y;
                        arText[1] = ArShow(1);
                        arErr = "";
                        SaveSettings();
                    }
                    break;
                case Hit.ArRowX:
                    if (idx >= 0 && idx < arTargets.Count) { arTargets.RemoveAt(idx); SaveSettings(); }
                    break;
                case Hit.ArTable: ArOpenTable(); break;
                case Hit.ArTabAdd: ArAddTableEntry(); break;
                case Hit.ArTabX:
                    if (idx >= 0 && idx < arTable.Count)
                    {
                        arTable.RemoveAt(idx);
                        arTabScroll = Math.Min(arTabScroll, Math.Max(0, arTable.Count - AR_TAB_ROWS));
                        SaveSettings();
                    }
                    break;
                case Hit.BtDropSw: battDropMsg = !battDropMsg; SaveSettings(); break;
                case Hit.BtToastSw:
                    battToastMsg = !battToastMsg;
                    if (battToastMsg) AllowToastInFullscreen();
                    SaveSettings();
                    break;
                case Hit.BtBootSw: battBootMsg = !battBootMsg; SaveSettings(); break;
                case Hit.BtPreview: ShowBatteryPreview(); break;
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
                    UpdateFgTimer();
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
                        UpdateFgTimer();
                        ForegroundTick();
                    }
                    break;
                case Hit.PopOutside: popupOpen = false; arTabOpen = false; break;
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
                bool text = h == Hit.ArPos || h == Hit.ArTgt || h == Hit.ArTabField;
                Cursor = text ? Cursors.IBeam : hand ? Cursors.Hand : Cursors.Default;
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
            if (page == 3)
            {
                if (arTabOpen)
                {
                    int max = Math.Max(0, arTable.Count - AR_TAB_ROWS);
                    arTabScroll = Math.Min(max, Math.Max(0, arTabScroll + (e.Delta > 0 ? -1 : 1)));
                    Invalidate();
                }
                return;
            }
            if (popupOpen)
            {
                int max = Math.Max(0, popItems.Count - POP_ROWS);
                popScroll = Math.Min(max, Math.Max(0, popScroll + (e.Delta > 0 ? -1 : 1)));
                Invalidate();
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            EndRecording(false);   // sonst wuerde die Aufnahme global Tasten schlucken
        }

        // Tab soll auf der Artillerie-Seite in OnKeyDown ankommen (Wechsel Position <-> Ziel)
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (page == 3 && (keyData & Keys.KeyCode) == Keys.Tab) return false;
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (page != 3 || themeOpen || arFocus < 0) return;
            char c = e.KeyChar;
            bool ok = (c >= '0' && c <= '9') || c == '.' || c == ',' || c == ' ' || (arFocus < 2 && "xXyY:;/".IndexOf(c) >= 0);
            if (!ok) return;
            if (arSelAll) { arText[arFocus] = ""; arSelAll = false; }
            if (arText[arFocus].Length < 40) arText[arFocus] += c;
            arErr = "";
            ArLive();
            Invalidate();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (page == 3 && !themeOpen && ArKey(e.KeyCode, e.Modifiers))
            {
                Invalidate();
                e.Handled = true;
                return;
            }
            if ((page == 1 || page == 2) && !themeOpen && e.KeyCode == Keys.Escape)
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
                g.FillEllipse(b, titleX + titleW + 12, 24, 8, 8);   // Punkt zeigt den Zustand: laeuft / bereit / wartet auf Programm
            linkDocRect = DrawTitleIcon(g, 0, (int)(titleX + titleW + 30), hover == Hit.LinkDoc);
            linkRepoRect = DrawTitleIcon(g, 1, linkDocRect.Right + 8, hover == Hit.LinkRepo);   // 8 px Abstand wie bei den Knoepfen rechts

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
            DrawBattIcon(g, hover == Hit.BattBtn);
            DrawArtyIcon(g, hover == Hit.ArtyBtn);
            if (page != 0)
            {
                if (page == 1) DrawRemapPage(g);
                else if (page == 2) DrawBatteryPage(g);
                else
                {
                    DrawArtyPage(g);
                    if (arTabOpen) DrawArtyTable(g);
                }
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

        // Akku-Symbol in der Titelleiste (oeffnet die Akku-Seite); Fuellstand = schwaechstes verbundenes Geraet
        void DrawBattIcon(Graphics g, bool hot)
        {
            BtnShape(g, hot || page == 2 ? C_BtnHover : C_Btn, R_BattBtn);
            float cx = R_BattBtn.X + R_BattBtn.Width / 2f, cy = R_BattBtn.Y + R_BattBtn.Height / 2f;
            Color c = page == 2 ? C_Accent : C_Text;
            int low = -1;
            foreach (BattDev d in Battery.Devices)
                if (d.Online && (low < 0 || d.Last.Percent < low)) low = d.Last.Percent;
            RectangleF body = new RectangleF(cx - 9f, cy - 5f, 16f, 10f);
            using (GraphicsPath p = RR(body, 2f))
            using (Pen pen = new Pen(c, 1.5f))
                g.DrawPath(pen, p);
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillRectangle(b, body.Right + 1f, cy - 2.5f, 2f, 5f);
                if (low > 0) g.FillRectangle(b, body.X + 2.5f, body.Y + 2.5f, (body.Width - 5f) * low / 100f, body.Height - 5f);
            }
        }

        // Fadenkreuz in der Titelleiste (oeffnet den Artillerie-Rechner)
        void DrawArtyIcon(Graphics g, bool hot)
        {
            BtnShape(g, hot || page == 3 ? C_BtnHover : C_Btn, R_ArtyBtn);
            float cx = R_ArtyBtn.X + R_ArtyBtn.Width / 2f, cy = R_ArtyBtn.Y + R_ArtyBtn.Height / 2f;
            Color c = page == 3 ? C_Accent : C_Text;
            using (Pen pen = new Pen(c, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, cx - 6f, cy - 6f, 12f, 12f);
                g.DrawLine(pen, cx, cy - 9.5f, cx, cy - 3.5f);
                g.DrawLine(pen, cx, cy + 3.5f, cx, cy + 9.5f);
                g.DrawLine(pen, cx - 9.5f, cy, cx - 3.5f, cy);
                g.DrawLine(pen, cx + 3.5f, cy, cx + 9.5f, cy);
            }
            using (SolidBrush b = new SolidBrush(c))
                g.FillEllipse(b, cx - 1.3f, cy - 1.3f, 2.6f, 2.6f);
        }

        // Eingabefeld des Rechners: markierter Inhalt, Schreibmarke, Platzhalter
        void ArField(Graphics g, int i, Rectangle r, string label, string placeholder)
        {
            if (label != null)
                DrawText(g, label, fLabel, C_Dim, new Rectangle(36, r.Y, r.X - 40, r.Height), StringAlignment.Near);
            bool focus = arFocus == i;
            float rad = RadCtl(r.Height);
            FillRR(g, C_Field, r, rad);
            StrokeRR(g, focus ? C_Accent : hover == ArFieldHit(i) ? C_Dim : C_CardLine, r, rad, focus ? 1.5f : 1f);
            string s = arText[i];
            Rectangle inner = new Rectangle(r.X + 14, r.Y, r.Width - 28, r.Height);
            if (s.Length == 0 && !focus)
            {
                DrawText(g, placeholder, fChip, C_Dim, inner, StringAlignment.Near);
                return;
            }
            using (StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (SolidBrush b = new SolidBrush(C_Text))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                float th = g.MeasureString("x0", fTitle, 2000, sf).Height;
                float tw = s.Length > 0 ? g.MeasureString(s, fTitle, 2000, sf).Width : 0f;
                float ty = r.Y + (r.Height - th) / 2f;
                g.SetClip(inner);
                if (focus && arSelAll && tw > 0)
                    using (SolidBrush sel = new SolidBrush(A(90, C_Accent)))
                        g.FillRectangle(sel, inner.X - 2, ty - 1, tw + 4, th + 2);
                g.DrawString(s, fTitle, b, inner.X, ty, sf);
                if (focus && !arSelAll)
                    using (Pen caret = new Pen(C_Accent, 1.5f))
                        g.DrawLine(caret, inner.X + tw + 1.5f, ty + 1, inner.X + tw + 1.5f, ty + th - 1);
                g.ResetClip();
            }
        }

        // Ergebnis-Kachel: Label, grosse Zahl mit Einheit, Zusatzzeile
        void ArTile(Graphics g, int n, string label, string value, string unit, string sub, Color valueColor, Color subColor)
        {
            Rectangle r = new Rectangle(36 + n * (AR_TILE_W + AR_TILE_GAP), AR_TILE_Y, AR_TILE_W, AR_TILE_H);
            float rad = Math.Min(RadCtl(r.Height), 12f);
            FillRR(g, C_Field, r, rad);
            StrokeRR(g, C_CardLine, r, rad, 1f);
            DrawText(g, label, fLabel, C_Dim, new Rectangle(r.X + 12, r.Y + 8, r.Width - 16, 18), StringAlignment.Near);
            Font f = fBig;
            SizeF vs = g.MeasureString(value, f, 2000, StringFormat.GenericTypographic);
            SizeF us = g.MeasureString(unit, fUnit, 2000, StringFormat.GenericTypographic);
            if (vs.Width + 4 + us.Width > r.Width - 22)
            {
                f = fTitle;   // sehr lange Zahl: kleinere Schrift statt ueber den Rand
                vs = g.MeasureString(value, f, 2000, StringFormat.GenericTypographic);
            }
            float bigH = g.MeasureString("0", fBig, 2000, StringFormat.GenericTypographic).Height;
            float vx = r.X + 12, vy = r.Y + 28 + (bigH - vs.Height) / 2f;
            using (SolidBrush b = new SolidBrush(valueColor))
                g.DrawString(value, f, b, vx, vy, StringFormat.GenericTypographic);
            if (unit.Length > 0)
                using (SolidBrush b = new SolidBrush(C_Dim))
                    g.DrawString(unit, fUnit, b, vx + vs.Width + 4, vy + vs.Height - us.Height - 4, StringFormat.GenericTypographic);
            DrawText(g, sub, fChip, subColor, new Rectangle(r.X + 12, r.Bottom - 26, r.Width - 16, 18), StringAlignment.Near);
        }

        // Artillerie-Seite: Position/Ziel einfuegen, Entfernung, Richtung, Erhoehung aus der eigenen Schusstafel
        void DrawArtyPage(Graphics g)
        {
            Card(g, R_ArCard, "ARTILLERIE · MÖRSER L81");
            ArField(g, 0, R_ArPos, "POSITION", "Strg+V oder tippen, z. B. x77.11, y75.32");
            ArField(g, 1, R_ArTgt, "ZIEL", "Strg+V oder tippen");
            if (arErr.Length > 0 && !arTabOpen)
                DrawText(g, arErr, fChip, DevIcons.Level(0), new Rectangle(118, 182, 302, 20), StringAlignment.Near);
            else
                DrawText(g, "Feld anklicken, Strg+V  ·  Tab wechselt", fChip, C_Dim, new Rectangle(118, 182, 302, 20), StringAlignment.Near);

            Color red = DevIcons.Level(0), green = DevIcons.Level(100);
            double dist;
            bool ok = ArDistance(out dist);
            bool inRange = ok && dist >= Arty.MinRange && dist <= Arty.MaxRange;
            if (ok)
            {
                double deg = Arty.Bearing(arX[0], arY[0], arX[1], arY[1]);
                double elev = Arty.Elevation(arTable, dist);
                string dx = (arX[1] - arX[0]).ToString("+0.00;−0.00;0.00", CultureInfo.InvariantCulture);
                string dy = (arY[1] - arY[0]).ToString("+0.00;−0.00;0.00", CultureInfo.InvariantCulture);
                ArTile(g, 0, "ENTFERNUNG", Math.Round(dist).ToString(CultureInfo.InvariantCulture), "m", "Δ " + dx + " / " + dy,
                    inRange ? C_Text : red, C_Dim);
                ArTile(g, 1, "RICHTUNG", deg.ToString("0.0", CultureInfo.InvariantCulture), "°",
                    Arty.Compass(deg) + " · " + Math.Round(Arty.ToMil(deg)) % 6400 + " mil", C_Text, C_Dim);
                string esub = arTable.Count == 0 ? "Schusstafel leer" : double.IsNaN(elev) ? "außerhalb der Tafel" : "aus Schusstafel";
                ArTile(g, 2, "ERHÖHUNG", double.IsNaN(elev) ? "–" : Math.Round(elev).ToString(CultureInfo.InvariantCulture), "mil", esub,
                    double.IsNaN(elev) ? C_Dim : C_Accent, C_Dim);
            }
            else
            {
                ArTile(g, 0, "ENTFERNUNG", "–", "m", "", C_Dim, C_Dim);
                ArTile(g, 1, "RICHTUNG", "–", "°", "", C_Dim, C_Dim);
                ArTile(g, 2, "ERHÖHUNG", "–", "mil", "", C_Dim, C_Dim);
            }

            // Reichweite
            string rangeText = !ok ? "Position und Ziel eingeben"
                : inRange ? "In Reichweite (L81 ca. " + Arty.MinRange + "–" + Arty.MaxRange + " m)"
                : (dist > Arty.MaxRange ? "Zu weit" : "Zu nah") + " für den L81 (ca. " + Arty.MinRange + "–" + Arty.MaxRange + " m)";
            if (ok)
                using (SolidBrush b = new SolidBrush(inRange ? green : red))
                    g.FillEllipse(b, 37, 322, 8, 8);
            DrawText(g, rangeText, fBtn, ok ? (inRange ? C_Text : red) : C_Dim, new Rectangle(ok ? 52 : 36, 316, 370, 20), StringAlignment.Near);

            // Stand der Schusstafel
            string tab = arTable.Count == 0 ? "Schusstafel: noch leer"
                : "Schusstafel: " + arTable.Count + (arTable.Count == 1 ? " Eintrag" : " Einträge") + ", "
                  + arTable[0][0] + "–" + arTable[arTable.Count - 1][0] + " m";
            DrawText(g, tab, fChip, C_Text, new Rectangle(36, 356, 384, 18), StringAlignment.Near);
            DrawText(g, "Nach Probeschüssen eintragen, dazwischen wird gerechnet", fChip, C_Dim,
                new Rectangle(36, 376, 384, 18), StringAlignment.Near);

            // Zielliste rechts
            using (Pen sep = new Pen(C_CardLine, 1f))
                g.DrawLine(sep, AR_DIV_X, R_ArCard.Y + 18, AR_DIV_X, R_ArCard.Bottom - 18);
            DrawText(g, "ZIELE", fLabel, C_Dim, new Rectangle(AR_LIST_X, R_ArCard.Y + 8, 100, 20), StringAlignment.Near);
            DrawText(g, arTargets.Count + " / " + AR_MAX, fChip, C_Dim, new Rectangle(AR_LIST_X, R_ArCard.Y + 8, AR_LIST_W, 20), StringAlignment.Far);
            bool full = arTargets.Count >= AR_MAX;
            bool canAdd = !double.IsNaN(arX[1]) && !full;
            bool addHot = hover == Hit.ArAdd && canAdd;
            FillRR(g, addHot ? A(36, C_Accent) : C_Field, R_ArAdd, RadCtl(R_ArAdd.Height));
            StrokeRR(g, canAdd ? C_Accent : C_CardLine, R_ArAdd, RadCtl(R_ArAdd.Height), 1f);
            DrawText(g, full ? "Liste voll" : "+  Ziel speichern", fChip, canAdd ? C_Accent : C_Dim, R_ArAdd, StringAlignment.Center);
            if (arTargets.Count == 0)
                DrawText(g, "Noch keine Ziele gespeichert", fChip, C_Dim,
                    new Rectangle(AR_LIST_X, AR_ROW_Y0, AR_LIST_W, AR_ROW_H), StringAlignment.Center);
            for (int i = 0; i < arTargets.Count; i++)
            {
                ArTarget t = arTargets[i];
                Rectangle row = new Rectangle(AR_LIST_X, AR_ROW_Y0 + i * AR_ROW, AR_LIST_W, AR_ROW_H);
                bool cur = t.X == arX[1] && t.Y == arY[1];
                bool hot = (hover == Hit.ArRow || hover == Hit.ArRowX) && hoverIdx == i;
                float rr = Math.Min(RadCtl(row.Height), 10f);
                FillRR(g, cur ? A(40, C_Accent) : hot ? C_BtnHover : C_Btn, row, rr);
                if (cur) StrokeRR(g, A(120, C_Accent), row, rr, 1f);
                DrawText(g, "Ziel " + t.No, fBtn, cur ? C_Accent : C_Text, new Rectangle(row.X + 12, row.Y, 58, row.Height), StringAlignment.Near);
                string info = double.IsNaN(arX[0]) ? Arty.Coord(t.X, t.Y)
                    : Math.Round(Arty.Distance(arX[0], arY[0], t.X, t.Y)) + " m · "
                      + Math.Round(Arty.Bearing(arX[0], arY[0], t.X, t.Y)) % 360 + "°";
                DrawText(g, info, fChip, C_Dim, new Rectangle(row.X + 70, row.Y, row.Width - 100, row.Height), StringAlignment.Near);
                bool xh = hover == Hit.ArRowX && hoverIdx == i;
                Rectangle xr = new Rectangle(row.Right - 24, row.Y + 6, 18, 18);
                if (xh) using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 60, 60))) g.FillEllipse(b, xr);
                using (Pen pen = new Pen(xh ? Color.White : C_Dim, 1.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, xr.X + 6, xr.Y + 6, xr.X + 12, xr.Y + 12);
                    g.DrawLine(pen, xr.X + 12, xr.Y + 6, xr.X + 6, xr.Y + 12);
                }
            }

            FillRR(g, hover == Hit.ArTable ? C_BtnHover : C_Btn, R_Start, radStart);
            StrokeRR(g, C_CardLine, R_Start, radStart, 1f);
            DrawText(g, "Schusstafel bearbeiten", fBtn, C_Text, R_Start, StringAlignment.Center);
        }

        // Schusstafel-Popup: Eingabe "Entfernung Mil", Liste mit Loeschen
        void DrawArtyTable(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(b, 0, 56, W, H - 56);
            FillRR(g, A(255, C_Card), R_Pop, Math.Min(radCard, 16f));
            StrokeRR(g, C_Accent, R_Pop, Math.Min(radCard, 16f), 1f);
            DrawText(g, "SCHUSSTAFEL L81  ·  ENTFERNUNG → MIL", fLabel, C_Dim, new Rectangle(R_Pop.X + 20, R_Pop.Y + 10, 400, 28), StringAlignment.Near);

            ArField(g, 2, R_ArTabField, null, "z. B. 450 1250");
            bool addHot = hover == Hit.ArTabAdd;
            FillRR(g, addHot ? A(36, C_Accent) : C_Field, R_ArTabAdd, RadCtl(R_ArTabAdd.Height));
            StrokeRR(g, C_Accent, R_ArTabAdd, RadCtl(R_ArTabAdd.Height), 1f);
            DrawText(g, "Eintragen", fBtn, C_Accent, R_ArTabAdd, StringAlignment.Center);

            // Eintraege, zwischen denen die aktuelle Entfernung liegt, hervorheben
            double dist;
            int lo = -1, hi = -1;
            if (ArDistance(out dist))
                for (int i = 0; i < arTable.Count; i++)
                {
                    if (arTable[i][0] <= dist) lo = i;
                    if (arTable[i][0] >= dist && hi < 0) hi = i;
                }
            if (lo < 0 || hi < 0) lo = hi = -1;

            if (arTable.Count == 0)
                DrawText(g, "Noch keine Einträge – nach einem Probeschuss Entfernung und Mil eintragen", fChip, C_Dim, R_ArTabList, StringAlignment.Center);
            for (int row = 0; row < AR_TAB_ROWS; row++)
            {
                int i = arTabScroll + row;
                if (i >= arTable.Count) break;
                Rectangle r = new Rectangle(R_ArTabList.X, R_ArTabList.Y + row * AR_TAB_ROW, R_ArTabList.Width, AR_TAB_ROW - 4);
                bool used = i == lo || i == hi;
                FillRR(g, used ? A(40, C_Accent) : C_Btn, r, 8);
                DrawText(g, arTable[i][0] + " m", fBtn, C_Text, new Rectangle(r.X + 14, r.Y, 100, r.Height), StringAlignment.Near);
                DrawText(g, "→", fBtn, C_Dim, new Rectangle(r.X + 116, r.Y, 30, r.Height), StringAlignment.Center);
                DrawText(g, arTable[i][1] + " mil", fBtn, used ? C_Accent : C_Text, new Rectangle(r.X + 156, r.Y, 120, r.Height), StringAlignment.Near);
                bool xh = hover == Hit.ArTabX && hoverIdx == i;
                Rectangle xr = new Rectangle(r.Right - 26, r.Y + (r.Height - 18) / 2, 18, 18);
                if (xh) using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 60, 60))) g.FillEllipse(b, xr);
                using (Pen pen = new Pen(xh ? Color.White : C_Dim, 1.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, xr.X + 6, xr.Y + 6, xr.X + 12, xr.Y + 12);
                    g.DrawLine(pen, xr.X + 12, xr.Y + 6, xr.X + 6, xr.Y + 12);
                }
            }

            string hint = arErr.Length > 0 ? arErr
                : "Enter = eintragen  ·  gleiche Entfernung überschreibt  ·  " + (arTable.Count > AR_TAB_ROWS ? "Mausrad = scrollen  ·  " : "") + "Esc = schließen";
            DrawText(g, hint, fChip, arErr.Length > 0 ? DevIcons.Level(0) : C_Dim,
                new Rectangle(R_Pop.X + 20, R_Pop.Bottom - 26, R_Pop.Width - 40, 20), StringAlignment.Far);
        }

        // Akku-Seite: Stand der Geraete + Schalter fuer die Meldungen
        void DrawBatteryPage(Graphics g)
        {
            Card(g, R_BtCard, "AKKU");
            DrawText(g, "Wird jede Minute abgefragt, ohne Herstellersoftware", fChip, C_Dim,
                new Rectangle(R_BtCard.X + 170, R_BtCard.Y + 8, 420, 20), StringAlignment.Near);

            const int barX = 340, barW = 220;
            for (int i = 0; i < Battery.Devices.Length; i++)
            {
                BattDev d = Battery.Devices[i];
                int y = BT_Y0 + i * BT_ROW;
                Rectangle row = new Rectangle(36, y, 628, BT_ROW - 12);
                float rr = Math.Min(RadCtl(row.Height), 12f);
                FillRR(g, C_Field, row, rr);
                StrokeRR(g, C_CardLine, row, rr, 1f);
                Color lc = d.Known ? DevIcons.Level(d.Last.Percent) : C_Dim;
                if (!d.Online) lc = Color.FromArgb(110, lc);
                DevIcons.Draw(g, d.Kind, new RectangleF(52, y + 8, 32, 32), d.Online ? C_Text : C_Dim, 1.8f);
                DrawText(g, d.Name, fBtn, d.Online ? C_Text : C_Dim, new Rectangle(100, y + 4, 230, 22), StringAlignment.Near);
                DrawText(g, BattStatus(d), fChip, C_Dim, new Rectangle(100, y + 24, 236, 18), StringAlignment.Near);

                // Balken in der Ampelfarbe
                Rectangle track = new Rectangle(barX, y + row.Height / 2 - 4, barW, 8);
                FillRR(g, Color.FromArgb(40, 40, 40), track, 4);
                if (d.Known)
                    FillRR(g, lc, new Rectangle(track.X, track.Y, Math.Max(8, barW * d.Last.Percent / 100), track.Height), 4);
                DrawText(g, d.Known ? d.Last.Percent + " %" : "–", fTitle, d.Online ? lc : C_Dim,
                    new Rectangle(barX + barW + 8, y, 640 - barX - barW, row.Height), StringAlignment.Far);
            }

            using (Pen sep = new Pen(C_CardLine, 1f))
                g.DrawLine(sep, 36, 278, 664, 278);
            DrawText(g, "Meldung, wenn der Akku fällt", fBtn, C_Text, new Rectangle(40, 284, 560, 20), StringAlignment.Near);
            DrawText(g, "Oben rechts bei 70 · 50 · 30 · 20 · 10 % und wenn ein Gerät voll geladen ist",
                fChip, C_Dim, new Rectangle(40, 303, 570, 18), StringAlignment.Near);
            Switch(g, R_BtDropSw, battDropMsg, hover == Hit.BtDropSw);
            DrawText(g, "Bei 30 und 10 % auch als Windows-Benachrichtigung", fBtn, C_Text, new Rectangle(40, 328, 560, 20), StringAlignment.Near);
            DrawText(g, "Mit Windows-Signalton, erscheint auch im Vollbild, verschwindet nach " + ToastSec + " s",
                fChip, C_Dim, new Rectangle(40, 347, 570, 18), StringAlignment.Near);
            Switch(g, R_BtToastSw, battToastMsg, hover == Hit.BtToastSw);
            DrawText(g, "Meldung beim PC-Start", fBtn, C_Text, new Rectangle(40, 372, 560, 20), StringAlignment.Near);
            DrawText(g, "Stand aller Geräte, wenn Purple Clicker mit Windows startet (Autostart)",
                fChip, C_Dim, new Rectangle(40, 391, 570, 18), StringAlignment.Near);
            Switch(g, R_BtBootSw, battBootMsg, hover == Hit.BtBootSw);

            FillRR(g, hover == Hit.BtPreview ? C_BtnHover : C_Btn, R_Start, radStart);
            StrokeRR(g, C_CardLine, R_Start, radStart, 1f);
            DrawText(g, "Vorschau der Meldungen", fBtn, C_Text, R_Start, StringAlignment.Center);
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

        // Offizielles GitHub-Logo (Primer Octicon "mark-github", 16 x 16), aus dem SVG-Pfad in Punkte umgerechnet
        static readonly float[] GitHubMark = {
            8f, 0f, 7.18f, 0.04f, 6.39f, 0.16f, 5.62f, 0.36f, 4.89f, 0.63f, 4.19f, 0.97f, 3.53f, 1.37f, 2.91f, 1.83f, 2.34f, 2.34f, 1.83f, 2.91f, 1.37f, 3.53f, 0.97f, 4.19f, 0.63f, 4.89f, 0.36f, 5.62f, 0.16f, 6.39f, 0.04f, 7.18f, 0f, 8f, 0.03f, 8.66f, 0.1f, 9.3f, 0.23f, 9.92f, 0.41f, 10.53f, 0.63f, 11.12f, 0.89f, 11.68f, 1.54f, 12.73f, 2.34f, 13.66f, 3.27f, 14.45f, 4.32f, 15.1f, 4.88f, 15.37f, 5.47f, 15.59f, 5.72f, 15.59f, 5.89f, 15.51f, 5.99f, 15.37f, 6.02f, 15.21f, 6.02f, 14.99f, 6.02f, 14.65f, 6.01f, 14.21f, 6.01f, 13.72f, 5.32f, 13.8f, 4.76f, 13.79f, 4.32f, 13.7f, 3.97f, 13.56f, 3.71f, 13.37f, 3.53f, 13.17f, 3.4f, 12.96f, 3.32f, 12.78f, 3.27f, 12.67f, 3.2f, 12.53f, 3f, 12.2f, 2.76f, 11.87f, 2.63f, 11.74f, 2.5f, 11.65f, 2.29f, 11.51f, 2.14f, 11.33f, 2.12f, 11.25f, 2.17f, 11.19f, 2.29f, 11.14f, 2.49f, 11.12f, 2.72f, 11.14f, 2.93f, 11.21f, 3.28f, 11.44f, 3.55f, 11.71f, 3.72f, 11.94f, 4.01f, 12.32f, 4.32f, 12.58f, 4.65f, 12.74f, 4.98f, 12.8f, 5.3f, 12.8f, 5.59f, 12.76f, 5.85f, 12.68f, 6.05f, 12.6f, 6.13f, 12.24f, 6.24f, 11.94f, 6.39f, 11.71f, 6.56f, 11.53f, 5.89f, 11.43f, 5.24f, 11.26f, 4.63f, 11f, 4.07f, 10.63f, 3.6f, 10.12f, 3.24f, 9.46f, 3.1f, 9.06f, 3f, 8.62f, 2.94f, 8.13f, 2.92f, 7.58f, 2.98f, 6.96f, 3.14f, 6.39f, 3.4f, 5.88f, 3.74f, 5.43f, 3.7f, 5.33f, 3.66f, 5.17f, 3.61f, 4.96f, 3.59f, 4.71f, 3.58f, 4.41f, 3.61f, 4.08f, 3.69f, 3.71f, 3.82f, 3.31f, 3.85f, 3.3f, 3.95f, 3.29f, 4.11f, 3.3f, 4.35f, 3.33f, 4.65f, 3.42f, 5.03f, 3.57f, 5.49f, 3.8f, 6.02f, 4.13f, 7.01f, 3.93f, 8.02f, 3.86f, 9.03f, 3.93f, 10.02f, 4.13f, 10.55f, 3.8f, 11.01f, 3.56f, 11.39f, 3.41f, 11.69f, 3.33f, 11.93f, 3.3f, 12.09f, 3.29f, 12.19f, 3.3f, 12.22f, 3.31f, 12.35f, 3.71f, 12.43f, 4.08f, 12.46f, 4.41f, 12.45f, 4.71f, 12.43f, 4.96f, 12.38f, 5.17f, 12.34f, 5.33f, 12.3f, 5.43f, 12.64f, 5.88f, 12.9f, 6.38f, 13.06f, 6.95f, 13.12f, 7.58f, 13.1f, 8.13f, 13.04f, 8.62f, 12.94f, 9.07f, 12.8f, 9.46f, 12.43f, 10.13f, 11.96f, 10.63f, 11.41f, 11f, 10.79f, 11.26f, 10.14f, 11.43f, 9.47f, 11.53f, 9.68f, 11.76f, 9.85f, 12.08f, 9.97f, 12.5f, 10.01f, 13.01f, 10.01f, 13.77f, 10.01f, 14.41f, 10f, 14.68f, 10f, 14.9f, 10f, 15.08f, 10f, 15.21f, 10.03f, 15.37f, 10.13f, 15.51f, 10.3f, 15.59f, 10.55f, 15.59f, 11.15f, 15.36f, 11.72f, 15.09f, 12.78f, 14.42f, 13.71f, 13.61f, 14.5f, 12.67f, 15.13f, 11.62f, 15.61f, 10.48f, 15.78f, 9.88f, 15.9f, 9.27f, 15.97f, 8.64f, 16f, 8f, 15.96f, 7.18f, 15.84f, 6.39f, 15.64f, 5.62f, 15.37f, 4.89f, 15.03f, 4.19f, 14.63f, 3.53f, 14.17f, 2.91f, 13.66f, 2.34f, 13.09f, 1.83f, 12.47f, 1.37f, 11.81f, 0.97f, 11.11f, 0.63f, 10.38f, 0.36f, 9.61f, 0.16f, 8.82f, 0.04f, 8f, 0f };

        // Link-Knopf in der Titelleiste (0 = Doku, 1 = GitHub), gleiche Form wie Maus- und Farbrad-Knopf;
        // liefert das Rechteck fuer die Treffererkennung
        Rectangle DrawTitleIcon(Graphics g, int kind, int x, bool hot)
        {
            Rectangle r = new Rectangle(x, 13, 30, 30);
            BtnShape(g, hot ? C_BtnHover : C_Btn, r);
            Color c = C_Text;
            float ox = r.X + 6, oy = r.Y + 6;   // 18 x 18 Symbol mittig
            if (kind == 1)
            {
                const float s = 18f / 16f;
                PointF[] pts = new PointF[GitHubMark.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(ox + GitHubMark[2 * i] * s, oy + GitHubMark[2 * i + 1] * s);
                using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, pts);
            }
            else
            {
                // Dokument mit Eselsohr und Textzeilen
                using (Pen pen = new Pen(c, 1.6f))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawPolygon(pen, new PointF[] {
                        new PointF(ox + 3, oy + 1), new PointF(ox + 11, oy + 1), new PointF(ox + 15, oy + 5),
                        new PointF(ox + 15, oy + 17), new PointF(ox + 3, oy + 17) });
                    g.DrawLines(pen, new PointF[] { new PointF(ox + 11, oy + 1), new PointF(ox + 11, oy + 5), new PointF(ox + 15, oy + 5) });
                    for (int i = 0; i < 3; i++) g.DrawLine(pen, ox + 6, oy + 9 + i * 3, ox + 12, oy + 9 + i * 3);
                }
            }
            return r;
        }

        // Link im Standardbrowser oeffnen
        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { }
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
                        case "BattDropMsg": battDropMsg = v != 0; break;
                        case "BattToastMsg": battToastMsg = v != 0; break;
                        case "BattBootMsg": battBootMsg = v != 0; break;
                        case "Theme": theme = ThemeFromSetting(raw); break;
                        case "ArtyPos":
                        case "ArtyTarget":
                        {
                            int fi = k == "ArtyPos" ? 0 : 1;
                            double x, y;
                            if (Arty.Parse(raw, out x, out y)) { arX[fi] = x; arY[fi] = y; arText[fi] = ArShow(fi); }
                            break;
                        }
                        case "ArtyTargets":   // "Nr x y;Nr x y"
                            arTargets.Clear();
                            foreach (string s in raw.Split(';'))
                            {
                                string[] f = s.Trim().Split(' ');
                                int no;
                                double x, y;
                                if (f.Length == 3 && int.TryParse(f[0], out no)
                                    && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                                    && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                                    && arTargets.Count < AR_MAX)
                                    arTargets.Add(new ArTarget(no, x, y));
                            }
                            break;
                        case "ArtyTable":     // "m mil;m mil"
                            arTable.Clear();
                            foreach (string s in raw.Split(';'))
                            {
                                string[] f = s.Trim().Split(' ');
                                int d, m;
                                if (f.Length == 2 && int.TryParse(f[0], out d) && int.TryParse(f[1], out m) && d > 0
                                    && !arTable.Exists(delegate(int[] e) { return e[0] == d; }))
                                    arTable.Add(new int[] { d, m });
                            }
                            arTable.Sort(delegate(int[] p, int[] q) { return p[0].CompareTo(q[0]); });
                            break;
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
                // Zahlen des Artillerie-Rechners immer mit Punkt, unabhaengig von der Windows-Sprache
                List<string> targets = new List<string>();
                foreach (ArTarget t in arTargets) targets.Add(t.No + " " + Arty.Num4(t.X) + " " + Arty.Num4(t.Y));
                List<string> table = new List<string>();
                foreach (int[] e in arTable) table.Add(e[0] + " " + e[1]);
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
                    "BattDropMsg=" + (battDropMsg ? 1 : 0),
                    "BattToastMsg=" + (battToastMsg ? 1 : 0),
                    "BattBootMsg=" + (battBootMsg ? 1 : 0),
                    "Theme=" + ThemeNames[theme],
                    "Programs=" + string.Join(";", programs.ToArray()),
                    "ArtyPos=" + (double.IsNaN(arX[0]) ? "" : Arty.Num4(arX[0]) + " " + Arty.Num4(arY[0])),
                    "ArtyTarget=" + (double.IsNaN(arX[1]) ? "" : Arty.Num4(arX[1]) + " " + Arty.Num4(arY[1])),
                    "ArtyTargets=" + string.Join(";", targets.ToArray()),
                    "ArtyTable=" + string.Join(";", table.ToArray())
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
                // Akku-Seite: verbunden, laedt, nicht erreichbar
                BattDev[] bd = Battery.Devices;
                bd[0].Polled = bd[0].Online = bd[0].Known = true; bd[0].Last.Percent = 44;
                bd[1].Polled = bd[1].Online = bd[1].Known = true; bd[1].Last.Percent = 83; bd[1].Last.Charging = true;
                bd[2].Polled = bd[2].Known = true; bd[2].Last.Percent = 20; bd[2].LastSeen = Environment.TickCount - 7 * 60000;
                // Artillerie-Seite: erfundene Tafel und Ziele (--rec 1 zeigt die Schusstafel)
                arX[0] = 77.11; arY[0] = 75.32; arText[0] = ArShow(0);
                arX[1] = 79.40; arY[1] = 71.20; arText[1] = ArShow(1);
                arTargets.Clear();
                arTargets.Add(new ArTarget(1, 80.91, 68.58));
                arTargets.Add(new ArTarget(2, 79.40, 71.20));
                arTargets.Add(new ArTarget(3, 75.02, 79.88));
                arTable.Clear();
                arTable.Add(new int[] { 150, 1400 });
                arTable.Add(new int[] { 300, 1250 });
                arTable.Add(new int[] { 450, 1080 });
                arTable.Add(new int[] { 600, 850 });
                if (page == 3 && snapRec == 1) { arTabOpen = true; arFocus = 2; arText[2] = "471 "; }
                if (page == 3 && snapRec == 2) { arFocus = 1; arText[1] = "x79.4, y71"; }
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
                    if (i + 2 < args.Length && args[i + 2] == "--battery") new OverlayForm(lg, sc).SnapshotBattery(args[i + 1]);
                    else new OverlayForm(lg, sc).Snapshot(args[i + 1]);
                    return;
                }
                if (args[i] == "--tray") tray = true;
                else if (args[i] == "--battery-sim") Battery.Sim = true;
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
