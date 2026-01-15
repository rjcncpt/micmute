using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.IO;

[assembly: AssemblyTitle("MicMute2")]
[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyInformationalVersion("Edit date: 01/15/2026")]
[assembly: AssemblyCompanyAttribute("Source: AveYo")]

namespace MicMute
{
    class Program
    {
        private const string Version = "v2.0.5";

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.txt");

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // Windows Core Audio API
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

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Lade den tatsächlichen Mikrofonzustand
            LoadActualMicState();

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
            trayIcon.MouseUp += TrayIcon_MouseUp;

            UpdateTrayIcon();
            Application.Run();
        }

        private static void TrayIcon_MouseUp(object sender, MouseEventArgs e)
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
                    // Fallback: Lade gespeicherten Status
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
                // Erstelle Device Enumerator
                Guid clsid = CLSID_MMDeviceEnumerator;
                Guid iid = IID_IMMDeviceEnumerator;
                
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out deviceEnumerator);
                if (hr != 0 || deviceEnumerator == IntPtr.Zero)
                    return null;

                // Hole Default Capture Device
                var vtbl = (IMMDeviceEnumeratorVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(deviceEnumerator), typeof(IMMDeviceEnumeratorVtbl));
                
                hr = vtbl.GetDefaultAudioEndpoint(deviceEnumerator, 1, 0, out device); // 1=Capture, 0=Console
                if (hr != 0 || device == IntPtr.Zero)
                    return null;

                // Hole IAudioEndpointVolume Interface
                var deviceVtbl = (IMMDeviceVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(device), typeof(IMMDeviceVtbl));
                
                Guid volumeIid = IID_IAudioEndpointVolume;
                hr = deviceVtbl.Activate(device, ref volumeIid, 0, IntPtr.Zero, out endpointVolume);
                if (hr != 0 || endpointVolume == IntPtr.Zero)
                    return null;

                // Lese Mute State
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
}