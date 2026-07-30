# MicMute 2

**Version:** v2.3.0  
**Letzte Änderung:** 30.07.2026  
**Autoren:** 
- AveYo (Original, nicht mehr auf Github verfügbar)
- rjcncpt (Verbesserungen)

MicMute ist ein kleines Windows-Tool, das es ermöglicht, das Mikrofon per System-Tray-Icon schnell stummzuschalten oder zu aktivieren. Es zeigt den aktuellen Mikrofonzustand (an/aus) über ein Tray-Icon an und speichert diesen Zustand in einer Konfigurationsdatei.

![mic-on-off](https://github.com/user-attachments/assets/5277a8af-3598-4b3c-a46c-df598fce5b6c)

---

## Funktionen
- **System-Tray-Icon**: Zeigt an, ob das Mikrofon eingeschaltet oder stummgeschaltet ist
- **Schnellschalter im Tray-Menü**: Statuszeile sowie Haken für Push-to-Talk, Benachrichtigungen, Stumm-bei-Sperre und Autostart
- **Flexibles Klick-Verhalten**: Wähle zwischen Einfachklick oder Doppelklick zum Umschalten des Mikrofons
- **Drei globale Hotkeys**: Getrennte Tastenkombinationen für Umschalten, Stummschalten und Einschalten
- **Push-to-Talk**: Taste gedrückt halten öffnet das Mikrofon, Loslassen schaltet stumm
- **App-Profile**: Eigenes Verhalten, solange eine bestimmte Anwendung im Vordergrund ist
- **Stumm bei Sperre**: Schaltet beim Sperren von Windows stumm und stellt den vorherigen Zustand beim Entsperren wieder her
- **Tastatur-LED**: Eine Tastatur-LED kann den Stummzustand anzeigen
- **Toast-Benachrichtigungen**: Einzeln pro Ereignis konfigurierbar
- **Autostart**: Optionaler Start mit Windows
- **Protokollierung**: Ereignisse werden in `MicMuteLog.txt` geschrieben
- **Mehrsprachigkeit**: Deutsch und Englisch mit Live-Sprachwechsel in den Einstellungen
- **Default-State beim Start**: Option zum automatischen Setzen des Mikrofonzustandes beim Programmstart
- **Automatische Zustandsspeicherung**: Der Mikrofonzustand und Einstellungen werden in einer Konfigurationsdatei gespeichert

<img width="446" height="518" alt="image" src="https://github.com/user-attachments/assets/f731d14e-5d72-4672-9205-720ad44d1f86" />


---

## Voraussetzungen
- Windows (getestet unter Windows 10/11; kompatibel ab Windows Vista)
- .NET Framework 4.0 oder höher
- Zwei Icon-Dateien: `mic_on.ico` und `mic_off.ico` (müssen im gleichen Verzeichnis wie die ausführbare Datei liegen)

---

## Installation
1. **Download:**
   - Lade die [ZIP-Datei](https://github.com/rjcncpt/micmute/releases) herunter
   - Entpacke die ZIP-Datei
   - Kopiere das micmute-Verzeichnis nach **`C:\`**

2. **Icons bereitstellen:**
   - Stelle sicher, dass die Dateien **`mic_on.ico`** und **`mic_off.ico`** im Verzeichnis **`C:\micmute\`** vorhanden sind
   - Du kannst eigene Icons erstellen oder kostenlose Icons von Websites wie IconArchive verwenden

3. **Kompilieren:**
   - Öffne eine Eingabeaufforderung (CMD) und führe den folgenden Befehl aus, um den Code zu kompilieren:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /out:"C:\micmute\MicMute2.exe" /target:winexe /platform:anycpu /optimize /nologo "C:\micmute\MicMute.cs"
   ```
   - Dies erstellt die ausführbare Datei MicMute2.exe in **`C:\micmute\`**
   - Verschiebe nun den Ordner an einen Ort deiner Wahl

4. **Ausführen:**
   - Starte **`MicMute2.exe`** aus **`C:\micmute\`**
   - Das Tray-Icon erscheint in der Taskleiste und zeigt den Mikrofonzustand an
   - Der Zustand wird beim Start direkt vom Windows-Audiogerät gelesen. Das Icon stimmt also auch dann, wenn das Mikrofon von einem anderen Programm stummgeschaltet wurde
   - Klick-Verhalten, Hotkeys, Sprache und sämtliche Automatik-Optionen lassen sich über **Einstellungen** konfigurieren

---

## Konfiguration
### Einstellungen
Rechtsklick auf das Tray-Icon → **Einstellungen** öffnet den Konfigurationsdialog mit vier Registerkarten:

| Registerkarte | Inhalt |
|---|---|
| **Allgemein** | Klick-Verhalten, Sprache, Autostart, Standard-Mikrofonstatus, Protokollierung |
| **Globale Hotkeys** | Getrennte Kombinationen für Umschalten, Stummschalten und Einschalten |
| **Erweitert** | Push-to-Talk, Toast-Benachrichtigungen je Ereignis |
| **Automatik** | Stumm bei Sperre, Tastatur-LED, App-Profile |

Ein globaler Hotkey braucht mindestens Strg, Umschalt oder Alt. Einzelne Tasten werden nur für F13-F24, Pause und Rollen angenommen, weil jede andere Einzeltaste systemweit geschluckt würde. Dieselbe Kombination kann nicht zweimal vergeben werden.

### Tray-Menü
Neben Stummschalten und Aktivieren bietet das Menü eine Statuszeile sowie Haken für Push-to-Talk, Toast-Benachrichtigungen, Stumm-bei-Sperre und Autostart, dazu einen Eintrag zum Öffnen der Protokolldatei. Jeder Haken wirkt sofort und wird sofort gespeichert.

### App-Profile
Ein App-Profil legt fest, wie sich das Mikrofon verhält, solange eine bestimmte Anwendung im Vordergrund ist:

| Modus | Verhalten |
|---|---|
| `mute` | Mikrofon stumm |
| `unmute` | Mikrofon aktiv |
| `ptt` | Push-to-Talk aktiv, sonst stumm |

Profile werden in der Registerkarte *Automatik* angelegt, die die aktuell laufenden Anwendungen zur Auswahl anbietet. In der Konfigurationsdatei stehen sie in einer Zeile:

```
PROFILE_APPS=discord.exe:ptt;obs64.exe:unmute;chrome.exe:mute
```

Beim Verlassen einer Profil-Anwendung wird der vorherige Mikrofonzustand wiederhergestellt.

### Tastatur-LED
Die LED leuchtet, solange das Mikrofon stumm ist. Wählbar sind Rollen, Num oder Feststell.

> **Achtung:** damit wird nicht nur die Lampe geschaltet, sondern der echte Tastaturmodus. Rollen ist zum Beispiel in Excel funktional. Die Option ist deshalb standardmäßig aus.

### Konfigurationsdatei
Alle Einstellungen und der Mikrofonzustand werden in **`C:\micmute\MicMuteConfig.ini`** gespeichert.

| Schlüssel | Standard | Bedeutung |
|---|---|---|
| `HOTKEY_TOGGLE_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Hotkey Umschalten |
| `HOTKEY_MUTE_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Hotkey Stummschalten |
| `HOTKEY_UNMUTE_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Hotkey Einschalten |
| `PUSH_TO_TALK_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Push-to-Talk |
| `SHOW_TOAST_ON_TOGGLE` … `_PUSHTOTALK` | `False` | Benachrichtigung je Ereignis |
| `USE_DEFAULT_STATE` / `DEFAULT_MUTED_STATE` | `True` / `True` | Zustand beim Start |
| `USE_DOUBLE_CLICK` | `False` | Doppelklick statt Einfachklick |
| `LANGUAGE` | `English` | `English` oder `German` |
| `AUTOSTART_ENABLED` | `False` | Mit Windows starten |
| `LOGGING_ENABLED` | `True` | `MicMuteLog.txt` schreiben |
| `AUTO_MUTE_ON_LOCK` | `False` | Stumm beim Sperren von Windows |
| `LED_SYNC_ENABLED` / `LED_SYNC_KEY` | `False` / `Scroll` | Tastatur-LED als Stummanzeige |
| `PROFILE_APPS` | *(leer)* | App-Profile, siehe oben |
| `MUTED` | — | Zuletzt gespeicherter Mikrofonzustand |

Schlüssel, die eine Version nicht kennt, bleiben unverändert erhalten. Eine ältere Version kann die Konfiguration einer neueren damit nicht zerstören.

---

## Hinweise
- **Icons**: Stelle sicher, dass `mic_on.ico` und `mic_off.ico` neben der ausführbaren Datei liegen, da sie für das Tray-Icon benötigt werden. Fehlen sie, weicht das Programm auf ein Systemsymbol aus, statt nicht zu starten
- **Konfigurationsdatei**: `MicMuteConfig.ini`, liegt neben der ausführbaren Datei
- **Protokolldatei**: `MicMuteLog.txt`, liegt neben der ausführbaren Datei und lässt sich über das Tray-Menü öffnen
- **Sprachwechsel**: Die Sprache kann jederzeit in den Einstellungen geändert werden und wird sofort angewendet
- **Nur eine Instanz**: Ein zweiter Start bewirkt nichts, die laufende Instanz behält ihre Hotkeys und ihr Tray-Icon

---

## Lizenz
Dieses Projekt steht unter der GPL-3.0 Lizenz. Du kannst den Code frei verwenden, modifizieren und verteilen, solange die Lizenzbedingungen eingehalten werden.
