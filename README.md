# Purple Clicker

Kleiner Autoclicker für Windows mit AMOLED-Oberfläche, gebaut in C# (WinForms), eine einzige `.cs`-Datei.

- Download: https://netpurple.net/data/PurpleClicker.exe
- Beschreibung (Englisch): https://docu.netpurple.net/docs/PurpleClicker/purple-clicker

## Funktionen
- Globaler Hotkey (Taste oder Maustaste 1–5, wird blockiert) als Toggle, funktioniert auch in anderen Programmen
- Alle Hotkeys (Start/Stop, Multiplikator, Tastenanzeige, FPS-Anzeige) optional mit Strg, Shift, Alt und Win: beim Belegen halten, dann Taste drücken. Ohne Modifikator feuert ein Hotkey auch bei gehaltenem Shift (Sprinten); passen mehrere, gewinnt der genauere (Shift+F6 vor F6). Bei Alt/Win wird ein unsichtbarer Zwischen-Tastendruck gesendet, damit Windows kein Menü bzw. Startmenü öffnet
- Intervall in ms, linke/rechte Maustaste, Single/Double
- Multiplikator: 1 echter Klick = bis zu 10 Klicks
- Hotkey nur in gewählten Programmen aktiv (Meldung bei Erkennung)
- Autostart mit Windows (startet im Tray), einstellbar auf der Einstellungsseite (Zahnrad oben rechts)
- Jede Funktion hat ihr Symbol in der Titelleiste, auch der Autoclicker. Die Startseite (Autoclicker, Tastenanzeige, FPS-Anzeige, Rechner, Akku oder Maus) ist einstellbar: sie öffnet sich beim Programmstart, ein Klick auf das Logo oben links und Esc führen dorthin zurück
- Einstellungsseite: für Autoclicker, Tastenanzeige, FPS-Anzeige, Artillerie-Rechner, Akku, Maus-Umbelegung und Theme-Auswahl festlegen, ob das Symbol in der Titelleiste erscheint, und optional nur, solange bestimmte Programme laufen (z. B. den Rechner nur, wenn Wardogs läuft; geprüft alle 2 s). Ausblenden versteckt nur das Symbol, die Funktion läuft weiter
- Maus-Umbelegung: Tasten, Rad und Kippen frei belegen (Linksklick, Rechtsklick, Doppelklick, Deaktiviert usw.), Mausbild mit Markern, Aufnahme-Modus erkennt, welche Tasten die Maus meldet
- Akku-Anzeige ohne Herstellersoftware (Corsair HS80 MAX, ROG Strix Scope II 96, Attack Shark X3): eigene Seite mit Stand aller Geräte, Meldung oben rechts beim Unterschreiten von 70/50/30/20/10 % und bei „voll geladen“, bei 30 und 10 % zusätzlich als Windows-Benachrichtigung (Wecker-Art, erscheint auch im Vollbild, mit Windows-Signalton), Stand aller Geräte beim PC-Start; alles einzeln abschaltbar, ohne Admin-Rechte. Laden löst keine Stufen-Meldungen aus. `--battery-sim` spielt zum Testen erfundene Werte ab
- Tastenanzeige (Tastatur-Symbol in der Titelleiste oder Tray-Menü): durchsichtiges Overlay über dem Spiel, das W/A/S/D, Shift, Strg, Leertaste, linke/mittlere/rechte Maustaste und das Mausrad beim Drücken aufleuchten lässt. Klickdurchlässig, nimmt keinen Fokus, nicht in Alt-Tab, sendet selbst keine Eingaben. Jede der 12 Tasten einzeln ein-/ausblendbar und in der Vorschau per Maus frei anzuordnen und in der Größe zu ändern (Mitte ziehen verschiebt, Rand/Ecke ziehen ändert die Größe, Schrift wächst mit; „Einrasten“ (abschaltbar) richtet Kanten und Mitten an den anderen Tasten und an der Mitte der Tastengruppe aus, zeigt dabei X/Y-Hilfslinien und nutzt sonst ein 4-px-Raster, ausgeschaltet geht es pixelgenau; „Zurücksetzen“ stellt Anordnung und Größen wieder her); das Overlay schrumpft auf die genutzte Fläche. Eigener belegbarer Hotkey (Taste oder Maustaste) schaltet die Tastenanzeige an/aus. „Überall“ zeigt sie in allen Programmen, ausgeschaltet nur in den gewählten Programmen (Liste unten auf der Seite). Jedes Programm hat eine eigene Bildschirmposition, die automatisch gilt, sobald es im Vordergrund ist; der gewählte Chip (Standard oder Programm) bestimmt, wessen Position man einstellt. Position per Ecke oder frei mit der Maus verschiebbar („Frei verschieben“, funktioniert auch über dem Spiel, der Klick kommt dort nicht an; Enter/Esc oder „Fertig“ beendet; rastet an Rand und Bildschirmmitte ein, auch auf Zweitbildschirmen), Größe (60–200 %), Deckkraft (20–100 %) und Randabstand einstellbar. Zeigt nur echte Eingaben, nicht die Klicks des Autoclickers oder der Umbelegung. Erscheint nicht über Spielen im exklusiven Vollbild (dort „Randloses Fenster“ wählen); solange ein als Administrator laufendes Programm im Vordergrund ist, sieht es keine Tasten. `--keys-snapshot datei.png [--keys W,A,LMB] [--hide MMB,RadHoch] [--theme N] [--placing]` rendert es zum Testen in eine PNG
- FPS-Anzeige (Tacho-Symbol in der Titelleiste oder Tray-Menü): Overlay mit FPS, Frametime, GPU-Auslastung und -Temperatur sowie CPU-Auslastung und -Temperatur, jede Kachel einzeln abschaltbar. Verschieben, Größe, Deckkraft, Randabstand und Anordnen der Kacheln wie bei der Tastenanzeige; Kacheln, die aneinanderliegen (beim Einrasten Kante an Kante), verschmelzen zu einer Box. „Hintergrund“ stellt die Deckkraft der Boxen samt Rand ein, bei 0 % bleiben nur die Werte; die Schrift hat eine dunkle Kontur und bleibt so auf jedem Spielbild lesbar, ihre Größe richtet sich nach dem breitesten möglichen Wert und springt nicht. Eigener Hotkey, „Nur im Vollbild“ zeigt sie nur, solange ein Fenster seinen Bildschirm ganz füllt (Spiel im Vollbild oder randlos). Gemessen wird das Programm im Vordergrund.
  - FPS per ETW wie PresentMon und die Xbox Game Bar: Present-Aufrufe von DXGI (DX10–12) und D3D9, für Vulkan/OpenGL der Present im Grafik-Kernel. Es wird nichts ins Spiel geladen. Ohne Admin-Rechte, sofern der Benutzer in der Gruppe „Leistungsprotokollbenutzer“ ist (richtet u. a. die Game Bar für ihren FPS-Zähler ein); sonst bietet die Seite „Zugriff einrichten“ an (einmal UAC, gilt nach dem nächsten Anmelden)
  - GPU über den Treiber des Herstellers: AMD per ADL, NVIDIA per NVML, sonst die Windows-Zähler des Task-Managers (dann ohne Temperatur). Bei mehreren GPUs zählt die stärkste (eigene Grafikkarte vor iGPU, dann mehr Grafikspeicher)
  - CPU-Auslastung von Windows. Die echte Kern-Temperatur liest Windows nur über einen Kernel-Treiber; ohne den nimmt die Anzeige den CPU- bzw. SoC-Sensor der integrierten AMD-Grafik (Ryzen mit iGPU; der SoC liegt unter Last etwas unter den Kernen), sonst die ACPI-Thermalzone, sonst bleibt die Kachel leer. Die Seite zeigt, welche Quelle benutzt wird
  - `--fps-snapshot datei.png [--hide GPU,CpuTemp] [--theme N] [--scale 150] [--placing]` rendert das Overlay mit Beispielwerten, `--perf-log datei.txt [--sec 10] [--windows-gpu]` schreibt die Messwerte zum Testen in eine Datei
