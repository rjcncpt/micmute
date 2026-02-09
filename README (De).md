# MicMute 2

**Version:** v2.1.0  
**Letzte Änderung:** 09.02.2026  
**Autoren:** 
- AveYo (Original, nicht mehr auf Github verfügbar)
- rjcncpt (Verbesserungen). 

MicMute ist ein kleines Windows-Tool, das es ermöglicht, das Mikrofon per System-Tray-Icon schnell stummzuschalten oder zu aktivieren. Es zeigt den aktuellen Mikrofonzustand (an/aus) über ein Tray-Icon an und speichert diesen Zustand in einer Konfigurationsdatei.

![mic-on-off](https://github.com/user-attachments/assets/5277a8af-3598-4b3c-a46c-df598fce5b6c)
<img width="446" height="338" alt="image" src="https://github.com/user-attachments/assets/c195ac02-ad89-4624-aace-cc45933e4851" />


---

## Neu in v2.1.0 (Wichtigste Änderungen)
- **Globaler, benutzerdefinierter Hotkey**: Lege einen Hotkey direkt in den Einstellungen fest (z. B. `Ctrl+Shift+M`).
- **Einstellungsdialog (Settings)**: UI zur Konfiguration des Hotkeys und zur Auswahl eines Default-Startzustands (Muted / Unmuted).
- Beta: **Systemstatus-Erkennung beim Start**: Das Programm nutzt die Windows Core Audio API, um den aktuellen Mikrofonstatus beim Start zu bestimmen und das Tray-Icon korrekt zu setzen; es gibt einen Fallback auf gespeicherte Einstellungen.

---

## Funktionen
- System-Tray-Icon: Zeigt an, ob das Mikrofon eingeschaltet oder stummgeschaltet ist.  
- Ein-Klick-Steuerung: Stummschalten oder Aktivieren des Mikrofons per Doppelklick auf das Tray-Icon oder über das Kontextmenü.  
- Globaler Hotkey: Programmweit über eine definierbare Tastenkombination.
- Default-State beim Start: Option zum automatischen Setzen des Mikrofonzustandes beim Programmstart.
- Automatische Zustandsspeicherung: Der Mikrofonzustand und Einstellungen werden in einer Konfigurationsdatei gespeichert. 

## Voraussetzungen
- Windows (getestet unter Windows 10/11; kompatibel ab Windows Vista).
- .NET Framework 4.0 oder höher.
- Zwei Icon-Dateien: `mic_on.ico` und `mic_off.ico` (müssen im gleichen Verzeichnis wie die ausführbare Datei liegen).

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
   - Wird der Zustand nicht korrekt angezeigt, zum Beispiel gründes Icon aber das Mikrofon ist ausgeschaltet:
     - Klicke mit der rechten Maustaste auf das Tray-Icon
     - Wähle "Settings"
     - Setze einen Haken bei "Set microphone to default state on Startup" und wähle den Standardwert aus.
   - Ein Doppelklick verändert den Zustand des Icons und speichert diesen in die MicMuteConfig.txt Datei.
  
## Hinweise
- Icons: Stelle sicher, dass mic_on.ico und mic_off.ico im Verzeichnis C:\micmute\ vorhanden sind, da sie für das Tray-Icon benötigt werden.
- Konfigurationsdatei: Der Mikrofonzustand wird in **`C:\micmute\MicMuteConfig.txt`** gespeichert.

## Lizenz
Dieses Projekt steht unter der GPL-3.0 Lizenz. Du kannst den Code frei verwenden, modifizieren und verteilen, solange die Lizenzbedingungen eingehalten werden.
