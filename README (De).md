# MicMute 2
MicMute ist ein kleines Windows-Tool, das es ermöglicht, das Mikrofon per System-Tray-Icon schnell stummzuschalten oder zu aktivieren. Es zeigt den aktuellen Mikrofonzustand (an/aus) über ein Tray-Icon an und speichert diesen Zustand in einer Konfigurationsdatei. Das Programm ist ideal für Nutzer, die eine einfache Möglichkeit suchen, ihr Mikrofon während Videokonferenzen oder Sprachaufnahmen zu steuern.

![mic-on-off](https://github.com/user-attachments/assets/5277a8af-3598-4b3c-a46c-df598fce5b6c)

## Funktionen
- System-Tray-Icon: Zeigt an, ob das Mikrofon eingeschaltet oder stummgeschaltet ist.
- Ein-Klick-Steuerung: Stummschalten oder Aktivieren des Mikrofons per Linksklick auf das Tray-Icon oder per Rechtsklick über das Kontextmenü.
- Automatische Zustandsspeicherung: Der Mikrofonzustand wird in **`MicMuteConfig.txt`** gespeichert und beim Programmstart geladen.
- Autostart (Optional): Kann über den Windows-Taskplaner oder den Autostart-Ordner beim Anmelden gestartet werden.

## Voraussetzungen
- Windows-Betriebssystem (getestet mit Windows 10/11).
- .NET Framework 4.0 oder höher.
- Zwei Icon-Dateien: **`mic_on.ico`** und **`mic_off.ico`** (müssen im gleichen Verzeichnis wie die ausführbare Datei liegen).

## Installation
1. **Download:**
   - Lade die [ZIP-Datei](https://github.com/rjcncpt/micmute/releases) herunter.
   - Entpacke die ZIP-Datei.
   - Kopiere das micmute-Verzeichnis nach **`C:\`**.
3. **Icons bereitstellen:**
   - Stelle sicher, dass die Dateien **`mic_on.ico`** und **`mic_off.ico`** im Verzeichnis **`C:\micmute\`** vorhanden sind. Du kannst eigene Icons erstellen oder kostenlose Icons von Websites wie IconArchive verwenden.
4. **Kompilieren:**
   - Öffne eine Eingabeaufforderung (CMD) und führe den folgenden Befehl aus, um den Code zu kompilieren:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /out:"C:\micmute\MicMute2.exe" /target:winexe /platform:anycpu /optimize /nologo "C:\micmute\MicMute.cs"
   ```
   - Dies erstellt die ausführbare Datei MicMute2.exe in **`C:\micmute\`**.
   - Verschiebe nun den Ordner an einen Ort deiner Wahl.
5. **Ausführen:**
   - Starte **`MicMute2.exe`** aus **`C:\micmute\`**.
   - Das Tray-Icon erscheint in der Taskleiste und zeigt den Mikrofonzustand an.
   - Der Standard Zustand ist **`False`**, was bedeutet, dass Mikrofon ist eingeschaltet. Vergewissere dich das dein Mikrofon beim ersten Ausführen aktiviert ist.
   - Ein Linksklick verändert den Zustand des Icons und speichert diesen in die MicMuteConfig.txt Datei.
  
## Hinweise
- Icons: Stelle sicher, dass mic_on.ico und mic_off.ico im Verzeichnis C:\micmute\ vorhanden sind, da sie für das Tray-Icon benötigt werden.
- Konfigurationsdatei: Der Mikrofonzustand wird in **`C:\micmute\MicMuteConfig.txt`** gespeichert.

## Lizenz
Dieses Projekt steht unter der GPL-3.0 Lizenz. Du kannst den Code frei verwenden, modifizieren und verteilen, solange die Lizenzbedingungen eingehalten werden.

## Autoren
AveYo: Originalentwickler (06.04.2019)<br>
rjcncpt: Bearbeiter (Stand: 27.05.2025)
