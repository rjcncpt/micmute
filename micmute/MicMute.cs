using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.IO;
using System.Linq;

[assembly: AssemblyTitle("MicMute2")]
[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyInformationalVersion("Edit date: 02/10/2026")]
[assembly: AssemblyCompanyAttribute("Source: AveYo")]

namespace MicMute
{
    class Program
    {
        private const string Version = "v2.1.1";

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9000;

        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.txt");

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid iid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

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

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            config = Config.Load();

            LoadActualMicState();

            if (config.UseDefaultState)
            {
                isMuted = config.DefaultMutedState;
                SetMicMuted(isMuted);
            }

            trayIcon = new NotifyIcon
            {
                Icon = isMuted ? new Icon("mic_off.ico") : new Icon("mic_on.ico"),
                Text = string.Format("MicMute: Microphone is {0}", isMuted ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage)),
                Visible = true
            };

            ContextMenuStrip menu = new ContextMenuStrip();

            muteItem = new ToolStripMenuItem(Translations.MuteMicrophone(config.AppLanguage));
            muteItem.Click += SetMicMutedExplicit;
            menu.Items.Add(muteItem);

            unmuteItem = new ToolStripMenuItem(Translations.UnmuteMicrophone(config.AppLanguage));
            unmuteItem.Click += SetMicUnmutedExplicit;
            menu.Items.Add(unmuteItem);

            menu.Items.Add(new ToolStripSeparator());

            settingsItem = new ToolStripMenuItem(Translations.Settings(config.AppLanguage));
            settingsItem.Click += (s, e) => ShowSettings();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            exitItem = new ToolStripMenuItem(Translations.Exit(config.AppLanguage));
            exitItem.Click += (s, e) => 
            {
                if (hotkeyWindow != null)
                {
                    hotkeyWindow.Close();
                }
                Application.Exit();
            };
            menu.Items.Add(exitItem);
            
            menu.Items.Add(new ToolStripSeparator());

            var versionItem = new ToolStripMenuItem(string.Format("MicMute {0} – by rjcncpt", Version));
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

