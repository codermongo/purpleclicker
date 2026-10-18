# Purple Clicker

Kleiner Autoclicker für Windows mit AMOLED-Oberfläche, gebaut in C# (WinForms), eine einzige `.cs`-Datei.

- Download: https://netpurple.net/data/PurpleClicker.exe
- Beschreibung (Englisch): https://docu.netpurple.net/docs/PurpleClicker/purple-clicker

## Funktionen
- Globaler Hotkey (Taste oder Maustaste 1–5, wird blockiert) als Toggle, funktioniert auch in anderen Programmen
- Intervall in ms, linke/rechte Maustaste, Single/Double
- Multiplikator: 1 echter Klick = bis zu 10 Klicks
- Hotkey nur in gewählten Programmen aktiv (Meldung bei Erkennung)
- Autostart mit Windows (startet im Tray)
- Maus-Umbelegung: Tasten, Rad und Kippen frei belegen (Linksklick, Rechtsklick, Doppelklick, Deaktiviert usw.), Mausbild mit Markern, Aufnahme-Modus erkennt, welche Tasten die Maus meldet
- Akku-Anzeige ohne Herstellersoftware (Corsair HS80 MAX, ROG Strix Scope II 96, Attack Shark X3): eigene Seite mit Stand aller Geräte, Meldung oben rechts beim Unterschreiten von 70/50/30/20/10 % und bei „voll geladen“, bei 30 und 10 % zusätzlich als Windows-Benachrichtigung (Wecker-Art, erscheint auch im Vollbild, mit Windows-Signalton), Stand aller Geräte beim PC-Start; alles einzeln abschaltbar, ohne Admin-Rechte. Laden löst keine Stufen-Meldungen aus. `--battery-sim` spielt zum Testen erfundene Werte ab
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
