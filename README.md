# MicMute 2
MicMute is a small Windows tool that allows you to quickly mute or unmute the microphone via a system tray icon. It displays the current microphone state (on/off) through the tray icon and saves this state in a configuration file. The program is ideal for users looking for a simple way to control their microphone during video conferences or voice recordings.

![mic-on-off](https://github.com/user-attachments/assets/bf6d5547-9c64-44c6-81b4-b6903bdf4ce1)

## Features
- System Tray Icon: Displays whether the microphone is on or muted.
- One-Click Control: Mute or unmute the microphone with a left mouse click on the tray icon or with a right mouse click via the context menu.
- Automatic State Saving: The microphone state is saved in **`MicMuteConfig.txt`** and loaded on program startup.
- Autostart (Optional): Can be configured to start automatically on login via the Windows Task Scheduler or the Startup folder.

## Requirements
- Windows operating system (tested with Windows 10/11).
- .NET Framework 4.0 or higher.
- Two icon files: **`mic_on.ico`** and **`mic_off.ico`** (must be located in the same directory as the executable).

## Installation
1. **Download:**
   - Download the [ZIP file](https://github.com/rjcncpt/micmute/releases).
   - Extract the ZIP file.
   - Copy the **`micmute`** directory to **`C:\`**.
3. **Provide Icons:**
   - Ensure that the files **`mic_on.ico`** and **`mic_off.ico`** are present in the **`C:\micmute\`** directory. You can create your own icons or use free icons from websites like IconArchive.
4. **Compile:**
   - Open a Command Prompt (CMD) and run the following command to compile the code:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /out:"C:\micmute\MicMute2.exe" /target:winexe /platform:anycpu /optimize /nologo "C:\micmute\MicMute.cs"
   ```
   - This creates the executable file **`MicMute2.exe`** in **`C:\micmute\`**.
   - Move the folder to a location of your choice.
5. **Run:**
   - Start **`MicMute2.exe`** from **`C:\micmute\`**.
   - The tray icon appears in the taskbar and displays the microphone state.
   - The default state is **`False`**, meaning the microphone is on. Ensure that your microphone is enabled on first run.
   - A left mouse click changes the icon state and saves it to the **`MicMuteConfig.txt`** file.

## Notes
- Icons: Ensure that **`mic_on.ico`** and **`mic_off.ico`** are present in the **`C:\micmute\`** directory, as they are required for the tray icon.
- Configuration File: The microphone state is stored in **`C:\micmute\MicMuteConfig.txt`**.

## License
This project is licensed under the GPL-3.0 License. You can freely use, modify, and distribute the code as long as the license terms are followed.

## Authors
AveYo: Original developer (04/06/2019)<br>
rjcncpt: Editor (05/27/2025)
