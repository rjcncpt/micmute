# MicMute 2

**Version:** v2.1.0  
**Last Updated:** 2026-02-09  
**Authors:**  
- AveYo (Original, no longer available on GitHub)  
- rjcncpt (Improvements)

MicMute is a small Windows utility that allows you to quickly mute or unmute your microphone via a system tray icon. It displays the current microphone status (on/off) and stores the state in a configuration file.

![mic-on-off](https://github.com/user-attachments/assets/5277a8af-3598-4b3c-a46c-df598fce5b6c)
<img width="446" height="338" alt="image" src="https://github.com/user-attachments/assets/da95fe9f-4037-42f6-8f20-eb9884ad1f62" />


---

## New in v2.1.0 (Key Changes)
- **Global custom hotkey**: Define a hotkey directly in the settings (e.g., `Ctrl+Shift+M`).
- **Settings dialog**: UI for configuring the hotkey and selecting a default startup state (Muted / Unmuted).
- Beta: **System state detection at startup**: The application uses the Windows Core Audio API to detect the current microphone status on launch and sets the tray icon accordingly; falls back to saved settings if necessary.

---

## Features
- System tray icon: Indicates whether the microphone is enabled or muted.  
- One-click control: Mute or enable the microphone via double-click on the tray icon or through the context menu.  
- Global hotkey: Application-wide control using a configurable key combination.  
- Default startup state: Option to automatically set the microphone state when the program starts.  
- Automatic state persistence: Microphone status and settings are stored in a configuration file.

---

## Requirements
- Windows (tested on Windows 10/11; compatible from Windows Vista).
- .NET Framework 4.0 or higher.
- Two icon files: `mic_on.ico` and `mic_off.ico` (must be located in the same directory as the executable).

---

## Installation
1. **Download**
   - Download the [ZIP file](https://github.com/rjcncpt/micmute/releases).
   - Extract the archive.
   - Copy the `micmute` folder to **`C:\`**.

2. **Provide icons**
   - Ensure the files **`mic_on.ico`** and **`mic_off.ico`** are located in **`C:\micmute\`**.
   - You can create your own icons or download free ones from sites such as IconArchive.

3. **Compile**
   - Open a Command Prompt (CMD) and run the following command to compile the code:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /out:"C:\micmute\MicMute2.exe" /target:winexe /platform:anycpu /optimize /nologo "C:\micmute\MicMute.cs"
   ```
   - This creates the executable **MicMute2.exe** in **`C:\micmute\`**.
- You may then move the folder to a location of your choice.

4. **Run**
- Start **`MicMute2.exe`** from **`C:\micmute\`**.
- The tray icon will appear in the taskbar and display the microphone state.
- If the status is incorrect (e.g., green icon but microphone is muted):
  - Right-click the tray icon
  - Select **Settings**
  - Enable **Set microphone to default state on startup** and choose the desired default state.
- Double-clicking the icon toggles the state and saves it to the **MicMuteConfig.txt** file.

---

## Notes
- Icons: Ensure `mic_on.ico` and `mic_off.ico` are present in `C:\micmute\`, as they are required for the tray icon.
- Configuration file: The microphone state is stored in **`C:\micmute\MicMuteConfig.txt`**.

---

## License
This project is licensed under the GPL-3.0 license. You are free to use, modify, and distribute the code as long as the license terms are respected.
