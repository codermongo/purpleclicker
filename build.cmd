@echo off
setlocal
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ /win32icon:PurpleClicker.ico /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:PurpleClicker.exe PurpleClicker.cs
if errorlevel 1 (echo BUILD FEHLGESCHLAGEN) else (echo OK: PurpleClicker.exe)