            hotkeyWindow = new HotkeyMessageWindow();
            RegisterGlobalHotkey();

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
                    UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID);
                    
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
                    
                    RegisterGlobalHotkey();
                }
            }
        }

        private static void RegisterGlobalHotkey()
        {
            if (config.HotkeyEnabled && config.HotkeyKey != Keys.None)
            {
                uint modifiers = 0;
                if ((config.HotkeyModifiers & Keys.Control) == Keys.Control)
                    modifiers |= 0x0002;
                if ((config.HotkeyModifiers & Keys.Shift) == Keys.Shift)
                    modifiers |= 0x0004;
                if ((config.HotkeyModifiers & Keys.Alt) == Keys.Alt)
                    modifiers |= 0x0001;

                RegisterHotKey(hotkeyWindow.Handle, HOTKEY_ID, modifiers, (uint)config.HotkeyKey);
            }
        }

        private static void LoadActualMicState()
        {
            try
            {
                bool? systemMuteState = GetSystemMicrophoneMuteState();
                
                if (systemMuteState.HasValue)
                {
                    isMuted = systemMuteState.Value;
                }
                else
                {
                    LoadMicStateFromFile();
                }
            }
            catch (Exception)
            {
                LoadMicStateFromFile();
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
                if (endpointVolume != IntPtr.Zero)
                    Marshal.Release(endpointVolume);
                if (device != IntPtr.Zero)
                    Marshal.Release(device);
                if (deviceEnumerator != IntPtr.Zero)
                    Marshal.Release(deviceEnumerator);
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
                        }
                    }
                }
                catch (Exception)
                {
                    isMuted = false;
                }
            }
        }

        private static void SaveMicStateToFile()
        {
            try
            {
                string existingContent = "";
                if (File.Exists(configFile))
                {
                    existingContent = File.ReadAllText(configFile);
                }

                string[] lines = existingContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool mutedLineFound = false;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("MUTED="))
                    {
                        lines[i] = "MUTED=" + isMuted.ToString().ToUpper();
                        mutedLineFound = true;
                        break;
                    }
                }

                if (mutedLineFound)
                {
                    File.WriteAllText(configFile, string.Join(Environment.NewLine, lines));
                }
                else
                {
                    File.AppendAllText(configFile, Environment.NewLine + "MUTED=" + isMuted.ToString().ToUpper());
                }
            }
            catch (Exception)
            {
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
        }

        private static void SetMicMutedExplicit(object sender, EventArgs e)
        {
            isMuted = true;
            SetMicMuted(true);
        }

        private static void SetMicUnmutedExplicit(object sender, EventArgs e)
        {
            isMuted = false;
            SetMicMuted(false);
        }

        private static void SetMicMuted(bool muted)
        {
            isMuted = muted;
            
            IntPtr hwnd = GetForegroundWindow();
            SendMessageW(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)APPCOMMAND_MICROPHONE_VOLUME_MUTE);
            
            UpdateTrayIcon();
            SaveMicStateToFile();
        }

        private static void UpdateTrayIcon()
        {
            try
            {
                trayIcon.Icon = isMuted ? new Icon("mic_off.ico") : new Icon("mic_on.ico");
                trayIcon.Text = string.Format("MicMute: Microphone is {0}", isMuted ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage));

                muteItem.Visible = !isMuted;
                unmuteItem.Visible = isMuted;
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
                    ToggleMic(null, EventArgs.Empty);
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

        public static string GlobalHotkey(Language lang)
        {
            return lang == Language.German ? "Globaler Hotkey" : "Global Hotkey";
        }

        public static string EnableGlobalHotkey(Language lang)
        {
            return lang == Language.German ? "Globalen Hotkey aktivieren" : "Enable global hotkey";
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
    }

    public class Config
    {
        public bool HotkeyEnabled { get; set; }
        public Keys HotkeyKey { get; set; }
        public Keys HotkeyModifiers { get; set; }
        public bool UseDefaultState { get; set; }
        public bool DefaultMutedState { get; set; }
        public bool UseDoubleClick { get; set; }
        public Language AppLanguage { get; set; }

        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.txt");

        public Config()
        {
            HotkeyEnabled = true;
            HotkeyKey = Keys.F13;
            HotkeyModifiers = Keys.None;
            UseDefaultState = false;
            DefaultMutedState = true;
            UseDoubleClick = false;
            AppLanguage = Language.English;
        }

        public static Config Load()
        {
            Config config = new Config();

            if (!File.Exists(configFile))
            {
                return config;
            }

            try
            {
                string[] lines = File.ReadAllLines(configFile);
                foreach (string line in lines)
                {
                    if (line.StartsWith("HOTKEY_ENABLED="))
                    {
                        bool enabled;
                        bool.TryParse(line.Substring(15), out enabled);
                        config.HotkeyEnabled = enabled;
                    }
                    else if (line.StartsWith("HOTKEY_KEY="))
                    {
                        Keys key;
                        Enum.TryParse(line.Substring(11), out key);
                        config.HotkeyKey = key;
                    }
                    else if (line.StartsWith("HOTKEY_MODIFIERS="))
                    {
                        Keys modifiers;
                        Enum.TryParse(line.Substring(17), out modifiers);
                        config.HotkeyModifiers = modifiers;
                    }
                    else if (line.StartsWith("USE_DEFAULT_STATE="))
                    {
                        bool useDefault;
                        bool.TryParse(line.Substring(18), out useDefault);
                        config.UseDefaultState = useDefault;
                    }
                    else if (line.StartsWith("DEFAULT_MUTED_STATE="))
                    {
                        bool defaultMuted;
                        bool.TryParse(line.Substring(20), out defaultMuted);
                        config.DefaultMutedState = defaultMuted;
                    }
                    else if (line.StartsWith("USE_DOUBLE_CLICK="))
                    {
                        bool useDoubleClick;
                        bool.TryParse(line.Substring(17), out useDoubleClick);
                        config.UseDoubleClick = useDoubleClick;
                    }
                    else if (line.StartsWith("LANGUAGE="))
                    {
                        Language language;
                        Enum.TryParse(line.Substring(9), out language);
                        config.AppLanguage = language;
                    }
                }
            }
            catch (Exception)
            {
            }

            return config;
        }

        public void Save()
        {
            try
            {
                string content = string.Format(
                    "HOTKEY_ENABLED={0}{1}HOTKEY_KEY={2}{1}HOTKEY_MODIFIERS={3}{1}USE_DEFAULT_STATE={4}{1}DEFAULT_MUTED_STATE={5}{1}USE_DOUBLE_CLICK={6}{1}LANGUAGE={7}",
                    HotkeyEnabled,
                    Environment.NewLine,
                    HotkeyKey,
                    HotkeyModifiers,
                    UseDefaultState,
                    DefaultMutedState,
                    UseDoubleClick,
                    AppLanguage
                );

                string existingContent = "";
                if (File.Exists(configFile))
                {
                    existingContent = File.ReadAllText(configFile);
                }

                if (existingContent.Contains("MUTED="))
                {
                    string[] lines = existingContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    string mutedLine = lines.FirstOrDefault(l => l.StartsWith("MUTED="));
                    if (mutedLine != null)
                    {
                        content += Environment.NewLine + mutedLine;
                    }
                }

                File.WriteAllText(configFile, content);
            }
            catch (Exception)
            {
            }
        }
    }

    public class SettingsForm : Form
    {
        private CheckBox chkEnableHotkey;
        private TextBox txtHotkey;
        private Label lblHotkey;
        private Label lblHotkeyInfo;
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
        private GroupBox grpHotkey;
        private Button btnOK;
        private Button btnCancel;
        private Config config;
        private Keys currentKey = Keys.None;
        private Keys currentModifiers = Keys.None;

        public SettingsForm(Config cfg)
        {
            this.config = new Config
            {
                HotkeyEnabled = cfg.HotkeyEnabled,
                HotkeyKey = cfg.HotkeyKey,
                HotkeyModifiers = cfg.HotkeyModifiers,
                UseDefaultState = cfg.UseDefaultState,
                DefaultMutedState = cfg.DefaultMutedState,
                UseDoubleClick = cfg.UseDoubleClick,
                AppLanguage = cfg.AppLanguage
            };

            currentKey = config.HotkeyKey;
            currentModifiers = config.HotkeyModifiers;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = Translations.SettingsTitle(config.AppLanguage);
            this.Size = new Size(450, 520);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            grpHotkey = new GroupBox
            {
                Text = Translations.GlobalHotkey(config.AppLanguage),
                Location = new Point(15, 15),
                Size = new Size(405, 130)
            };

            chkEnableHotkey = new CheckBox
            {
                Text = Translations.EnableGlobalHotkey(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(300, 20),
                Checked = config.HotkeyEnabled
            };
            chkEnableHotkey.CheckedChanged += (s, e) => 
            {
                txtHotkey.Enabled = chkEnableHotkey.Checked;
                UpdateHotkeyDisplay();
            };

            lblHotkey = new Label
            {
                Text = Translations.Hotkey(config.AppLanguage),
                Location = new Point(15, 55),
                Size = new Size(60, 20)
            };

            txtHotkey = new TextBox
            {
                Location = new Point(80, 52),
                Size = new Size(300, 23),
                ReadOnly = true,
                Enabled = config.HotkeyEnabled
            };
            txtHotkey.KeyDown += TxtHotkey_KeyDown;
            txtHotkey.PreviewKeyDown += TxtHotkey_PreviewKeyDown;

            lblHotkeyInfo = new Label
            {
                Text = Translations.HotkeyInfo(config.AppLanguage),
                Location = new Point(15, 85),
                Size = new Size(370, 35),
                ForeColor = Color.Gray
            };

            grpHotkey.Controls.Add(chkEnableHotkey);
            grpHotkey.Controls.Add(lblHotkey);
            grpHotkey.Controls.Add(txtHotkey);
            grpHotkey.Controls.Add(lblHotkeyInfo);

            grpClickBehavior = new GroupBox
            {
                Text = Translations.TrayIconClickBehavior(config.AppLanguage),
                Location = new Point(15, 155),
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
                Location = new Point(15, 235),
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

            rbEnglish.CheckedChanged += (s, e) => { if (rbEnglish.Checked) UpdateLanguage(Language.English); };
            rbGerman.CheckedChanged += (s, e) => { if (rbGerman.Checked) UpdateLanguage(Language.German); };

            grpLanguage.Controls.Add(rbEnglish);
            grpLanguage.Controls.Add(rbGerman);

            grpDefaultState = new GroupBox
            {
                Text = Translations.DefaultMicrophoneState(config.AppLanguage),
                Location = new Point(15, 315),
                Size = new Size(405, 100)
            };

            chkUseDefaultState = new CheckBox
            {
                Text = Translations.SetMicrophoneDefaultState(config.AppLanguage),
                Location = new Point(15, 25),
                Size = new Size(370, 20),
                Checked = config.UseDefaultState
            };
            chkUseDefaultState.CheckedChanged += (s, e) =>
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

            btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(250, 430),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(340, 430),
                Size = new Size(80, 30)
            };

            this.Controls.Add(grpHotkey);
            this.Controls.Add(grpClickBehavior);
            this.Controls.Add(grpLanguage);
            this.Controls.Add(grpDefaultState);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            UpdateHotkeyDisplay();
        }

        private void UpdateLanguage(Language newLanguage)
        {
            config.AppLanguage = newLanguage;

            this.Text = Translations.SettingsTitle(newLanguage);
            
            grpHotkey.Text = Translations.GlobalHotkey(newLanguage);
            chkEnableHotkey.Text = Translations.EnableGlobalHotkey(newLanguage);
            lblHotkey.Text = Translations.Hotkey(newLanguage);
            lblHotkeyInfo.Text = Translations.HotkeyInfo(newLanguage);

            grpClickBehavior.Text = Translations.TrayIconClickBehavior(newLanguage);
            rbSingleClick.Text = Translations.SingleClickToggle(newLanguage);
            rbDoubleClick.Text = Translations.DoubleClickToggle(newLanguage);

            grpLanguage.Text = Translations.LanguageSettings(newLanguage);
            rbEnglish.Text = Translations.English(newLanguage);
            rbGerman.Text = Translations.German(newLanguage);

            grpDefaultState.Text = Translations.DefaultMicrophoneState(newLanguage);
            chkUseDefaultState.Text = Translations.SetMicrophoneDefaultState(newLanguage);
            rbDefaultMuted.Text = Translations.MutedMicrophoneOff(newLanguage);
            rbDefaultUnmuted.Text = Translations.UnmutedMicrophoneOn(newLanguage);

            UpdateHotkeyDisplay();
        }

        private void TxtHotkey_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            e.IsInputKey = true;
        }

        private void TxtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            Keys key = e.KeyCode;

            if (key == Keys.Back || key == Keys.Delete)
            {
                currentKey = Keys.None;
                currentModifiers = Keys.None;
                UpdateHotkeyDisplay();
                return;
            }

            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu)
            {
                return;
            }

            currentKey = key;
            currentModifiers = Keys.None;

            if (e.Control)
                currentModifiers |= Keys.Control;
            if (e.Shift)
                currentModifiers |= Keys.Shift;
            if (e.Alt)
                currentModifiers |= Keys.Alt;

            UpdateHotkeyDisplay();
        }

        private void UpdateHotkeyDisplay()
        {
            if (!chkEnableHotkey.Checked)
            {
                txtHotkey.Text = Translations.HotkeyDisabled(config.AppLanguage);
                return;
            }

            if (currentKey == Keys.None)
            {
                txtHotkey.Text = Translations.ClickHerePress(config.AppLanguage);
                return;
            }

            string hotkeyText = "";
            if ((currentModifiers & Keys.Control) == Keys.Control)
                hotkeyText += "Ctrl + ";
            if ((currentModifiers & Keys.Shift) == Keys.Shift)
                hotkeyText += "Shift + ";
            if ((currentModifiers & Keys.Alt) == Keys.Alt)
                hotkeyText += "Alt + ";

            hotkeyText += currentKey.ToString();
            txtHotkey.Text = hotkeyText;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            config.HotkeyEnabled = chkEnableHotkey.Checked;
            config.HotkeyKey = currentKey;
            config.HotkeyModifiers = currentModifiers;
            config.UseDefaultState = chkUseDefaultState.Checked;
            config.DefaultMutedState = rbDefaultMuted.Checked;
            config.UseDoubleClick = rbDoubleClick.Checked;
            config.AppLanguage = rbGerman.Checked ? Language.German : Language.English;
        }

        public Config GetConfig()
        {
            return config;
        }
    }
}