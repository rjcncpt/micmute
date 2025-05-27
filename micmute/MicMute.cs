using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.IO;

[assembly: AssemblyTitle("MicMute2")]

[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyInformationalVersion("Edit date: 05/27/2025")]

[assembly: AssemblyCompanyAttribute("Source: AveYo")]
[assembly: AssemblyVersionAttribute("2019.04.06")]

namespace MicMute
{
    class Program
    {
        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.txt");

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static NotifyIcon trayIcon;
        private static bool isMuted = false;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Load state from file
            LoadMicState();

            // Set up system tray icon
            trayIcon = new NotifyIcon
            {
                Icon = isMuted ? new Icon("mic_off.ico") : new Icon("mic_on.ico"),
                Text = String.Format("MicMute: Microphone is {0}", isMuted ? "off" : "on"),
                Visible = true
            };

            // Context menu for the tray icon
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Mute/Unmute Microphone", null, ToggleMic);
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            trayIcon.ContextMenuStrip = menu;

            // Double-click on icon toggles microphone
            trayIcon.DoubleClick += ToggleMic;

            // Initialize microphone state
            UpdateTrayIcon();

            // Keep the program running
            Application.Run();
        }

        private static void ToggleMic(object sender, EventArgs e)
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                SendMessageW(h, WM_APPCOMMAND, IntPtr.Zero, (IntPtr)APPCOMMAND_MICROPHONE_VOLUME_MUTE);
                isMuted = !isMuted; // Toggle state (placeholder, later use CoreAudioAPI)
                UpdateTrayIcon();
                SaveMicState(); // Save state
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(1000, "Error", "Could not toggle microphone: " + ex.Message, ToolTipIcon.Error);
            }
        }

        private static void UpdateTrayIcon()
        {
            try
            {
                // Set icon and text based on state
                trayIcon.Icon = isMuted ? new Icon("mic_off.ico") : new Icon("mic_on.ico");
                trayIcon.Text = String.Format("MicMute: Microphone is {0}", isMuted ? "off" : "on");
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(1000, "Error", "Could not load icon: " + ex.Message, ToolTipIcon.Error);
                trayIcon.Icon = SystemIcons.Application;
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

        private static void LoadMicState()
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
                isMuted = false; // Fallback to default value
            }
        }
    }
}
