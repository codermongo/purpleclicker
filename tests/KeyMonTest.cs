// Konsolen-Harness fuer KeyMon (Tastenanzeige). Bauen und starten aus dem Repo-Ordner:
//   "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 /main:PurpleClicker.KeyMonTest
//       /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:tests\KeyMonTest.exe PurpleClicker.cs tests\KeyMonTest.cs
//   tests\KeyMonTest.exe
using System;
using System.Threading;

namespace PurpleClicker
{
    static class KeyMonTest
    {
        static int fails;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok    " : "  FEHLER ") + what);
            if (!ok) fails++;
        }

        static int Main()
        {
            const int W = 0x57, LSHIFT = 0xA0, RSHIFT = 0xA1;
            KeyMon m = new KeyMon();

            Console.WriteLine("Druecken und Loslassen");
            m.Key(W, true);
            Check(m.Down(CapSrc.Key, W), "W gedrueckt");
            Check(m.Dirty, "Dirty nach Druck");
            m.Dirty = false;
            m.Key(W, false);
            Check(!m.Down(CapSrc.Key, W), "W losgelassen");
            Check(m.Dirty, "Dirty nach Loslassen");

            Console.WriteLine("Auto-Repeat (mehrfach down ohne up)");
            m.Key(W, true);
            m.Dirty = false;
            for (int i = 0; i < 20; i++) m.Key(W, true);
            Check(m.Down(CapSrc.Key, W), "W bleibt gedrueckt");
            Check(!m.Dirty, "kein Neuzeichnen durch Wiederholung");
            m.Key(W, false);
            Check(!m.Down(CapSrc.Key, W), "ein Up reicht zum Loslassen");

            Console.WriteLine("Up ohne vorheriges Down");
            m.Dirty = false;
            m.Key(0x41, false);
            m.Mouse(2, false);
            Check(!m.Down(CapSrc.Key, 0x41) && !m.Down(CapSrc.Mouse, 2), "bleibt oben");
            Check(!m.Dirty, "kein Neuzeichnen");

            Console.WriteLine("Links/rechts-Varianten der Umschalttasten");
            m.Key(RSHIFT, true);
            Check(m.Down(CapSrc.Key, 0x10), "rechtes Shift zaehlt als Shift");
            m.Key(LSHIFT, true);
            m.Key(RSHIFT, false);
            Check(m.Down(CapSrc.Key, 0x10), "linkes Shift haelt Shift weiter");
            m.Key(LSHIFT, false);
            Check(!m.Down(CapSrc.Key, 0x10), "beide los: Shift aus");
            m.Key(0xA3, true);
            Check(m.Down(CapSrc.Key, 0x11), "rechtes Strg zaehlt als Strg");
            m.Key(0xA3, false);

            Console.WriteLine("Mehrere gleichzeitig, Maus 1 bis 5");
            m.Key(W, true); m.Key(0x41, true); m.Key(0x20, true);
            for (int b = 1; b <= 5; b++) m.Mouse(b, true);
            bool all = m.Down(CapSrc.Key, W) && m.Down(CapSrc.Key, 0x41) && m.Down(CapSrc.Key, 0x20);
            for (int b = 1; b <= 5; b++) all &= m.Down(CapSrc.Mouse, b);
            Check(all, "alle gehalten");
            m.Mouse(0, true); m.Mouse(6, true);
            Check(!m.Down(CapSrc.Mouse, 0) && !m.Down(CapSrc.Mouse, 6), "ungueltige Maustasten ignoriert");

            Console.WriteLine("Clear mitten im Gedrueckthalten");
            m.Clear();
            bool none = !m.Down(CapSrc.Key, W) && !m.Down(CapSrc.Key, 0x41) && !m.Down(CapSrc.Key, 0x20);
            for (int b = 1; b <= 5; b++) none &= !m.Down(CapSrc.Mouse, b);
            Check(none, "alles oben");
            m.Key(W, false);
            m.Mouse(1, false);
            Check(!m.Down(CapSrc.Key, W) && !m.Down(CapSrc.Mouse, 1), "spaete Ups nach Clear harmlos");
            m.Key(W, true);
            Check(m.Down(CapSrc.Key, W), "neuer Druck nach Clear wirkt");
            m.Key(W, false);

            Console.WriteLine("Radimpuls und Ablauf des Nachleuchtens");
            m.Wheel(false, 120);
            Check(m.Down(CapSrc.WheelUp, 0) && !m.Down(CapSrc.WheelDown, 0), "Rad hoch leuchtet, Rad runter nicht");
            Check(m.GlowPending(), "GlowPending waehrend des Nachleuchtens");
            m.Wheel(true, -120);
            Check(m.Down(CapSrc.TiltLeft, 0) && !m.Down(CapSrc.TiltRight, 0), "Kippen links leuchtet");
            Thread.Sleep(KeyMon.GlowMs + 60);
            Check(!m.Down(CapSrc.WheelUp, 0) && !m.Down(CapSrc.TiltLeft, 0), "erloschen nach " + KeyMon.GlowMs + " ms");
            Check(!m.GlowPending(), "GlowPending danach false");
            m.Dirty = false;
            m.Wheel(false, 0);
            Check(!m.Dirty && !m.GlowPending(), "Delta 0 ignoriert");
            m.Wheel(false, -120);
            m.Clear();
            Check(!m.GlowPending(), "Clear loescht Nachleuchten");

            Console.WriteLine("Sweep (verpasstes Loslassen)");
            m.Key(W, true); m.Key(0x41, true);
            m.Sweep(delegate(int vk) { return true; });
            Check(m.Down(CapSrc.Key, W), "frisch gedrueckt: Sweep prueft noch nicht");
            Thread.Sleep(560);
            m.Dirty = false;
            m.Sweep(delegate(int vk) { return vk == W; });
            Check(!m.Down(CapSrc.Key, W), "W freigegeben (System meldet oben)");
            Check(m.Down(CapSrc.Key, 0x41), "A bleibt (System meldet gedrueckt)");
            Check(m.Dirty, "Dirty nach Freigabe");

            Console.WriteLine(fails == 0 ? "Alles bestanden." : fails + " Fehler.");
            return fails == 0 ? 0 : 1;
        }
    }
}
