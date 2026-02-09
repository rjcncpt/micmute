using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.IO;

[assembly: AssemblyTitle("MicMute2")]
[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyInformationalVersion("Edit date: 01/09/2026")]
[assembly: AssemblyCompanyAttribute("Source: AveYo")]

namespace MicMute
{
    class Program
    {
        private const string Version = "v2.1.0";

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
                Text = string.Format("MicMute: Microphone is {0}", isMuted ? "off" : "on"),
                Visible = true
            };

            ContextMenuStrip menu = new ContextMenuStrip();

            muteItem = new ToolStripMenuItem("Mute Microphone");
            muteItem.Click += SetMicMutedExplicit;
            menu.Items.Add(muteItem);

            unmuteItem = new ToolStripMenuItem("Unmute Microphone");
            unmuteItem.Click += SetMicUnmutedExplicit;
            menu.Items.Add(unmuteItem);

            menu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += (s, e) => ShowSettings();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("Exit", null, (s, e) => 
            {
                if (hotkeyWindow != null)
                {
                    hotkeyWindow.Close();
                }
                Application.Exit();
            });
            
            menu.Items.Add(new ToolStripSeparator());

            var versionItem = new ToolStripMenuItem(string.Format("MicMute {0} – by rjcncpt", Version));
            versionItem.Enabled = false;
            versionItem.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            menu.Items.Add(versionItem);

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += ToggleMic;

            UpdateTrayIcon();

            hotkeyWindow = new HotkeyMessageWindow();
            RegisterGlobalHotkey();