- Artillerie-Rechner für Wardogs (Mörser L81, Fadenkreuz-Symbol in der Titelleiste): Position und Ziel per Strg+V aus der Karte einfügen (`x77.11, y75.32`, Tippen geht auch, Strg+V ohne gewähltes Feld landet im Ziel). Zeigt Entfernung (1 Kartenfeld = 100 m), Richtung in Grad mit Himmelsrichtung und mil, und ob das Ziel in Reichweite liegt (ca. 130–685 m laut Community-Guides). Die Rohrerhöhung kommt aus einer eigenen Schusstafel: nach Probeschüssen Entfernung → Mil eintragen, dazwischen wird gleichmäßig gerechnet (eine offizielle Tabelle gibt es nicht). Bis zu 8 Ziele speicherbar; Position, Ziele und Tafel bleiben in `settings.ini`. Der Rechner sendet nichts an das Spiel

## Bauen
Keine Installation nötig, es wird der in Windows enthaltene C#-Compiler benutzt:

```
build.cmd
```

`build.cmd` bettet das Mausfoto aus `assets/` in die exe ein.

Ergebnis ist `PurpleClicker.exe` (portabel, braucht nur .NET Framework 4.x, das in Windows 10/11 enthalten ist).
Einstellungen landen in `settings.ini` neben der exe.

## Hinweise
Spiele, die als Administrator laufen oder Anti-Cheat nutzen, ignorieren Eingaben fremder Programme häufig. Bei Online-Spielen kann ein Autoclicker zu Sperren führen.
