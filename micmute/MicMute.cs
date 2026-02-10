using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("MicMute2")]
[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyInformationalVersion("Edit date: 02/10/2026")]
[assembly: AssemblyCompanyAttribute("Source: AveYo")]

namespace MicMute
{
    class Program
    {
        private const string Version = "v2.2.0";
        private const string AppGuid = "B16C6A92-1234-4567-8901-MicMuteAppMutex"; 

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_TOGGLE = 9000;
        private const int HOTKEY_ID_MUTE = 9001;
        private const int HOTKEY_ID_UNMUTE = 9002;
        private const int HOTKEY_ID_PUSH_TO_TALK = 9003;

        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.ini");

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private static IntPtr hookID = IntPtr.Zero;
        private static LowLevelKeyboardProc hookCallback;
        private static bool pushToTalkActive = false;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid iid, out IntPtr ppv);

        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        private static NotifyIcon trayIcon;
        private static bool isMuted = false;
        private static ToolStripMenuItem muteItem;
        private static ToolStripMenuItem unmuteItem;
        private static ToolStripMenuItem settingsItem;
        private static ToolStripMenuItem exitItem;
        private static HotkeyMessageWindow hotkeyWindow;
        private static Config config;
        
        private static Icon iconMuted;
        private static Icon iconUnmuted;

