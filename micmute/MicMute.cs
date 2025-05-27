using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.IO;

[assembly: AssemblyTitle("MicMute2")]

[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyInformationalVersion("Edit date: 05/27/2025")]

[assembly: AssemblyCompanyAttribute("AveYo")]
[assembly: AssemblyVersionAttribute("2019.04.06")]

namespace MicMute
{
    class Program
    {
        private const string Version = "v2.0.2";

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.txt");

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static NotifyIcon trayIcon;
        private static bool isMuted = false;

        private static ToolStripMenuItem muteItem;
        private static ToolStripMenuItem unmuteItem;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LoadMicState();

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

            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
			
            menu.Items.Add(new ToolStripSeparator());

            var versionItem = new ToolStripMenuItem(string.Format("MicMute {0} – by rjcncpt", Version));
            versionItem.Enabled = false;
            versionItem.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            menu.Items.Add(versionItem);

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += ToggleMic;

            UpdateTrayIcon();
            Application.Run();
        }

        private static void ToggleMic(object sender, EventArgs e)
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
                unmuteItem.Checked = false;

                unmuteItem.Checked = !isMuted;
                muteItem.Checked = false;
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
                isMuted = false;
            }
        }
    }
}
