# MicMute 2

**Version:** v2.3.0  
**Last Updated:** 2026-07-30  
**Authors:**  
- AveYo (Original, no longer available on GitHub)  
- rjcncpt (Improvements)

MicMute is a small Windows utility that allows you to quickly mute or unmute your microphone via a system tray icon. It displays the current microphone status (on/off) and stores the state in a configuration file.

![mic-on-off](https://github.com/user-attachments/assets/5277a8af-3598-4b3c-a46c-df598fce5b6c)

---

## Features
- **System tray icon**: Indicates whether the microphone is enabled or muted
- **Quick switches in the tray menu**: Status line plus one-click toggles for push-to-talk, notifications, mute-on-lock and autostart
- **Flexible click behavior**: Choose between single-click or double-click to toggle the microphone
- **Three global hotkeys**: Separate combinations for toggle, mute and unmute
- **Push-to-talk**: Hold a key to open the microphone, release to mute
- **App profiles**: Per-application behaviour while that application is in the foreground
- **Mute on lock**: Mutes on Windows lock and restores the previous state on unlock
- **Keyboard LED**: A keyboard LED can indicate the muted state
- **Toast notifications**: Individually configurable per event
- **Autostart**: Optional start with Windows
- **Logging**: Events are written to `MicMuteLog.txt`
- **Multi-language support**: German and English with live language switching in settings
- **Default startup state**: Option to automatically set the microphone state when the program starts
- **Automatic state persistence**: Microphone status and settings are stored in a configuration file

<img width="446" height="518" alt="image" src="https://github.com/user-attachments/assets/3c3ac07e-8555-4a56-a594-c3b4286fee0e" />

---

## Requirements
- Windows (tested on Windows 10/11; compatible from Windows Vista)
- .NET Framework 4.0 or higher
- Two icon files: `mic_on.ico` and `mic_off.ico` (must be located in the same directory as the executable)

---

## Installation
1. **Download**
   - Download the [ZIP file](https://github.com/rjcncpt/micmute/releases)
   - Extract the archive
   - Copy the `micmute` folder to **`C:\`**

2. **Provide icons**
   - Ensure the files **`mic_on.ico`** and **`mic_off.ico`** are located in **`C:\micmute\`**
   - You can create your own icons or download free ones from sites such as IconArchive

3. **Compile**
   - Open a Command Prompt (CMD) and run the following command to compile the code:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /out:"C:\micmute\MicMute2.exe" /target:winexe /platform:anycpu /optimize /nologo "C:\micmute\MicMute.cs"
   ```
   - This creates the executable **MicMute2.exe** in **`C:\micmute\`**
   - You may then move the folder to a location of your choice

4. **Run**
   - Start **`MicMute2.exe`** from **`C:\micmute\`**
   - The tray icon will appear in the taskbar and display the microphone state
   - The state is read directly from the Windows audio device on startup, so the icon matches reality even if the microphone was muted by another application
   - Click behavior, hotkeys, language and all automation options can be configured via **Settings**

---

## Configuration
### Settings
Right-click the tray icon → **Settings** opens the configuration dialog with four tabs:

| Tab | Contents |
|---|---|
| **General** | Click behaviour, language, autostart, default microphone state, logging |
| **Global Hotkeys** | Separate combinations for toggle, mute and unmute |
| **Advanced** | Push-to-talk, toast notifications per event |
| **Automation** | Mute on lock, keyboard LED, app profiles |

A global hotkey requires at least Ctrl, Shift or Alt. Standalone keys are only accepted for F13-F24, Pause and Scroll Lock, because any other single key would be swallowed system-wide. The same combination cannot be assigned to two actions.

### Tray menu
Beyond mute/unmute the menu offers a status line and quick switches for push-to-talk, toast notifications, mute-on-lock and autostart, plus an entry to open the log file. Each switch takes effect and is saved immediately.

### App profiles
An app profile defines how the microphone behaves while a given application is in the foreground:

| Mode | Behaviour |
|---|---|
| `mute` | Microphone muted |
| `unmute` | Microphone active |
| `ptt` | Push-to-talk active, muted otherwise |

Profiles are added in the *Automation* tab, which offers the currently running applications for selection. In the configuration file they are stored as a single line:

```
PROFILE_APPS=discord.exe:ptt;obs64.exe:unmute;chrome.exe:mute
```

Leaving a profiled application restores the previous microphone state.

### Keyboard LED
The LED lights up while the microphone is muted. Scroll Lock, Num Lock or Caps Lock can be selected.

> **Note:** this also toggles the actual keyboard mode, not just the light. Scroll Lock is functional in Excel, for example. The option is therefore disabled by default.

### Configuration file
All settings and the microphone state are stored in **`C:\micmute\MicMuteConfig.ini`**.

| Key | Default | Meaning |
|---|---|---|
| `HOTKEY_TOGGLE_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Toggle hotkey |
| `HOTKEY_MUTE_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Mute hotkey |
| `HOTKEY_UNMUTE_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Unmute hotkey |
| `PUSH_TO_TALK_ENABLED` / `_KEY` / `_MODIFIERS` | `False` / `None` / `None` | Push-to-talk |
| `SHOW_TOAST_ON_TOGGLE` … `_PUSHTOTALK` | `False` | Notification per event |
| `USE_DEFAULT_STATE` / `DEFAULT_MUTED_STATE` | `True` / `True` | State on startup |
| `USE_DOUBLE_CLICK` | `False` | Double-click instead of single-click |
| `LANGUAGE` | `English` | `English` or `German` |
| `AUTOSTART_ENABLED` | `False` | Start with Windows |
| `LOGGING_ENABLED` | `True` | Write `MicMuteLog.txt` |
| `AUTO_MUTE_ON_LOCK` | `False` | Mute on Windows lock |
| `LED_SYNC_ENABLED` / `LED_SYNC_KEY` | `False` / `Scroll` | Keyboard LED as mute indicator |
| `PROFILE_APPS` | *(empty)* | App profiles, see above |
| `MUTED` | — | Last microphone state |

Keys that a given version does not recognise are preserved unchanged, so an older build cannot destroy the configuration of a newer one.

---

## Notes
- **Icons**: Ensure `mic_on.ico` and `mic_off.ico` are present next to the executable, as they are required for the tray icon. If they are missing, the application falls back to a system icon instead of failing to start
- **Configuration file**: `MicMuteConfig.ini`, stored next to the executable
- **Log file**: `MicMuteLog.txt`, stored next to the executable, can be opened from the tray menu
- **Language switching**: The language can be changed at any time in settings and is applied immediately
- **Single instance**: Starting the application a second time does nothing; the running instance keeps its hotkeys and tray icon

---

## License
This project is licensed under the GPL-3.0 license. You are free to use, modify, and distribute the code as long as the license terms are respected.