        private static void SetupPushToTalkHook()
        {
            if (!config.PushToTalkEnabled || config.PushToTalkKey == Keys.None)
                return;

            hookCallback = HookCallback;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                hookID = SetWindowsHookEx(WH_KEYBOARD_LL, hookCallback, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static void RemovePushToTalkHook()
        {
            if (hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookID);
                hookID = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && config.PushToTalkEnabled)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                if (key == config.PushToTalkKey && CheckModifiers(config.PushToTalkModifiers))
                {
                    if (isKeyDown && !pushToTalkActive)
                    {
                        pushToTalkActive = true;
                        SetMicMuted(false);
                        
                        if (config.ShowToastOnPushToTalk)
                        {
                            string statusText = Translations.MicrophoneOn(config.AppLanguage);
                            ShowNotification(string.Format("Push-to-Talk: {0}", statusText));
                        }
                    }
                    else if (isKeyUp && pushToTalkActive)
                    {
                        pushToTalkActive = false;
                        SetMicMuted(true);
                        
                        if (config.ShowToastOnPushToTalk)
                        {
                            string statusText = Translations.MicrophoneOff(config.AppLanguage);
                            ShowNotification(string.Format("Push-to-Talk: {0}", statusText));
                        }
                    }
                }
            }
            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        private static bool CheckModifiers(Keys modifiers)
        {
            bool ctrlPressed = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool shiftPressed = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            bool altPressed = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;

            bool ctrlNeeded = (modifiers & Keys.Control) == Keys.Control;
            bool shiftNeeded = (modifiers & Keys.Shift) == Keys.Shift;
            bool altNeeded = (modifiers & Keys.Alt) == Keys.Alt;

            return (ctrlPressed == ctrlNeeded) && (shiftPressed == shiftNeeded) && (altPressed == altNeeded);
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\" + AppGuid, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    RunApp();
                }
                finally
                {
                    if (trayIcon != null)
                    {
                        trayIcon.Visible = false;
                        trayIcon.Dispose();
                    }
                    
                    if (hookID != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(hookID);
                    }
                }
            }
        }

        private static void LoadIcons()
        {
            try 
            {
                iconMuted = File.Exists("mic_off.ico") ? new Icon("mic_off.ico") : SystemIcons.Shield;
            }
            catch { iconMuted = SystemIcons.Shield; }

            try
            {
                iconUnmuted = File.Exists("mic_on.ico") ? new Icon("mic_on.ico") : SystemIcons.Information;
            }
            catch { iconUnmuted = SystemIcons.Information; }
        }

        private static void RunApp()
        {
            config = Config.Load();
            LoadIcons();
            LoadActualMicState();

            if (config.UseDefaultState)
            {
                isMuted = config.DefaultMutedState;
                SetMicMuted(isMuted);
            }
            else if (isMuted)
            {
                SetMicMuted(true);
            }

            trayIcon = new NotifyIcon();
            trayIcon.Icon = isMuted ? iconMuted : iconUnmuted;
            trayIcon.Text = string.Format("MicMute: Microphone is {0}", isMuted ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage));
            trayIcon.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();

            muteItem = new ToolStripMenuItem(Translations.MuteMicrophone(config.AppLanguage));
            muteItem.Click += SetMicMutedExplicit;
            menu.Items.Add(muteItem);

            unmuteItem = new ToolStripMenuItem(Translations.UnmuteMicrophone(config.AppLanguage));
            unmuteItem.Click += SetMicUnmutedExplicit;
            menu.Items.Add(unmuteItem);

            menu.Items.Add(new ToolStripSeparator());

            settingsItem = new ToolStripMenuItem(Translations.Settings(config.AppLanguage));
            settingsItem.Click += delegate(object s, EventArgs e) { ShowSettings(); };
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            exitItem = new ToolStripMenuItem(Translations.Exit(config.AppLanguage));
            exitItem.Click += delegate(object s, EventArgs e)
            {
                if (hotkeyWindow != null) hotkeyWindow.Close();
                Application.Exit();
            };
            menu.Items.Add(exitItem);
            
            menu.Items.Add(new ToolStripSeparator());

            var versionItem = new ToolStripMenuItem(string.Format("MicMute {0}", Version));
            versionItem.Enabled = false;
            versionItem.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            menu.Items.Add(versionItem);

            trayIcon.ContextMenuStrip = menu;
            
            if (config.UseDoubleClick)
            {
                trayIcon.MouseDoubleClick += MouseDoubleClick;
                trayIcon.MouseUp += MouseUpRightClickOnly;
            }
            else
            {
                trayIcon.MouseUp += MouseUp;
            }

            UpdateTrayIcon();

            if (config.ShowToastOnStartup)
            {
                string statusText = isMuted ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage);
                ShowNotification(string.Format("MicMute gestartet - Mikrofon: {0}", statusText));
            }

            hotkeyWindow = new HotkeyMessageWindow();
            RegisterGlobalHotkeys();

            GC.KeepAlive(hookCallback);

            Application.Run();
        }

        private static void MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                trayIcon.ContextMenuStrip.Show(Cursor.Position);
            }
            else if (e.Button == MouseButtons.Left)
            {
                ToggleMic(sender, EventArgs.Empty);
            }
        }

        private static void MouseUpRightClickOnly(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                trayIcon.ContextMenuStrip.Show(Cursor.Position);
            }
        }

        private static void MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleMic(sender, EventArgs.Empty);
            }
        }

        private static void ShowSettings()
        {
            using (SettingsForm settingsForm = new SettingsForm(config))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    UnregisterAllHotkeys();
                    
                    bool previousDoubleClickSetting = config.UseDoubleClick;
                    Language previousLanguage = config.AppLanguage;
                    config = settingsForm.GetConfig();
                    config.Save();
                    
                    if (previousDoubleClickSetting != config.UseDoubleClick)
                    {
                        if (config.UseDoubleClick)
                        {
                            trayIcon.MouseUp -= MouseUp;
                            trayIcon.MouseDoubleClick += MouseDoubleClick;
                            trayIcon.MouseUp += MouseUpRightClickOnly;
                        }
                        else
                        {
                            trayIcon.MouseDoubleClick -= MouseDoubleClick;
                            trayIcon.MouseUp -= MouseUpRightClickOnly;
                            trayIcon.MouseUp += MouseUp;
                        }
                    }

                    if (previousLanguage != config.AppLanguage)
                    {
                        muteItem.Text = Translations.MuteMicrophone(config.AppLanguage);
                        unmuteItem.Text = Translations.UnmuteMicrophone(config.AppLanguage);
                        settingsItem.Text = Translations.Settings(config.AppLanguage);
                        exitItem.Text = Translations.Exit(config.AppLanguage);
                        UpdateTrayIcon();
                    }
                    
                    RegisterGlobalHotkeys();
                }
            }
        }

        private static void RegisterGlobalHotkeys()
        {
            try
            {
                if (config.HotkeyToggleEnabled && config.HotkeyToggleKey != Keys.None)
                {
                    uint modifiers = GetModifiers(config.HotkeyToggleModifiers);
                    RegisterHotKey(hotkeyWindow.Handle, HOTKEY_ID_TOGGLE, modifiers, (uint)config.HotkeyToggleKey);
                }

                if (config.HotkeyMuteEnabled && config.HotkeyMuteKey != Keys.None)
                {
                    uint modifiers = GetModifiers(config.HotkeyMuteModifiers);
                    RegisterHotKey(hotkeyWindow.Handle, HOTKEY_ID_MUTE, modifiers, (uint)config.HotkeyMuteKey);
                }

                if (config.HotkeyUnmuteEnabled && config.HotkeyUnmuteKey != Keys.None)
                {
                    uint modifiers = GetModifiers(config.HotkeyUnmuteModifiers);
                    RegisterHotKey(hotkeyWindow.Handle, HOTKEY_ID_UNMUTE, modifiers, (uint)config.HotkeyUnmuteKey);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Fehler beim Registrieren der Hotkeys: " + ex.Message);
            }

            SetupPushToTalkHook();
        }

        private static void UnregisterAllHotkeys()
        {
            try 
            {
                UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID_TOGGLE);
                UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID_MUTE);
                UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID_UNMUTE);
            } 
            catch { }
            
            RemovePushToTalkHook();
        }

        private static uint GetModifiers(Keys modifierKeys)
        {
            uint modifiers = 0;
            if ((modifierKeys & Keys.Control) == Keys.Control)
                modifiers |= 0x0002;
            if ((modifierKeys & Keys.Shift) == Keys.Shift)
                modifiers |= 0x0004;
            if ((modifierKeys & Keys.Alt) == Keys.Alt)
                modifiers |= 0x0001;
            return modifiers;
        }

        private static void LoadActualMicState()
        {
            bool stateLoadedFromSystem = false;
            
            try
            {
                bool? systemMuteState = GetSystemMicrophoneMuteState();
                
                if (systemMuteState.HasValue)
                {
                    isMuted = systemMuteState.Value;
                    stateLoadedFromSystem = true;
                }
            }
            catch (Exception)
            {
            }
            
            if (!stateLoadedFromSystem)
            {
                LoadMicStateFromFile();
            }
        }

        private static void SafeRelease(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                try { Marshal.Release(ptr); } catch { }
            }
        }

        private static bool? GetSystemMicrophoneMuteState()
        {
            IntPtr deviceEnumerator = IntPtr.Zero;
            IntPtr device = IntPtr.Zero;
            IntPtr endpointVolume = IntPtr.Zero;

            try
            {
                Guid clsid = CLSID_MMDeviceEnumerator;
                Guid iid = IID_IMMDeviceEnumerator;
                
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out deviceEnumerator);
                if (hr != 0 || deviceEnumerator == IntPtr.Zero)
                    return null;

                var vtbl = (IMMDeviceEnumeratorVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(deviceEnumerator), typeof(IMMDeviceEnumeratorVtbl));
                
                hr = vtbl.GetDefaultAudioEndpoint(deviceEnumerator, 1, 0, out device); 
                if (hr != 0 || device == IntPtr.Zero)
                    return null;

                var deviceVtbl = (IMMDeviceVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(device), typeof(IMMDeviceVtbl));
                
                Guid volumeIid = IID_IAudioEndpointVolume;
                hr = deviceVtbl.Activate(device, ref volumeIid, 0, IntPtr.Zero, out endpointVolume);
                if (hr != 0 || endpointVolume == IntPtr.Zero)
                    return null;

                var volumeVtbl = (IAudioEndpointVolumeVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(endpointVolume), typeof(IAudioEndpointVolumeVtbl));
                
                int muted;
                hr = volumeVtbl.GetMute(endpointVolume, out muted);
                if (hr != 0)
                    return null;

                return muted != 0;
            }
            catch
            {
                return null;
            }
            finally
            {
                SafeRelease(endpointVolume);
                SafeRelease(device);
                SafeRelease(deviceEnumerator);
            }
        }

        private static void LoadMicStateFromFile()
        {
            if (File.Exists(configFile))
            {
                try
                {
                    string content = File.ReadAllText(configFile);
                    if (content.Contains("MUTED="))
                    {
                        string mutedLine = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault(l => l.StartsWith("MUTED="));
                        if (mutedLine != null)
                        {
                            isMuted = mutedLine.Split('=')[1].Trim().ToUpper() == "TRUE";
                            return;
                        }
                    }
                }
                catch
                {
                    isMuted = true;
                }
            }
            else
            {
                isMuted = true;
            }
        }

        private static void SaveMicStateToFile()
        {
            if (config != null)
            {
                config.SaveWithMutedState(isMuted);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMMDeviceEnumeratorVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public GetDefaultAudioEndpointDelegate GetDefaultAudioEndpoint;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetDefaultAudioEndpointDelegate(IntPtr This, int dataFlow, int role, out IntPtr ppDevice);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMMDeviceVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public ActivateDelegate Activate;
            public IntPtr OpenPropertyStore;
            public IntPtr GetId;
            public IntPtr GetState;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int ActivateDelegate(IntPtr This, ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IAudioEndpointVolumeVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public IntPtr RegisterControlChangeNotify;
            public IntPtr UnregisterControlChangeNotify;
            public IntPtr GetChannelCount;
            public IntPtr SetMasterVolumeLevel;
            public IntPtr SetMasterVolumeLevelScalar;
            public IntPtr GetMasterVolumeLevel;
            public IntPtr GetMasterVolumeLevelScalar;
            public IntPtr SetChannelVolumeLevel;
            public IntPtr SetChannelVolumeLevelScalar;
            public IntPtr GetChannelVolumeLevel;
            public IntPtr GetChannelVolumeLevelScalar;
            public SetMuteDelegate SetMute;
            public GetMuteDelegate GetMute;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int SetMuteDelegate(IntPtr This, int bMute, ref Guid pguidEventContext);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetMuteDelegate(IntPtr This, out int pbMute);
        }

        private static void ToggleMic(object sender, EventArgs e)
        {
            isMuted = !isMuted;
            SetMicMuted(isMuted);
            
            if (config.ShowToastOnToggle)
            {
                string statusText = isMuted ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage);
                ShowNotification(string.Format("{0}: {1}", Translations.Microphone(config.AppLanguage), statusText));
            }
        }

        private static void SetMicMutedExplicit(object sender, EventArgs e)
        {
            isMuted = true;
            SetMicMuted(true);
            
            if (config.ShowToastOnMute)
            {
                string statusText = Translations.MicrophoneOff(config.AppLanguage);
                ShowNotification(string.Format("{0}: {1}", Translations.Microphone(config.AppLanguage), statusText));
            }
        }

        private static void SetMicUnmutedExplicit(object sender, EventArgs e)
        {
            isMuted = false;
            SetMicMuted(false);
            
            if (config.ShowToastOnUnmute)
            {
                string statusText = Translations.MicrophoneOn(config.AppLanguage);
                ShowNotification(string.Format("{0}: {1}", Translations.Microphone(config.AppLanguage), statusText));
            }
        }

        private static void SetMicMuted(bool muted)
        {
            isMuted = muted;
            
            bool? actualSystemState = GetSystemMicrophoneMuteState();
            
            if (!actualSystemState.HasValue || actualSystemState.Value != muted)
            {
                IntPtr hwnd = GetForegroundWindow();
                SendMessageW(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)APPCOMMAND_MICROPHONE_VOLUME_MUTE);
            }
            
            UpdateTrayIcon();
            SaveMicStateToFile();
        }

        private static void UpdateTrayIcon()
        {
            try
            {
                trayIcon.Icon = isMuted ? iconMuted : iconUnmuted;
                trayIcon.Text = string.Format("MicMute: Microphone is {0}", isMuted ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage));

                muteItem.Visible = !isMuted;
                unmuteItem.Visible = isMuted;
            }
            catch (Exception)
            {
            }
        }

        private static void ShowNotification(string message)
        {
            try
            {
                if (trayIcon != null && trayIcon.Visible)
                {
                    trayIcon.BalloonTipTitle = "MicMute";
                    trayIcon.BalloonTipText = message;
                    trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                    trayIcon.ShowBalloonTip(2000);
                }
            }
            catch (Exception)
            {
            }
        }

        private class HotkeyMessageWindow : Form
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    if (m.WParam.ToInt32() == HOTKEY_ID_TOGGLE)
                    {
                        ToggleMic(null, EventArgs.Empty);
                    }
                    else if (m.WParam.ToInt32() == HOTKEY_ID_MUTE)
                    {
                        if (!isMuted)
                        {
                            SetMicMutedExplicit(null, EventArgs.Empty);
                        }
                    }
                    else if (m.WParam.ToInt32() == HOTKEY_ID_UNMUTE)
                    {
                        if (isMuted)
                        {
                            SetMicUnmutedExplicit(null, EventArgs.Empty);
                        }
                    }
                }
                base.WndProc(ref m);
            }
        }
    }

    public enum Language
    {
        English,
        German
    }

    public static class Translations
    {
        public static string MicrophoneOn(Language lang)
        {
            return lang == Language.German ? "an" : "on";
        }

        public static string MicrophoneOff(Language lang)
        {
            return lang == Language.German ? "aus" : "off";
        }

        public static string MuteMicrophone(Language lang)
        {
            return lang == Language.German ? "Mikrofon stummschalten" : "Mute Microphone";
        }

        public static string UnmuteMicrophone(Language lang)
        {
            return lang == Language.German ? "Mikrofon aktivieren" : "Unmute Microphone";
        }

        public static string Settings(Language lang)
        {
            return lang == Language.German ? "Einstellungen" : "Settings";
        }

        public static string Exit(Language lang)
        {
            return lang == Language.German ? "Beenden" : "Exit";
        }

        public static string SettingsTitle(Language lang)
        {
            return lang == Language.German ? "MicMute Einstellungen" : "MicMute Settings";
        }

        public static string GlobalHotkeys(Language lang)
        {
            return lang == Language.German ? "Globale Hotkeys" : "Global Hotkeys";
        }

        public static string ToggleHotkey(Language lang)
        {
            return lang == Language.German ? "Umschalten" : "Toggle";
        }

        public static string MuteHotkey(Language lang)
        {
            return lang == Language.German ? "Stummschalten" : "Mute";
        }

        public static string UnmuteHotkey(Language lang)
        {
            return lang == Language.German ? "Einschalten" : "Unmute";
        }

        public static string EnableHotkey(Language lang)
        {
            return lang == Language.German ? "Aktivieren" : "Enable";
        }

        public static string Hotkey(Language lang)
        {
            return lang == Language.German ? "Hotkey:" : "Hotkey:";
        }

        public static string HotkeyInfo(Language lang)
        {
            return lang == Language.German ? "Klicken Sie in das Feld und drücken Sie die gewünschte Tastenkombination" : "Click in the field and press your desired key combination";
        }

        public static string HotkeyDisabled(Language lang)
        {
            return lang == Language.German ? "Hotkey deaktiviert" : "Hotkey disabled";
        }

        public static string ClickHerePress(Language lang)
        {
            return lang == Language.German ? "Hier klicken und Tastenkombination drücken..." : "Click here and press a key combination...";
        }

        public static string TrayIconClickBehavior(Language lang)
        {
            return lang == Language.German ? "Tray-Icon Klick-Verhalten" : "Tray Icon Click Behavior";
        }

        public static string SingleClickToggle(Language lang)
        {
            return lang == Language.German ? "Einfachklick zum Umschalten des Mikrofons" : "Single click to toggle microphone";
        }

        public static string DoubleClickToggle(Language lang)
        {
            return lang == Language.German ? "Doppelklick zum Umschalten des Mikrofons" : "Double click to toggle microphone";
        }

        public static string DefaultMicrophoneState(Language lang)
        {
            return lang == Language.German ? "Standard-Mikrofonstatus" : "Default Microphone State";
        }

        public static string SetMicrophoneDefaultState(Language lang)
        {
            return lang == Language.German ? "Mikrofon beim Start auf Standardstatus setzen" : "Set microphone to default state on startup";
        }

        public static string MutedMicrophoneOff(Language lang)
        {
            return lang == Language.German ? "Stumm (Mikrofon aus)" : "Muted (microphone off)";
        }

        public static string UnmutedMicrophoneOn(Language lang)
        {
            return lang == Language.German ? "Aktiv (Mikrofon an)" : "Unmuted (microphone on)";
        }

        public static string LanguageSettings(Language lang)
        {
            return lang == Language.German ? "Sprache" : "Language";
        }

        public static string English(Language lang)
        {
            return lang == Language.German ? "Englisch" : "English";
        }

        public static string German(Language lang)
        {
            return lang == Language.German ? "Deutsch" : "German";
        }

        public static string Autostart(Language lang)
        {
            return lang == Language.German ? "Autostart" : "Autostart";
        }

        public static string StartWithWindows(Language lang)
        {
            return lang == Language.German ? "Mit Windows starten" : "Start with Windows";
        }

        public static string AdvancedSettings(Language lang)
        {
            return lang == Language.German ? "Erweitert" : "Advanced";
        }

        public static string PushToTalk(Language lang)
        {
            return lang == Language.German ? "Push-to-Talk" : "Push-to-Talk";
        }

        public static string PushToTalkDescription(Language lang)
        {
            return lang == Language.German ? "Taste gedrückt halten = Mikrofon an, Loslassen = Mikrofon aus" : "Hold key = microphone on, release = microphone off";
        }

        public static string ToastNotifications(Language lang)
        {
            return lang == Language.German ? "Toast-Benachrichtigungen" : "Toast-Notifications";
        }

        public static string ShowToastOnToggle(Language lang)
        {
            return lang == Language.German ? "Beim Umschalten anzeigen" : "Show when toggle";
        }

        public static string ShowToastOnMute(Language lang)
        {
            return lang == Language.German ? "Beim Stummschalten anzeigen" : "Show when mute";
        }

        public static string ShowToastOnUnmute(Language lang)
        {
            return lang == Language.German ? "Beim Einschalten anzeigen" : "Show when unmute";
        }

        public static string ShowToastOnStartup(Language lang)
        {
            return lang == Language.German ? "Beim App-Start anzeigen" : "Show on app startup";
        }

        public static string ShowToastOnPushToTalk(Language lang)
        {
            return lang == Language.German ? "Bei Push-to-Talk anzeigen" : "Show when push-to-talk";
        }

        public static string Microphone(Language lang)
        {
            return lang == Language.German ? "Mikrofon" : "Microphone";
        }
    }

    public class Config
    {
        public bool HotkeyToggleEnabled { get; set; }
        public Keys HotkeyToggleKey { get; set; }
        public Keys HotkeyToggleModifiers { get; set; }
        
        public bool HotkeyMuteEnabled { get; set; }
        public Keys HotkeyMuteKey { get; set; }
        public Keys HotkeyMuteModifiers { get; set; }
        
        public bool HotkeyUnmuteEnabled { get; set; }
        public Keys HotkeyUnmuteKey { get; set; }
        public Keys HotkeyUnmuteModifiers { get; set; }
        
        public bool PushToTalkEnabled { get; set; }
        public Keys PushToTalkKey { get; set; }
        public Keys PushToTalkModifiers { get; set; }
        
        public bool ShowToastOnToggle { get; set; }
        public bool ShowToastOnMute { get; set; }
        public bool ShowToastOnUnmute { get; set; }
        public bool ShowToastOnStartup { get; set; }
        public bool ShowToastOnPushToTalk { get; set; }
        
        public bool UseDefaultState { get; set; }
        public bool DefaultMutedState { get; set; }
        public bool UseDoubleClick { get; set; }
        public Language AppLanguage { get; set; }
        public bool AutostartEnabled { get; set; }

        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.ini");

        public Config()
        {
            HotkeyToggleEnabled = false;
            HotkeyToggleKey = Keys.None;
            HotkeyToggleModifiers = Keys.None;
            
            HotkeyMuteEnabled = false;
            HotkeyMuteKey = Keys.None;
            HotkeyMuteModifiers = Keys.None;
            
            HotkeyUnmuteEnabled = false;
            HotkeyUnmuteKey = Keys.None;
            HotkeyUnmuteModifiers = Keys.None;
            
            PushToTalkEnabled = false;
            PushToTalkKey = Keys.None;
            PushToTalkModifiers = Keys.None;
            
            ShowToastOnToggle = false;
            ShowToastOnMute = false;
            ShowToastOnUnmute = false;
            ShowToastOnStartup = false;
            ShowToastOnPushToTalk = false;
            
            UseDefaultState = true;
            DefaultMutedState = true;
            UseDoubleClick = false;
            AppLanguage = Language.English;
            AutostartEnabled = false;
        }

        public static Config Load()
        {
            Config config = new Config();
            
            if (!File.Exists(configFile))
            {
                config.SaveWithMutedState(true);
                return config;
            }

            try
            {
                string[] lines = File.ReadAllLines(configFile);
                
                // Variablendeklaration vor dem Switch-Block (für .NET 4.0 Kompatibilität)
                bool bVal;
                Keys kVal;
                Language lVal;

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    int equalIndex = line.IndexOf('=');
                    if (equalIndex <= 0) continue;

                    string key = line.Substring(0, equalIndex).Trim();
                    string val = line.Substring(equalIndex + 1).Trim();

                    switch (key)
                    {
                        case "HOTKEY_TOGGLE_ENABLED": 
                            if(bool.TryParse(val, out bVal)) config.HotkeyToggleEnabled = bVal; 
                            break;
                        case "HOTKEY_TOGGLE_KEY": 
                            if(Enum.TryParse(val, out kVal)) config.HotkeyToggleKey = kVal; 
                            break;
                        case "HOTKEY_TOGGLE_MODIFIERS": 
                            if(Enum.TryParse(val, out kVal)) config.HotkeyToggleModifiers = kVal; 
                            break;
                        
                        case "HOTKEY_MUTE_ENABLED": 
                            if(bool.TryParse(val, out bVal)) config.HotkeyMuteEnabled = bVal; 
                            break;
                        case "HOTKEY_MUTE_KEY": 
                            if(Enum.TryParse(val, out kVal)) config.HotkeyMuteKey = kVal; 
                            break;
                        case "HOTKEY_MUTE_MODIFIERS": 
                            if(Enum.TryParse(val, out kVal)) config.HotkeyMuteModifiers = kVal; 
                            break;
                        
                        case "HOTKEY_UNMUTE_ENABLED": 
                            if(bool.TryParse(val, out bVal)) config.HotkeyUnmuteEnabled = bVal; 
                            break;
                        case "HOTKEY_UNMUTE_KEY": 
                            if(Enum.TryParse(val, out kVal)) config.HotkeyUnmuteKey = kVal; 
                            break;
                        case "HOTKEY_UNMUTE_MODIFIERS": 
                            if(Enum.TryParse(val, out kVal)) config.HotkeyUnmuteModifiers = kVal; 
                            break;
                        
                        case "PUSH_TO_TALK_ENABLED": 
                            if(bool.TryParse(val, out bVal)) config.PushToTalkEnabled = bVal; 
                            break;
                        case "PUSH_TO_TALK_KEY": 
                            if(Enum.TryParse(val, out kVal)) config.PushToTalkKey = kVal; 
                            break;
                        case "PUSH_TO_TALK_MODIFIERS": 
                            if(Enum.TryParse(val, out kVal)) config.PushToTalkModifiers = kVal; 
                            break;
                        
                        case "SHOW_TOAST_ON_TOGGLE": 
                            if(bool.TryParse(val, out bVal)) config.ShowToastOnToggle = bVal; 
                            break;
                        case "SHOW_TOAST_ON_MUTE": 
                            if(bool.TryParse(val, out bVal)) config.ShowToastOnMute = bVal; 
                            break;
                        case "SHOW_TOAST_ON_UNMUTE": 
                            if(bool.TryParse(val, out bVal)) config.ShowToastOnUnmute = bVal; 
                            break;
                        case "SHOW_TOAST_ON_STARTUP": 
                            if(bool.TryParse(val, out bVal)) config.ShowToastOnStartup = bVal; 
                            break;
                        case "SHOW_TOAST_ON_PUSHTOTALK": 
                            if(bool.TryParse(val, out bVal)) config.ShowToastOnPushToTalk = bVal; 
                            break;
                        
                        case "USE_DEFAULT_STATE": 
                            if(bool.TryParse(val, out bVal)) config.UseDefaultState = bVal; 
                            break;
                        case "DEFAULT_MUTED_STATE": 
                            if(bool.TryParse(val, out bVal)) config.DefaultMutedState = bVal; 
                            break;
                        case "USE_DOUBLE_CLICK": 
                            if(bool.TryParse(val, out bVal)) config.UseDoubleClick = bVal; 
                            break;
                        case "LANGUAGE": 
                            if(Enum.TryParse(val, out lVal)) config.AppLanguage = lVal; 
                            break;
                        case "AUTOSTART_ENABLED": 
                            if(bool.TryParse(val, out bVal)) config.AutostartEnabled = bVal; 
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Bei groben Fehlern werden Defaults genutzt
            }

            // Sync Autostart status with Registry logic
            bool actualAutostartStatus = GetAutostartStatus();
            if (config.AutostartEnabled != actualAutostartStatus)
            {
                config.AutostartEnabled = actualAutostartStatus;
            }

            return config;
        }

        public void Save()
        {
            // Speichert nur den aktuellen Config-Zustand, erhält aber MUTED Status wenn vorhanden
            bool currentMutedState = true;
            try 
            {
                if (File.Exists(configFile))
                {
                    string content = File.ReadAllText(configFile);
                    string mutedLine = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                              .FirstOrDefault(l => l.StartsWith("MUTED="));
                    if (mutedLine != null)
                    {
                         // Parse existing muted state to preserve it
                         bool.TryParse(mutedLine.Substring(6), out currentMutedState);
                    }
                }
            } catch { }

            SaveWithMutedState(currentMutedState);
        }

        public void SaveWithMutedState(bool isMuted)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("HOTKEY_TOGGLE_ENABLED=" + HotkeyToggleEnabled);
                sb.AppendLine("HOTKEY_TOGGLE_KEY=" + HotkeyToggleKey);
                sb.AppendLine("HOTKEY_TOGGLE_MODIFIERS=" + HotkeyToggleModifiers);
                
                sb.AppendLine("HOTKEY_MUTE_ENABLED=" + HotkeyMuteEnabled);
                sb.AppendLine("HOTKEY_MUTE_KEY=" + HotkeyMuteKey);
                sb.AppendLine("HOTKEY_MUTE_MODIFIERS=" + HotkeyMuteModifiers);
                
                sb.AppendLine("HOTKEY_UNMUTE_ENABLED=" + HotkeyUnmuteEnabled);
                sb.AppendLine("HOTKEY_UNMUTE_KEY=" + HotkeyUnmuteKey);
                sb.AppendLine("HOTKEY_UNMUTE_MODIFIERS=" + HotkeyUnmuteModifiers);
                
                sb.AppendLine("PUSH_TO_TALK_ENABLED=" + PushToTalkEnabled);
                sb.AppendLine("PUSH_TO_TALK_KEY=" + PushToTalkKey);
                sb.AppendLine("PUSH_TO_TALK_MODIFIERS=" + PushToTalkModifiers);
                
                sb.AppendLine("SHOW_TOAST_ON_TOGGLE=" + ShowToastOnToggle);
                sb.AppendLine("SHOW_TOAST_ON_MUTE=" + ShowToastOnMute);
                sb.AppendLine("SHOW_TOAST_ON_UNMUTE=" + ShowToastOnUnmute);
                sb.AppendLine("SHOW_TOAST_ON_STARTUP=" + ShowToastOnStartup);
                sb.AppendLine("SHOW_TOAST_ON_PUSHTOTALK=" + ShowToastOnPushToTalk);
                
                sb.AppendLine("USE_DEFAULT_STATE=" + UseDefaultState);
                sb.AppendLine("DEFAULT_MUTED_STATE=" + DefaultMutedState);
                sb.AppendLine("USE_DOUBLE_CLICK=" + UseDoubleClick);
                sb.AppendLine("LANGUAGE=" + AppLanguage);
                sb.AppendLine("AUTOSTART_ENABLED=" + AutostartEnabled);
                sb.AppendLine("MUTED=" + isMuted.ToString().ToUpper());

                File.WriteAllText(configFile, sb.ToString());
            }
            catch (Exception)
            {
            }
        }

        public static void SetAutostart(bool enabled)
        {
            try
            {
                string appPath = "\"" + Application.ExecutablePath + "\"";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enabled)
                        {
                            key.SetValue("MicMute2", appPath);
                        }
                        else
                        {
                            if (key.GetValue("MicMute2") != null)
                            {
                                key.DeleteValue("MicMute2");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public static bool GetAutostartStatus()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("MicMute2");
                        return value != null;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }
    }

    public class SettingsForm : Form
    {
        private TabControl tabControl;
        
        private CheckBox chkEnableToggle;
        private TextBox txtToggleHotkey;
        private Label lblToggleHotkey;
        
        private CheckBox chkEnableMute;
        private TextBox txtMuteHotkey;
        private Label lblMuteHotkey;
        
        private CheckBox chkEnableUnmute;
        private TextBox txtUnmuteHotkey;
        private Label lblUnmuteHotkey;
        
        private CheckBox chkUseDefaultState;
        private RadioButton rbDefaultMuted;
        private RadioButton rbDefaultUnmuted;
        private GroupBox grpDefaultState;
        private RadioButton rbSingleClick;
        private RadioButton rbDoubleClick;
        private GroupBox grpClickBehavior;
        private RadioButton rbEnglish;
        private RadioButton rbGerman;
        private GroupBox grpLanguage;
        private CheckBox chkAutostart;
        private GroupBox grpAutostart;
        private Button btnOK;
        private Button btnCancel;
        private Config config;
        
        private CheckBox chkEnablePushToTalk;
        private TextBox txtPushToTalkHotkey;
        private Label lblPushToTalkHotkey;
        private CheckBox chkShowToastOnToggle;
        private CheckBox chkShowToastOnMute;
        private CheckBox chkShowToastOnUnmute;
        private CheckBox chkShowToastOnStartup;
        private CheckBox chkShowToastOnPushToTalk;
        private GroupBox grpPushToTalk;
        private GroupBox grpNotifications;
        
        private Keys toggleKey = Keys.None;
        private Keys toggleModifiers = Keys.None;
        private Keys muteKey = Keys.None;
        private Keys muteModifiers = Keys.None;
        private Keys unmuteKey = Keys.None;
        private Keys unmuteModifiers = Keys.None;
        private Keys pushToTalkKey = Keys.None;
        private Keys pushToTalkModifiers = Keys.None;
        
        private TextBox activeTextBox = null;

        public SettingsForm(Config cfg)
        {
            this.config = new Config
            {
                HotkeyToggleEnabled = cfg.HotkeyToggleEnabled,
                HotkeyToggleKey = cfg.HotkeyToggleKey,
                HotkeyToggleModifiers = cfg.HotkeyToggleModifiers,
                HotkeyMuteEnabled = cfg.HotkeyMuteEnabled,
                HotkeyMuteKey = cfg.HotkeyMuteKey,
                HotkeyMuteModifiers = cfg.HotkeyMuteModifiers,
                HotkeyUnmuteEnabled = cfg.HotkeyUnmuteEnabled,
                HotkeyUnmuteKey = cfg.HotkeyUnmuteKey,
                HotkeyUnmuteModifiers = cfg.HotkeyUnmuteModifiers,
                PushToTalkEnabled = cfg.PushToTalkEnabled,
                PushToTalkKey = cfg.PushToTalkKey,
                PushToTalkModifiers = cfg.PushToTalkModifiers,
                ShowToastOnToggle = cfg.ShowToastOnToggle,
                ShowToastOnMute = cfg.ShowToastOnMute,
                ShowToastOnUnmute = cfg.ShowToastOnUnmute,
                ShowToastOnStartup = cfg.ShowToastOnStartup,
                ShowToastOnPushToTalk = cfg.ShowToastOnPushToTalk,
                UseDefaultState = cfg.UseDefaultState,
                DefaultMutedState = cfg.DefaultMutedState,
                UseDoubleClick = cfg.UseDoubleClick,
                AppLanguage = cfg.AppLanguage,
                AutostartEnabled = cfg.AutostartEnabled
            };

            toggleKey = config.HotkeyToggleKey;
            toggleModifiers = config.HotkeyToggleModifiers;
            muteKey = config.HotkeyMuteKey;
            muteModifiers = config.HotkeyMuteModifiers;
            unmuteKey = config.HotkeyUnmuteKey;
            unmuteModifiers = config.HotkeyUnmuteModifiers;
            pushToTalkKey = config.PushToTalkKey;
            pushToTalkModifiers = config.PushToTalkModifiers;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = Translations.SettingsTitle(config.AppLanguage);
            this.Size = new Size(470, 540);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            tabControl = new TabControl
            {
                Location = new Point(10, 10),
                Size = new Size(435, 430)
            };

            TabPage tabHotkeys = new TabPage(Translations.GlobalHotkeys(config.AppLanguage));
            TabPage tabGeneral = new TabPage(config.AppLanguage == Language.German ? "Allgemein" : "General");

            GroupBox grpToggle = new GroupBox
            {
                Text = Translations.ToggleHotkey(config.AppLanguage),
                Location = new Point(10, 10),
                Size = new Size(405, 100)
            };

            chkEnableToggle = new CheckBox
            {
                Text = Translations.EnableHotkey(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(150, 20),
                Checked = config.HotkeyToggleEnabled
            };
            chkEnableToggle.CheckedChanged += delegate(object s, EventArgs e) 
            {
                txtToggleHotkey.Enabled = chkEnableToggle.Checked;
                UpdateHotkeyDisplay(txtToggleHotkey, chkEnableToggle.Checked, toggleKey, toggleModifiers);
            };

            lblToggleHotkey = new Label
            {
                Text = Translations.Hotkey(config.AppLanguage),
                Location = new Point(15, 55),
                Size = new Size(60, 20)
            };

            txtToggleHotkey = new TextBox
            {
                Location = new Point(80, 52),
                Size = new Size(300, 23),
                ReadOnly = true,
                Enabled = config.HotkeyToggleEnabled,
                Tag = "toggle"
            };
            txtToggleHotkey.Enter += delegate(object s, EventArgs e) { activeTextBox = txtToggleHotkey; };
            txtToggleHotkey.KeyDown += TxtHotkey_KeyDown;
            txtToggleHotkey.PreviewKeyDown += TxtHotkey_PreviewKeyDown;

            grpToggle.Controls.Add(chkEnableToggle);
            grpToggle.Controls.Add(lblToggleHotkey);
            grpToggle.Controls.Add(txtToggleHotkey);

            GroupBox grpMute = new GroupBox
            {
                Text = Translations.MuteHotkey(config.AppLanguage),
                Location = new Point(10, 120),
                Size = new Size(405, 100)
            };

            chkEnableMute = new CheckBox
            {
                Text = Translations.EnableHotkey(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(150, 20),
                Checked = config.HotkeyMuteEnabled
            };
            chkEnableMute.CheckedChanged += delegate(object s, EventArgs e) 
            {
                txtMuteHotkey.Enabled = chkEnableMute.Checked;
                UpdateHotkeyDisplay(txtMuteHotkey, chkEnableMute.Checked, muteKey, muteModifiers);
            };

            lblMuteHotkey = new Label
            {
                Text = Translations.Hotkey(config.AppLanguage),
                Location = new Point(15, 55),
                Size = new Size(60, 20)
            };

            txtMuteHotkey = new TextBox
            {
                Location = new Point(80, 52),
                Size = new Size(300, 23),
                ReadOnly = true,
                Enabled = config.HotkeyMuteEnabled,
                Tag = "mute"
            };
            txtMuteHotkey.Enter += delegate(object s, EventArgs e) { activeTextBox = txtMuteHotkey; };
            txtMuteHotkey.KeyDown += TxtHotkey_KeyDown;
            txtMuteHotkey.PreviewKeyDown += TxtHotkey_PreviewKeyDown;

            grpMute.Controls.Add(chkEnableMute);
            grpMute.Controls.Add(lblMuteHotkey);
            grpMute.Controls.Add(txtMuteHotkey);

            GroupBox grpUnmute = new GroupBox
            {
                Text = Translations.UnmuteHotkey(config.AppLanguage),
                Location = new Point(10, 230),
                Size = new Size(405, 100)
            };

            chkEnableUnmute = new CheckBox
            {
                Text = Translations.EnableHotkey(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(150, 20),
                Checked = config.HotkeyUnmuteEnabled
            };
            chkEnableUnmute.CheckedChanged += delegate(object s, EventArgs e) 
            {
                txtUnmuteHotkey.Enabled = chkEnableUnmute.Checked;
                UpdateHotkeyDisplay(txtUnmuteHotkey, chkEnableUnmute.Checked, unmuteKey, unmuteModifiers);
            };

            lblUnmuteHotkey = new Label
            {
                Text = Translations.Hotkey(config.AppLanguage),
                Location = new Point(15, 55),
                Size = new Size(60, 20)
            };

            txtUnmuteHotkey = new TextBox
            {
                Location = new Point(80, 52),
                Size = new Size(300, 23),
                ReadOnly = true,
                Enabled = config.HotkeyUnmuteEnabled,
                Tag = "unmute"
            };
            txtUnmuteHotkey.Enter += delegate(object s, EventArgs e) { activeTextBox = txtUnmuteHotkey; };
            txtUnmuteHotkey.KeyDown += TxtHotkey_KeyDown;
            txtUnmuteHotkey.PreviewKeyDown += TxtHotkey_PreviewKeyDown;

            grpUnmute.Controls.Add(chkEnableUnmute);
            grpUnmute.Controls.Add(lblUnmuteHotkey);
            grpUnmute.Controls.Add(txtUnmuteHotkey);

            Label lblInfo = new Label
            {
                Text = Translations.HotkeyInfo(config.AppLanguage),
                Location = new Point(10, 340),
                Size = new Size(405, 40),
                ForeColor = Color.Gray
            };

            tabHotkeys.Controls.Add(grpToggle);
            tabHotkeys.Controls.Add(grpMute);
            tabHotkeys.Controls.Add(grpUnmute);
            tabHotkeys.Controls.Add(lblInfo);

            grpClickBehavior = new GroupBox
            {
                Text = Translations.TrayIconClickBehavior(config.AppLanguage),
                Location = new Point(10, 10),
                Size = new Size(405, 70)
            };

            rbSingleClick = new RadioButton
            {
                Text = Translations.SingleClickToggle(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(370, 20),
                Checked = !config.UseDoubleClick
            };

            rbDoubleClick = new RadioButton
            {
                Text = Translations.DoubleClickToggle(config.AppLanguage),
                Location = new Point(15, 45),
                Size = new Size(370, 20),
                Checked = config.UseDoubleClick
            };

            grpClickBehavior.Controls.Add(rbSingleClick);
            grpClickBehavior.Controls.Add(rbDoubleClick);

            grpLanguage = new GroupBox
            {
                Text = Translations.LanguageSettings(config.AppLanguage),
                Location = new Point(10, 90),
                Size = new Size(405, 70)
            };

            rbEnglish = new RadioButton
            {
                Text = Translations.English(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(180, 20),
                Checked = config.AppLanguage == Language.English
            };

            rbGerman = new RadioButton
            {
                Text = Translations.German(config.AppLanguage),
                Location = new Point(15, 45),
                Size = new Size(180, 20),
                Checked = config.AppLanguage == Language.German
            };

            rbEnglish.CheckedChanged += delegate(object s, EventArgs e) { if (rbEnglish.Checked) UpdateLanguage(Language.English); };
            rbGerman.CheckedChanged += delegate(object s, EventArgs e) { if (rbGerman.Checked) UpdateLanguage(Language.German); };

            grpLanguage.Controls.Add(rbEnglish);
            grpLanguage.Controls.Add(rbGerman);

            grpAutostart = new GroupBox
            {
                Text = Translations.Autostart(config.AppLanguage),
                Location = new Point(10, 170),
                Size = new Size(405, 60)
            };

            chkAutostart = new CheckBox
            {
                Text = Translations.StartWithWindows(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(370, 20),
                Checked = config.AutostartEnabled
            };

            grpAutostart.Controls.Add(chkAutostart);

            grpDefaultState = new GroupBox
            {
                Text = Translations.DefaultMicrophoneState(config.AppLanguage),
                Location = new Point(10, 240),
                Size = new Size(405, 100)
            };

            chkUseDefaultState = new CheckBox
            {
                Text = Translations.SetMicrophoneDefaultState(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(370, 20),
                Checked = config.UseDefaultState
            };
            chkUseDefaultState.CheckedChanged += delegate(object s, EventArgs e)
            {
                rbDefaultMuted.Enabled = chkUseDefaultState.Checked;
                rbDefaultUnmuted.Enabled = chkUseDefaultState.Checked;
            };

            rbDefaultMuted = new RadioButton
            {
                Text = Translations.MutedMicrophoneOff(config.AppLanguage),
                Location = new Point(35, 55),
                Size = new Size(200, 20),
                Checked = config.DefaultMutedState,
                Enabled = config.UseDefaultState
            };

            rbDefaultUnmuted = new RadioButton
            {
                Text = Translations.UnmutedMicrophoneOn(config.AppLanguage),
                Location = new Point(235, 55),
                Size = new Size(200, 20),
                Checked = !config.DefaultMutedState,
                Enabled = config.UseDefaultState
            };

            grpDefaultState.Controls.Add(chkUseDefaultState);
            grpDefaultState.Controls.Add(rbDefaultMuted);
            grpDefaultState.Controls.Add(rbDefaultUnmuted);

            tabGeneral.Controls.Add(grpClickBehavior);
            tabGeneral.Controls.Add(grpLanguage);
            tabGeneral.Controls.Add(grpAutostart);
            tabGeneral.Controls.Add(grpDefaultState);

            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabHotkeys);
            
            // Advanced Tab
            TabPage tabAdvanced = new TabPage(Translations.AdvancedSettings(config.AppLanguage));
            
            grpPushToTalk = new GroupBox
            {
                Text = Translations.PushToTalk(config.AppLanguage),
                Location = new Point(10, 10),
                Size = new Size(405, 130)
            };

            chkEnablePushToTalk = new CheckBox
            {
                Text = Translations.EnableHotkey(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(150, 20),
                Checked = config.PushToTalkEnabled
            };
            chkEnablePushToTalk.CheckedChanged += delegate(object s, EventArgs e) 
            {
                txtPushToTalkHotkey.Enabled = chkEnablePushToTalk.Checked;
                UpdateHotkeyDisplay(txtPushToTalkHotkey, chkEnablePushToTalk.Checked, pushToTalkKey, pushToTalkModifiers);
            };

            lblPushToTalkHotkey = new Label
            {
                Text = Translations.Hotkey(config.AppLanguage),
                Location = new Point(15, 55),
                Size = new Size(60, 20)
            };

            txtPushToTalkHotkey = new TextBox
            {
                Location = new Point(80, 52),
                Size = new Size(300, 23),
                ReadOnly = true,
                Enabled = config.PushToTalkEnabled,
                Tag = "pushtotalk"
            };
            txtPushToTalkHotkey.Enter += delegate(object s, EventArgs e) { activeTextBox = txtPushToTalkHotkey; };
            txtPushToTalkHotkey.KeyDown += TxtHotkey_KeyDown;
            txtPushToTalkHotkey.PreviewKeyDown += TxtHotkey_PreviewKeyDown;

            Label lblPushToTalkDesc = new Label
            {
                Text = Translations.PushToTalkDescription(config.AppLanguage),
                Location = new Point(15, 85),
                Size = new Size(375, 35),
                ForeColor = Color.Gray
            };

            grpPushToTalk.Controls.Add(chkEnablePushToTalk);
            grpPushToTalk.Controls.Add(lblPushToTalkHotkey);
            grpPushToTalk.Controls.Add(txtPushToTalkHotkey);
            grpPushToTalk.Controls.Add(lblPushToTalkDesc);

            grpNotifications = new GroupBox
            {
                Text = Translations.ToastNotifications(config.AppLanguage),
                Location = new Point(10, 150),
                Size = new Size(405, 165)
            };

            chkShowToastOnToggle = new CheckBox
            {
                Text = Translations.ShowToastOnToggle(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(370, 20),
                Checked = config.ShowToastOnToggle
            };

            chkShowToastOnMute = new CheckBox
            {
                Text = Translations.ShowToastOnMute(config.AppLanguage),
                Location = new Point(15, 50),
                Size = new Size(370, 20),
                Checked = config.ShowToastOnMute
            };

            chkShowToastOnUnmute = new CheckBox
            {
                Text = Translations.ShowToastOnUnmute(config.AppLanguage),
                Location = new Point(15, 75),
                Size = new Size(370, 20),
                Checked = config.ShowToastOnUnmute
            };

            chkShowToastOnStartup = new CheckBox
            {
                Text = Translations.ShowToastOnStartup(config.AppLanguage),
                Location = new Point(15, 100),
                Size = new Size(370, 20),
                Checked = config.ShowToastOnStartup
            };

            chkShowToastOnPushToTalk = new CheckBox
            {
                Text = Translations.ShowToastOnPushToTalk(config.AppLanguage),
                Location = new Point(15, 125),
                Size = new Size(370, 20),
                Checked = config.ShowToastOnPushToTalk
            };

            grpNotifications.Controls.Add(chkShowToastOnToggle);
            grpNotifications.Controls.Add(chkShowToastOnMute);
            grpNotifications.Controls.Add(chkShowToastOnUnmute);
            grpNotifications.Controls.Add(chkShowToastOnStartup);
            grpNotifications.Controls.Add(chkShowToastOnPushToTalk);

            tabAdvanced.Controls.Add(grpPushToTalk);
            tabAdvanced.Controls.Add(grpNotifications);
            
            tabControl.TabPages.Add(tabAdvanced);

            btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(275, 450),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(365, 450),
                Size = new Size(80, 30)
            };

            this.Controls.Add(tabControl);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            UpdateHotkeyDisplay(txtToggleHotkey, chkEnableToggle.Checked, toggleKey, toggleModifiers);
            UpdateHotkeyDisplay(txtMuteHotkey, chkEnableMute.Checked, muteKey, muteModifiers);
            UpdateHotkeyDisplay(txtUnmuteHotkey, chkEnableUnmute.Checked, unmuteKey, unmuteModifiers);
            UpdateHotkeyDisplay(txtPushToTalkHotkey, chkEnablePushToTalk.Checked, pushToTalkKey, pushToTalkModifiers);
        }

        private void UpdateLanguage(Language newLanguage)
        {
            config.AppLanguage = newLanguage;

            this.Text = Translations.SettingsTitle(newLanguage);
            
            tabControl.TabPages[0].Text = newLanguage == Language.German ? "Allgemein" : "General";
            tabControl.TabPages[1].Text = Translations.GlobalHotkeys(newLanguage);
            tabControl.TabPages[2].Text = Translations.AdvancedSettings(newLanguage);

            grpClickBehavior.Text = Translations.TrayIconClickBehavior(newLanguage);
            rbSingleClick.Text = Translations.SingleClickToggle(newLanguage);
            rbDoubleClick.Text = Translations.DoubleClickToggle(newLanguage);

            grpLanguage.Text = Translations.LanguageSettings(newLanguage);
            rbEnglish.Text = Translations.English(newLanguage);
            rbGerman.Text = Translations.German(newLanguage);

            grpAutostart.Text = Translations.Autostart(newLanguage);
            chkAutostart.Text = Translations.StartWithWindows(newLanguage);

            grpDefaultState.Text = Translations.DefaultMicrophoneState(newLanguage);
            chkUseDefaultState.Text = Translations.SetMicrophoneDefaultState(newLanguage);
            rbDefaultMuted.Text = Translations.MutedMicrophoneOff(newLanguage);
            rbDefaultUnmuted.Text = Translations.UnmutedMicrophoneOn(newLanguage);

            foreach (Control tab in tabControl.TabPages[1].Controls)
            {
                GroupBox grp = tab as GroupBox;
                if (grp != null)
                {
                    if (grp.Controls.Contains(chkEnableToggle))
                    {
                        grp.Text = Translations.ToggleHotkey(newLanguage);
                        chkEnableToggle.Text = Translations.EnableHotkey(newLanguage);
                        lblToggleHotkey.Text = Translations.Hotkey(newLanguage);
                    }
                    else if (grp.Controls.Contains(chkEnableMute))
                    {
                        grp.Text = Translations.MuteHotkey(newLanguage);
                        chkEnableMute.Text = Translations.EnableHotkey(newLanguage);
                        lblMuteHotkey.Text = Translations.Hotkey(newLanguage);
                    }
                    else if (grp.Controls.Contains(chkEnableUnmute))
                    {
                        grp.Text = Translations.UnmuteHotkey(newLanguage);
                        chkEnableUnmute.Text = Translations.EnableHotkey(newLanguage);
                        lblUnmuteHotkey.Text = Translations.Hotkey(newLanguage);
                    }
                }
                
                Label lbl = tab as Label;
                if (lbl != null)
                {
                    lbl.Text = Translations.HotkeyInfo(newLanguage);
                }
            }

            UpdateHotkeyDisplay(txtToggleHotkey, chkEnableToggle.Checked, toggleKey, toggleModifiers);
            UpdateHotkeyDisplay(txtMuteHotkey, chkEnableMute.Checked, muteKey, muteModifiers);
            UpdateHotkeyDisplay(txtUnmuteHotkey, chkEnableUnmute.Checked, unmuteKey, unmuteModifiers);
            UpdateHotkeyDisplay(txtPushToTalkHotkey, chkEnablePushToTalk.Checked, pushToTalkKey, pushToTalkModifiers);
            
            // Advanced tab translations
            grpPushToTalk.Text = Translations.PushToTalk(newLanguage);
            chkEnablePushToTalk.Text = Translations.EnableHotkey(newLanguage);
            lblPushToTalkHotkey.Text = Translations.Hotkey(newLanguage);
            
            grpNotifications.Text = Translations.ToastNotifications(newLanguage);
            chkShowToastOnToggle.Text = Translations.ShowToastOnToggle(newLanguage);
            chkShowToastOnMute.Text = Translations.ShowToastOnMute(newLanguage);
            chkShowToastOnUnmute.Text = Translations.ShowToastOnUnmute(newLanguage);
            chkShowToastOnStartup.Text = Translations.ShowToastOnStartup(newLanguage);
            chkShowToastOnPushToTalk.Text = Translations.ShowToastOnPushToTalk(newLanguage);
        }

        private void TxtHotkey_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            e.IsInputKey = true;
        }

        private void TxtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            if (activeTextBox == null) return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            Keys key = e.KeyCode;

            if (key == Keys.Back || key == Keys.Delete)
            {
                if (activeTextBox.Tag.ToString() == "toggle")
                {
                    toggleKey = Keys.None;
                    toggleModifiers = Keys.None;
                    UpdateHotkeyDisplay(txtToggleHotkey, chkEnableToggle.Checked, toggleKey, toggleModifiers);
                }
                else if (activeTextBox.Tag.ToString() == "mute")
                {
                    muteKey = Keys.None;
                    muteModifiers = Keys.None;
                    UpdateHotkeyDisplay(txtMuteHotkey, chkEnableMute.Checked, muteKey, muteModifiers);
                }
                else if (activeTextBox.Tag.ToString() == "unmute")
                {
                    unmuteKey = Keys.None;
                    unmuteModifiers = Keys.None;
                    UpdateHotkeyDisplay(txtUnmuteHotkey, chkEnableUnmute.Checked, unmuteKey, unmuteModifiers);
                }
                else if (activeTextBox.Tag.ToString() == "pushtotalk")
                {
                    pushToTalkKey = Keys.None;
                    pushToTalkModifiers = Keys.None;
                    UpdateHotkeyDisplay(txtPushToTalkHotkey, chkEnablePushToTalk.Checked, pushToTalkKey, pushToTalkModifiers);
                }
                return;
            }

            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu)
            {
                return;
            }

            Keys modifiers = Keys.None;
            if (e.Control)
                modifiers |= Keys.Control;
            if (e.Shift)
                modifiers |= Keys.Shift;
            if (e.Alt)
                modifiers |= Keys.Alt;

            if (activeTextBox.Tag.ToString() == "toggle")
            {
                toggleKey = key;
                toggleModifiers = modifiers;
                UpdateHotkeyDisplay(txtToggleHotkey, chkEnableToggle.Checked, toggleKey, toggleModifiers);
            }
            else if (activeTextBox.Tag.ToString() == "mute")
            {
                muteKey = key;
                muteModifiers = modifiers;
                UpdateHotkeyDisplay(txtMuteHotkey, chkEnableMute.Checked, muteKey, muteModifiers);
            }
            else if (activeTextBox.Tag.ToString() == "unmute")
            {
                unmuteKey = key;
                unmuteModifiers = modifiers;
                UpdateHotkeyDisplay(txtUnmuteHotkey, chkEnableUnmute.Checked, unmuteKey, unmuteModifiers);
            }
            else if (activeTextBox.Tag.ToString() == "pushtotalk")
            {
                pushToTalkKey = key;
                pushToTalkModifiers = modifiers;
                UpdateHotkeyDisplay(txtPushToTalkHotkey, chkEnablePushToTalk.Checked, pushToTalkKey, pushToTalkModifiers);
            }
        }

        private void UpdateHotkeyDisplay(TextBox textBox, bool enabled, Keys key, Keys modifiers)
        {
            if (!enabled)
            {
                textBox.Text = Translations.HotkeyDisabled(config.AppLanguage);
                return;
            }

            if (key == Keys.None)
            {
                textBox.Text = Translations.ClickHerePress(config.AppLanguage);
                return;
            }

            string hotkeyText = "";
            if ((modifiers & Keys.Control) == Keys.Control)
                hotkeyText += "Ctrl + ";
            if ((modifiers & Keys.Shift) == Keys.Shift)
                hotkeyText += "Shift + ";
            if ((modifiers & Keys.Alt) == Keys.Alt)
                hotkeyText += "Alt + ";

            hotkeyText += key.ToString();
            textBox.Text = hotkeyText;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            config.HotkeyToggleEnabled = chkEnableToggle.Checked;
            config.HotkeyToggleKey = toggleKey;
            config.HotkeyToggleModifiers = toggleModifiers;
            
            config.HotkeyMuteEnabled = chkEnableMute.Checked;
            config.HotkeyMuteKey = muteKey;
            config.HotkeyMuteModifiers = muteModifiers;
            
            config.HotkeyUnmuteEnabled = chkEnableUnmute.Checked;
            config.HotkeyUnmuteKey = unmuteKey;
            config.HotkeyUnmuteModifiers = unmuteModifiers;
            
            config.PushToTalkEnabled = chkEnablePushToTalk.Checked;
            config.PushToTalkKey = pushToTalkKey;
            config.PushToTalkModifiers = pushToTalkModifiers;
            
            config.ShowToastOnToggle = chkShowToastOnToggle.Checked;
            config.ShowToastOnMute = chkShowToastOnMute.Checked;
            config.ShowToastOnUnmute = chkShowToastOnUnmute.Checked;
            config.ShowToastOnStartup = chkShowToastOnStartup.Checked;
            config.ShowToastOnPushToTalk = chkShowToastOnPushToTalk.Checked;
            
            config.UseDefaultState = chkUseDefaultState.Checked;
            config.DefaultMutedState = rbDefaultMuted.Checked;
            config.UseDoubleClick = rbDoubleClick.Checked;
            config.AppLanguage = rbGerman.Checked ? Language.German : Language.English;
            config.AutostartEnabled = chkAutostart.Checked;
            
            Config.SetAutostart(config.AutostartEnabled);
        }

        public Config GetConfig()
        {
            return config;
        }
    }
}