            Application.Run();
        }

        private static void ShowSettings()
        {
            using (SettingsForm settingsForm = new SettingsForm(config))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_ID);
                    config = settingsForm.GetConfig();
                    config.Save();
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

        [StructLayout(LayoutKind.Sequential)]
        private struct IMMDeviceEnumeratorVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public GetDefaultAudioEndpointDelegate GetDefaultAudioEndpoint;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDefaultAudioEndpointDelegate(IntPtr self, int dataFlow, int role, out IntPtr device);

        [StructLayout(LayoutKind.Sequential)]
        private struct IMMDeviceVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public ActivateDelegate Activate;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ActivateDelegate(IntPtr self, ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr interfacePtr);

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
            public IntPtr SetMute;
            public GetMuteDelegate GetMute;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetMuteDelegate(IntPtr self, out int muted);

        private static void LoadMicStateFromFile()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    string state = File.ReadAllText(configFile).Trim();
                    isMuted = bool.Parse(state);
                }
            }
            catch (Exception)
            {
                isMuted = false;
            }
        }

        private static void ToggleMic(object sender, EventArgs e)
        {
            SetMicMuted(!isMuted);
        }

        public static void OnHotkeyPressed()
        {
            SetMicMuted(!isMuted);
        }

        private static void SetMicMutedExplicit(object sender, EventArgs e)
        {
            SetMicMuted(true);
        }

        private static void SetMicUnmutedExplicit(object sender, EventArgs e)
        {
            SetMicMuted(false);
        }

        private static void SetMicMuted(bool mute)
        {
            try
            {
                if (isMuted != mute)
                {
                    IntPtr h = GetForegroundWindow();
                    SendMessageW(h, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_MICROPHONE_VOLUME_MUTE);
                    isMuted = mute;
                    UpdateTrayIcon();
                    SaveMicState();
                }
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(1000, "Error", "Could not change microphone state: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private static void UpdateTrayIcon()
        {
            try
            {
                trayIcon.Icon = isMuted ? new Icon("mic_off.ico") : new Icon("mic_on.ico");
                trayIcon.Text = string.Format("MicMute: Microphone is {0}", isMuted ? "off" : "on");
                UpdateMenuStatusItems();
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(1000, "Error", "Could not load icon: " + ex.Message, ToolTipIcon.Error);
                trayIcon.Icon = SystemIcons.Application;
            }
        }

        private static void UpdateMenuStatusItems()
        {
            if (muteItem != null && unmuteItem != null)
            {
                muteItem.Checked = isMuted;
                unmuteItem.Checked = !isMuted;
            }
        }

        private static void SaveMicState()
        {
            try
            {
                File.WriteAllText(configFile, isMuted.ToString());
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(1000, "Error", "Could not save state: " + ex.Message, ToolTipIcon.Error);
            }
        }
    }

    public class Config
    {
        private static readonly string configPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteSettings.cfg");

        public bool HotkeyEnabled { get; set; }
        public Keys HotkeyKey { get; set; }
        public Keys HotkeyModifiers { get; set; }
        public bool UseDefaultState { get; set; }
        public bool DefaultMutedState { get; set; }

        public Config()
        {
            HotkeyEnabled = false;
            HotkeyKey = Keys.M;
            HotkeyModifiers = Keys.Control | Keys.Shift;
            UseDefaultState = false;
            DefaultMutedState = false;
        }

        public static Config Load()
        {
            Config config = new Config();
            try
            {
                if (File.Exists(configPath))
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string value = parts[1].Trim();

                            switch (key)
                            {
                                case "HotkeyEnabled":
                                    config.HotkeyEnabled = bool.Parse(value);
                                    break;
                                case "HotkeyKey":
                                    config.HotkeyKey = (Keys)Enum.Parse(typeof(Keys), value);
                                    break;
                                case "HotkeyModifiers":
                                    config.HotkeyModifiers = (Keys)Enum.Parse(typeof(Keys), value);
                                    break;
                                case "UseDefaultState":
                                    config.UseDefaultState = bool.Parse(value);
                                    break;
                                case "DefaultMutedState":
                                    config.DefaultMutedState = bool.Parse(value);
                                    break;
                            }
                        }
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
                string[] lines = new string[]
                {
                    "HotkeyEnabled=" + HotkeyEnabled,
                    "HotkeyKey=" + HotkeyKey,
                    "HotkeyModifiers=" + HotkeyModifiers,
                    "UseDefaultState=" + UseDefaultState,
                    "DefaultMutedState=" + DefaultMutedState
                };
                File.WriteAllLines(configPath, lines);
            }
            catch (Exception)
            {
            }
        }
    }

    public class HotkeyMessageWindow : Form
    {
        public HotkeyMessageWindow()
        {
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Minimized;
            this.Size = new Size(0, 0);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                Program.OnHotkeyPressed();
            }
            base.WndProc(ref m);
        }
    }

    public class SettingsForm : Form
    {
        private CheckBox chkEnableHotkey;
        private TextBox txtHotkey;
        private Label lblHotkeyInfo;
        private CheckBox chkUseDefaultState;
        private RadioButton rbDefaultMuted;
        private RadioButton rbDefaultUnmuted;
        private GroupBox grpDefaultState;
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
                DefaultMutedState = cfg.DefaultMutedState
            };

            currentKey = config.HotkeyKey;
            currentModifiers = config.HotkeyModifiers;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "MicMute Settings";
            this.Size = new Size(450, 340);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            GroupBox grpHotkey = new GroupBox
            {
                Text = "Global Hotkey",
                Location = new Point(15, 15),
                Size = new Size(405, 130)
            };

            chkEnableHotkey = new CheckBox
            {
                Text = "Enable global hotkey",
                Location = new Point(15, 25),
                Size = new Size(200, 20),
                Checked = config.HotkeyEnabled
            };
            chkEnableHotkey.CheckedChanged += (s, e) => 
            {
                txtHotkey.Enabled = chkEnableHotkey.Checked;
                UpdateHotkeyDisplay();
            };

            Label lblHotkey = new Label
            {
                Text = "Hotkey:",
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
                Text = "Click in the field and press your desired key combination",
                Location = new Point(15, 85),
                Size = new Size(370, 35),
                ForeColor = Color.Gray
            };

            grpHotkey.Controls.Add(chkEnableHotkey);
            grpHotkey.Controls.Add(lblHotkey);
            grpHotkey.Controls.Add(txtHotkey);
            grpHotkey.Controls.Add(lblHotkeyInfo);

            grpDefaultState = new GroupBox
            {
                Text = "Default Microphone State",
                Location = new Point(15, 155),
                Size = new Size(405, 100)
            };

            chkUseDefaultState = new CheckBox
            {
                Text = "Set microphone to default state on startup",
                Location = new Point(15, 25),
                Size = new Size(300, 20),
                Checked = config.UseDefaultState
            };
            chkUseDefaultState.CheckedChanged += (s, e) =>
            {
                rbDefaultMuted.Enabled = chkUseDefaultState.Checked;
                rbDefaultUnmuted.Enabled = chkUseDefaultState.Checked;
            };

            rbDefaultMuted = new RadioButton
            {
                Text = "Muted (microphone off)",
                Location = new Point(35, 55),
                Size = new Size(200, 20),
                Checked = config.DefaultMutedState,
                Enabled = config.UseDefaultState
            };

            rbDefaultUnmuted = new RadioButton
            {
                Text = "Unmuted (microphone on)",
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
                Location = new Point(250, 270),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(340, 270),
                Size = new Size(80, 30)
            };

            this.Controls.Add(grpHotkey);
            this.Controls.Add(grpDefaultState);
            this.Controls.Add(btnOK);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

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
                txtHotkey.Text = "Hotkey disabled";
                return;
            }

            if (currentKey == Keys.None)
            {
                txtHotkey.Text = "Click here and press a key combination...";
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
        }

        public Config GetConfig()
        {
            return config;
        }
    }
}