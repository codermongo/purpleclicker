# Purple Clicker

Kleiner Autoclicker für Windows mit AMOLED-Oberfläche, gebaut in C# (WinForms), eine einzige `.cs`-Datei.

## Funktionen
- Globaler Hotkey (Taste oder Maustaste 1–5, wird blockiert) als Toggle, funktioniert auch in anderen Programmen
- Intervall in ms, linke/rechte Maustaste, Single/Double
- Multiplikator: 1 echter Klick = bis zu 10 Klicks
- Hotkey nur in gewählten Programmen aktiv (Meldung bei Erkennung)
- Autostart mit Windows (startet im Tray)
- Maus-Umbelegung: Tasten, Rad und Kippen frei belegen (Linksklick, Rechtsklick, Doppelklick, Deaktiviert usw.), Mausbild mit Markern, Aufnahme-Modus erkennt, welche Tasten die Maus meldet

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
