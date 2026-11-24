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
using System.Collections.Generic;
using Microsoft.Win32;

[assembly: AssemblyTitle("MicMute2")]
[assembly: AssemblyDescription("Edited by rjcncpt")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]
[assembly: AssemblyInformationalVersion("2.3.0")]
[assembly: AssemblyCompanyAttribute("Source: rjcncpt")]

namespace MicMute
{
    class Program
    {
        private const string Version = "v2.3.0";
        // Benutzerbezogene Anwendung: Local genügt, Global bräuchte je nach
        // Konfiguration erhöhte Rechte und wirkte über Sitzungsgrenzen hinweg.
        private const string AppMutexName = "Local\\MicMute2-SingleInstance";

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_MICROPHONE_VOLUME_MUTE = 0x180000;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_TOGGLE = 9000;
        private const int HOTKEY_ID_MUTE = 9001;
        private const int HOTKEY_ID_UNMUTE = 9002;
        private const int HOTKEY_ID_PUSH_TO_TALK = 9003;

        private static readonly string configFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteConfig.ini");
        internal static readonly string logFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MicMuteLog.txt");

        /// <summary>
        /// Schreibt eine Zeile nach MicMuteLog.txt. Meldungen sind bewusst immer deutsch,
        /// unabhängig von der eingestellten Oberflächensprache.
        /// </summary>
        // ponytail: Append ohne Rotation. Rotation erst, wenn die Datei real stört.
        internal static void Log(string message)
        {
            if (config != null && !config.LoggingEnabled)
                return;

            try
            {
                File.AppendAllText(logFile, string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine));
            }
            catch
            {
                // Logging darf die Anwendung nie zum Absturz bringen
            }
        }

        internal static void Log(string context, Exception ex)
        {
            Log(string.Format("FEHLER in {0}: {1}", context, ex.Message));
        }

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

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // MOUSEINPUT nur für die richtige Größe der Union (x64: 40 Byte)
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        /// <summary>Nicht belegter virtueller Tastencode, verhindert das Startmenü nach Win.</summary>
        private const ushort VK_MENU_MASK = 0xE8;
        /// <summary>Kennung eigener eingeschobener Tastenereignisse (dwExtraInfo), die der Hook übergeht.</summary>
        private static readonly IntPtr InjectTag = new IntPtr(0x4D4D5554);
        private static readonly UIntPtr InjectTagU = new UIntPtr(0x4D4D5554);

        /// <summary>
        /// Spiegelt den Mute-Status auf eine Tastatur-LED: LED an bedeutet stumm.
        /// Achtung, das schaltet den echten Tastaturmodus mit - Rollen ist z.B. in
        /// Excel funktional. Deshalb standardmäßig aus.
        /// </summary>
        // ponytail: keybd_event statt Hersteller-SDK. SDK erst, wenn eine bestimmte
        // ponytail: Tastatur wirklich gefordert ist - das waere eine native DLL und
        // ponytail: wuerde den Single-File-csc-Build brechen.
        private static void SyncLed(bool muted)
        {
            if (config == null || !config.LedSyncEnabled || config.LedSyncKey == Keys.None)
                return;

            try
            {
                byte vk = (byte)config.LedSyncKey;
                bool ledOn = (GetKeyState(vk) & 1) != 0;

                if (ledOn != muted)
                {
                    // Markiert, damit der Push-to-Talk-Hook die LED-Taste nicht als Tastendruck wertet
                    keybd_event(vk, 0, 0, InjectTagU);
                    keybd_event(vk, 0, KEYEVENTF_KEYUP, InjectTagU);
                }
            }
            catch (Exception ex)
            {
                Log("SyncLed", ex);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private static IntPtr hookID = IntPtr.Zero;
        private static LowLevelKeyboardProc hookCallback;
        /// <summary>
        /// Gehaltene Push-to-Talk-Tasten mit dem Zeitpunkt (Environment.TickCount) ihres letzten
        /// Ereignisses. Mikrofon offen, solange mindestens eine gehalten wird. Alle sind gesperrt.
        /// </summary>
        private static readonly Dictionary<Keys, int> pushToTalkHeld = new Dictionary<Keys, int>();
        /// <summary>
        /// Ohne Ereignis seit dieser Zeit gilt eine gehaltene Taste als losgelassen (KeyUp verpasst).
        /// Autorepeat meldet sich spätestens nach 1 s Verzögerung, danach in kürzeren Abständen.
        /// </summary>
        private const int PushToTalkStaleMs = 1500;
        /// <summary>Während der Einstellungsdialog offen ist: kein Push-to-Talk, nichts sperren (Tastenerfassung).</summary>
        private static bool settingsOpen = false;

        private static IntPtr winEventHook = IntPtr.Zero;
        private static WinEventDelegate winEventCallback;

        /// <summary>Zustand vor dem Aktivieren eines App-Profils, null wenn keins aktiv ist.</summary>
        private static bool? stateBeforeProfile = null;
        private static string activeProfileApp = null;
        /// <summary>Push-to-Talk, das nicht aus den Einstellungen, sondern aus einem Profil kommt.</summary>
        private static bool profilePushToTalk = false;

        /// <summary>
        /// Profilregeln aus PROFILE_APPS: "discord.exe:ptt;obs64.exe:unmute;chrome.exe:mute".
        /// </summary>
        private static Dictionary<string, string> ParseProfiles()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (config == null || string.IsNullOrEmpty(config.ProfileApps))
                return result;

            foreach (string entry in config.ProfileApps.Split(';'))
            {
                if (string.IsNullOrEmpty(entry.Trim()))
                    continue;

                string[] parts = entry.Split(':');
                if (parts.Length != 2)
                    continue;

                string app = parts[0].Trim();
                string mode = parts[1].Trim().ToLowerInvariant();

                if (app.Length > 0 && (mode == "mute" || mode == "unmute" || mode == "ptt"))
                {
                    result[app] = mode;
                }
            }

            return result;
        }

        private static void SetupForegroundHook()
        {
            if (winEventHook != IntPtr.Zero)
                return;

            if (config == null || string.IsNullOrEmpty(config.ProfileApps))
                return;

            // ponytail: WinEvent-Hook statt Polling-Timer. Ein Handler, kein Scheduler.
            winEventCallback = OnForegroundChanged;
            winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, winEventCallback, 0, 0, WINEVENT_OUTOFCONTEXT);

            if (winEventHook == IntPtr.Zero)
            {
                Log("App-Profile: Vordergrund-Hook konnte nicht registriert werden");
            }
        }

        private static void RemoveForegroundHook()
        {
            if (winEventHook == IntPtr.Zero)
                return;

            UnhookWinEvent(winEventHook);
            winEventHook = IntPtr.Zero;
            winEventCallback = null;
        }

        private static string GetProcessNameOfWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return null;

            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0)
                    return null;

                using (Process p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName + ".exe";
                }
            }
            catch
            {
                // Prozess kann zwischen Ereignis und Abfrage beendet sein - kein Fehlerfall
                return null;
            }
        }

        private static void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                Dictionary<string, string> profiles = ParseProfiles();
                if (profiles.Count == 0)
                    return;

                string exe = GetProcessNameOfWindow(hwnd);

                string mode;
                if (exe != null && profiles.TryGetValue(exe, out mode))
                {
                    if (string.Equals(activeProfileApp, exe, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (!stateBeforeProfile.HasValue)
                        stateBeforeProfile = isMuted;

                    activeProfileApp = exe;
                    profilePushToTalk = (mode == "ptt");

                    Log(string.Format("App-Profil aktiv: {0} -> {1}", exe, mode));

                    // ptt startet stumm, die Taste öffnet das Mikrofon
                    PostToUi(mode != "unmute", MuteSource.Profile);
                }
                else if (activeProfileApp != null)
                {
                    bool restore = stateBeforeProfile.HasValue ? stateBeforeProfile.Value : isMuted;

                    Log(string.Format("App-Profil verlassen: {0}", activeProfileApp));

                    activeProfileApp = null;
                    profilePushToTalk = false;
                    stateBeforeProfile = null;

                    PostToUi(restore, MuteSource.Profile);
                }
            }
            catch (Exception ex)
            {
                Log("OnForegroundChanged", ex);
            }
        }

        /// <summary>Zustand vor dem Sperren, null wenn die Sitzung nicht gesperrt ist.</summary>
        private static bool? stateBeforeLock = null;
        private static bool sessionSwitchHooked = false;

        private static void SetupSessionSwitchHandler()
        {
            if (sessionSwitchHooked)
                return;

            SystemEvents.SessionSwitch += OnSessionSwitch;
            sessionSwitchHooked = true;
        }

        private static void RemoveSessionSwitchHandler()
        {
            if (!sessionSwitchHooked)
                return;

            SystemEvents.SessionSwitch -= OnSessionSwitch;
            sessionSwitchHooked = false;
        }

        private static void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (config == null || !config.AutoMuteOnLock)
                return;

            bool leaving = e.Reason == SessionSwitchReason.SessionLock
                        || e.Reason == SessionSwitchReason.SessionLogoff
                        || e.Reason == SessionSwitchReason.RemoteDisconnect
                        || e.Reason == SessionSwitchReason.ConsoleDisconnect;

            bool returning = e.Reason == SessionSwitchReason.SessionUnlock
                          || e.Reason == SessionSwitchReason.SessionLogon
                          || e.Reason == SessionSwitchReason.RemoteConnect
                          || e.Reason == SessionSwitchReason.ConsoleConnect;

            if (leaving && !stateBeforeLock.HasValue)
            {
                stateBeforeLock = isMuted;
                Log("Sitzung gesperrt - Mikrofon wird stummgeschaltet");
                PostToUi(true, MuteSource.Lock);
            }
            else if (returning && stateBeforeLock.HasValue)
            {
                bool restore = stateBeforeLock.Value;
                stateBeforeLock = null;
                Log("Sitzung entsperrt - vorheriger Zustand wird wiederhergestellt");
                PostToUi(restore, MuteSource.Lock);
            }
        }

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid iid, out IntPtr ppv);

        private const uint CLSCTX_INPROC_SERVER = 0x1;
        private const int CLSCTX_ALL = 0x17;
        private const int eCapture = 1;
        private const int eConsole = 0;
        private const int eCommunications = 2;
        private const int DEVICE_STATE_ACTIVE = 0x1;

        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        private static NotifyIcon trayIcon;
        private static bool isMuted = false;
        private static ToolStripMenuItem muteItem;
        private static ToolStripMenuItem unmuteItem;
        private static ToolStripMenuItem settingsItem;
        private static ToolStripMenuItem exitItem;
        private static ToolStripMenuItem statusItem;
        private static ToolStripMenuItem pttItem;
        private static ToolStripMenuItem toastItem;
        private static ToolStripMenuItem lockItem;
        private static ToolStripMenuItem autostartItem;
        private static ToolStripMenuItem logItem;
        private static HotkeyMessageWindow hotkeyWindow;
        private static Config config;
        
        private static Icon iconMuted;
        private static Icon iconUnmuted;

        private static void SetupPushToTalkHook()
        {
            if (hookID != IntPtr.Zero)
                return;

            if (!config.HasPushToTalkKey)
                return;

            // Der Hook wird auch gebraucht, wenn Push-to-Talk nicht global aktiv ist,
            // sondern nur von einem App-Profil angefordert wird.
            bool neededByProfile = config.ProfileApps != null && config.ProfileApps.ToLowerInvariant().Contains(":ptt");
            if (!config.PushToTalkEnabled && !neededByProfile)
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

            // Ohne Hook kommt kein Loslassen mehr an
            ReleaseHeldPushToTalkKeys();
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Dieser Callback läuft im Tastatur-Hook. Alles, was hier länger dauert,
            // verzögert systemweit jeden Tastendruck; überschreitet es
            // LowLevelHooksTimeout, entfernt Windows den Hook kommentarlos.
            // Deshalb hier nur den Zustand ermitteln und die Arbeit auf den UI-Thread posten.
            // Eigene eingeschobene Ereignisse (Maskentaste, LED-Sync) unverändert durchreichen;
            // während die Einstellungen offen sind, erreicht jede Taste das Erfassungsfeld
            if (nCode >= 0 && config != null && !settingsOpen && Marshal.ReadIntPtr(lParam, 16) != InjectTag)
            {
                Keys key = (Keys)Marshal.ReadInt32(lParam);
                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);
                bool active = (config.PushToTalkEnabled || profilePushToTalk) && config.HasPushToTalkKey;
                bool block = false;

                if (isKeyUp && pushToTalkHeld.Remove(key))
                {
                    // Loslassen ohne Modifier-Prüfung: die Copilot-Taste lässt F23 erst nach
                    // Win und Shift los, und wer Strg vor der Taste loslässt, darf das Mikrofon
                    // nicht offen zurücklassen. Bei mehreren Tasten schaltet erst die zuletzt
                    // losgelassene stumm. Auch gesperrt, wenn Push-to-Talk inzwischen aus ist.
                    block = true;
                    if (active && pushToTalkHeld.Count == 0)
                        PostToUi(true, MuteSource.PushToTalk);
                }
                else if (isKeyDown && pushToTalkHeld.ContainsKey(key))
                {
                    // Autorepeat (Copilot-Taste: ganze Folge LWin, LShift, F23 alle ~31 ms).
                    // Lange kein Ereignis: KeyUp verpasst, das ist ein neuer Druck - Mikrofon öffnen.
                    int now = Environment.TickCount;
                    RemoveStalePushToTalkKeys(key, now);
                    if (active && unchecked(now - pushToTalkHeld[key]) > PushToTalkStaleMs)
                        PostToUi(false, MuteSource.PushToTalk);
                    pushToTalkHeld[key] = now;
                    block = true;
                }
                else if (isKeyDown && active && IsPushToTalkCombo(key))
                {
                    int now = Environment.TickCount;
                    RemoveStalePushToTalkKeys(key, now);

                    pushToTalkHeld[key] = now;
                    if (pushToTalkHeld.Count == 1)
                        PostToUi(false, MuteSource.PushToTalk);

                    // Jede Push-to-Talk-Taste ist gesperrt: sie wirkt nur noch als Push-to-Talk.
                    block = true;

                    // Win (Copilot-Taste = LWin + LShift + F23), Alt, Strg oder Umschalt allein losgelassen
                    // öffnen Startmenü, Menüleiste bzw. wechseln das Tastaturlayout: unbelegte Taste einschieben.
                    if (IsKeyDownNow(Keys.LWin) || IsKeyDownNow(Keys.RWin) || IsKeyDownNow(Keys.Menu)
                        || IsKeyDownNow(Keys.ControlKey) || IsKeyDownNow(Keys.ShiftKey))
                        InjectMenuMaskKey();
                }

                // Gesperrt: weder Windows noch andere Programme sehen die Taste
                if (block)
                    return (IntPtr)1;
            }
            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        /// <summary>
        /// Reicht eine Zustandsänderung aus einem fremden Thread (Tastatur-Hook,
        /// SystemEvents, WinEvent-Hook) an den UI-Thread weiter.
        /// </summary>
        private static void PostToUi(bool muted, MuteSource source)
        {
            try
            {
                if (hotkeyWindow != null && hotkeyWindow.IsHandleCreated)
                {
                    hotkeyWindow.BeginInvoke((MethodInvoker)delegate
                    {
                        SetMicMuted(muted, source);
                    });
                }
            }
            catch (Exception ex)
            {
                Log("PostToUi", ex);
            }
        }

        /// <summary>
        /// Asynchroner Tastenzustand. Im Tastatur-Hook verlässlicher als Control.ModifierKeys,
        /// das den Zustand der eigenen Nachrichtenschlange liest.
        /// </summary>
        internal static bool IsKeyDownNow(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        private static bool IsPushToTalkCombo(Keys key)
        {
            foreach (HotkeyBinding b in config.PushToTalkKeys)
            {
                if (b.Key == key && CheckModifiers(b.Modifiers))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Verlorene KeyUps aufräumen (z. B. Sperrbildschirm). GetAsyncKeyState hilft nicht, gesperrte
        /// Tasten sieht Windows nie. Eine gehaltene Taste meldet sich per Autorepeat; ohne Ereignis seit
        /// PushToTalkStaleMs gilt sie als losgelassen.
        /// </summary>
        private static void RemoveStalePushToTalkKeys(Keys except, int now)
        {
            List<Keys> stale = pushToTalkHeld
                .Where(p => p.Key != except && unchecked(now - p.Value) > PushToTalkStaleMs)
                .Select(p => p.Key)
                .ToList();
            foreach (Keys k in stale)
                pushToTalkHeld.Remove(k);
        }

        /// <summary>Vergisst gehaltene Push-to-Talk-Tasten; hatte Push-to-Talk das Mikrofon geöffnet, wieder schließen.</summary>
        private static void ReleaseHeldPushToTalkKeys()
        {
            // PostToUi statt direktem Aufruf, damit ein noch wartendes "öffnen" aus dem Hook nicht danach läuft.
            if (pushToTalkHeld.Count > 0)
            {
                pushToTalkHeld.Clear();
                PostToUi(true, MuteSource.PushToTalk);
            }
        }

        /// <summary>
        /// Schiebt eine unbelegte Taste ein, solange Win, Alt, Strg oder Umschalt gedrückt ist.
        /// Sonst öffnet Windows beim Loslassen das Startmenü, aktiviert die Menüleiste bzw.
        /// wechselt das Tastaturlayout, weil es die gesperrte Taste nie gesehen hat.
        /// </summary>
        private static void InjectMenuMaskKey()
        {
            INPUT[] inputs = new INPUT[2];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i].type = INPUT_KEYBOARD;
                inputs[i].u.ki.wVk = VK_MENU_MASK;
                inputs[i].u.ki.dwFlags = (i == 0) ? 0u : KEYEVENTF_KEYUP;
                inputs[i].u.ki.dwExtraInfo = InjectTag;
            }

            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
                Log("Push-to-Talk: Maskentaste konnte nicht gesendet werden");
        }

        /// <summary>
        /// Prüft, ob die geforderten Modifier gedrückt sind. Zusätzlich gedrückte
        /// Modifier stören bewusst nicht - sonst bricht Push-to-Talk, sobald man
        /// nebenbei Shift oder Strg hält (Sprint-Taste in Spielen).
        /// </summary>
        private static bool CheckModifiers(Keys modifiers)
        {
            if ((modifiers & Keys.Control) == Keys.Control && !IsKeyDownNow(Keys.ControlKey))
                return false;
            if ((modifiers & Keys.Shift) == Keys.Shift && !IsKeyDownNow(Keys.ShiftKey))
                return false;
            if ((modifiers & Keys.Alt) == Keys.Alt && !IsKeyDownNow(Keys.Menu))
                return false;

            return true;
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, AppMutexName, out createdNew))
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

                    RemoveForegroundHook();

                    // SystemEvents hält sonst eine statische Referenz auf den Handler
                    RemoveSessionSwitchHandler();

                    StopMuteGuard();
                }
            }
        }

        private static void LoadIcons()
        {
            // Fix: Get the directory where the .exe is located
            string appDir = Path.GetDirectoryName(Application.ExecutablePath);
            iconMuted = LoadIconFile(appDir, "mic_off.ico", SystemIcons.Shield);
            iconUnmuted = LoadIconFile(appDir, "mic_on.ico", SystemIcons.Information);
        }

        /// <summary>
        /// Datei neben der exe hat Vorrang (eigene Icons), sonst die eingebettete
        /// Ressource (/resource beim Kompilieren), sonst das Systemsymbol.
        /// </summary>
        private static Icon LoadIconFile(string appDir, string fileName, Icon fallback)
        {
            try
            {
                string path = Path.Combine(appDir, fileName);
                if (File.Exists(path))
                    return new Icon(path);
            }
            catch (Exception ex) { Log("LoadIconFile " + fileName, ex); }

            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName))
                {
                    if (stream != null)
                        return new Icon(stream);
                }
            }
            catch (Exception ex) { Log("LoadIconFile (Ressource) " + fileName, ex); }

            return fallback;
        }

        private static void RunApp()
        {
            config = Config.Load();
            Log(string.Format("=== MicMute {0} gestartet ===", Version));
            LoadIcons();
            LoadActualMicState();

            trayIcon = new NotifyIcon();
            trayIcon.Icon = isMuted ? iconMuted : iconUnmuted;
            trayIcon.Text = Translations.TrayStatus(config.AppLanguage, isMuted);
            trayIcon.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();

            statusItem = new ToolStripMenuItem(Translations.TrayStatus(config.AppLanguage, isMuted));
            statusItem.Enabled = false;
            statusItem.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            menu.Items.Add(statusItem);

            menu.Items.Add(new ToolStripSeparator());

            muteItem = new ToolStripMenuItem(Translations.MuteMicrophone(config.AppLanguage));
            muteItem.Click += SetMicMutedExplicit;
            menu.Items.Add(muteItem);

            unmuteItem = new ToolStripMenuItem(Translations.UnmuteMicrophone(config.AppLanguage));
            unmuteItem.Click += SetMicUnmutedExplicit;
            menu.Items.Add(unmuteItem);

            menu.Items.Add(new ToolStripSeparator());

            // Schnellschalter: jeder Haken wirkt sofort und wird sofort gespeichert.
            pttItem = new ToolStripMenuItem(Translations.PushToTalk(config.AppLanguage));
            pttItem.Click += delegate(object s, EventArgs e)
            {
                config.PushToTalkEnabled = !config.PushToTalkEnabled;
                UnregisterAllHotkeys();
                RegisterGlobalHotkeys();
                SaveMicStateToFile();
                Log("Push-to-Talk " + (config.PushToTalkEnabled ? "aktiviert" : "deaktiviert"));
                SyncMenuChecks();
            };
            menu.Items.Add(pttItem);

            toastItem = new ToolStripMenuItem(Translations.ToastNotifications(config.AppLanguage));
            toastItem.Click += delegate(object s, EventArgs e)
            {
                bool enable = !AnyToastEnabled();
                config.ShowToastOnToggle = enable;
                config.ShowToastOnMute = enable;
                config.ShowToastOnUnmute = enable;
                config.ShowToastOnStartup = enable;
                config.ShowToastOnPushToTalk = enable;
                SaveMicStateToFile();
                Log("Toast-Benachrichtigungen " + (enable ? "aktiviert" : "deaktiviert"));
                SyncMenuChecks();
            };
            menu.Items.Add(toastItem);

            lockItem = new ToolStripMenuItem(Translations.AutoMuteOnLock(config.AppLanguage));
            lockItem.Click += delegate(object s, EventArgs e)
            {
                config.AutoMuteOnLock = !config.AutoMuteOnLock;
                SaveMicStateToFile();
                Log("Stumm bei Sperre " + (config.AutoMuteOnLock ? "aktiviert" : "deaktiviert"));
                SyncMenuChecks();
            };
            menu.Items.Add(lockItem);

            autostartItem = new ToolStripMenuItem(Translations.StartWithWindows(config.AppLanguage));
            autostartItem.Click += delegate(object s, EventArgs e)
            {
                config.AutostartEnabled = !config.AutostartEnabled;
                Config.SetAutostart(config.AutostartEnabled);
                SaveMicStateToFile();
                Log("Autostart " + (config.AutostartEnabled ? "aktiviert" : "deaktiviert"));
                SyncMenuChecks();
            };
            menu.Items.Add(autostartItem);

            menu.Items.Add(new ToolStripSeparator());

            logItem = new ToolStripMenuItem(Translations.OpenLog(config.AppLanguage));
            logItem.Click += delegate(object s, EventArgs e)
            {
                try
                {
                    if (File.Exists(logFile))
                    {
                        Process.Start(logFile);
                    }
                }
                catch (Exception ex)
                {
                    Log("OpenLog", ex);
                }
            };
            menu.Items.Add(logItem);

            settingsItem = new ToolStripMenuItem(Translations.Settings(config.AppLanguage));
            settingsItem.Click += delegate(object s, EventArgs e) { ShowSettings(); };
            menu.Items.Add(settingsItem);

            exitItem = new ToolStripMenuItem(Translations.Exit(config.AppLanguage));
            exitItem.Click += delegate(object s, EventArgs e)
            {
                Log("=== MicMute beendet ===");
                UnregisterAllHotkeys();
                StopMuteGuard();
                if (hotkeyWindow != null) hotkeyWindow.Close();
                Application.Exit();
            };
            menu.Items.Add(exitItem);

            menu.Items.Add(new ToolStripSeparator());

            var versionItem = new ToolStripMenuItem(string.Format("MicMute {0} – by rjcncpt", Version));
            versionItem.Enabled = false;
            versionItem.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            menu.Items.Add(versionItem);

            trayIcon.ContextMenuStrip = menu;
            SyncMenuChecks();
            
            if (config.UseDoubleClick)
            {
                trayIcon.MouseDoubleClick += MouseDoubleClick;
                trayIcon.MouseUp += MouseUpRightClickOnly;
            }
            else
            {
                trayIcon.MouseUp += MouseUp;
            }

            // Erst jetzt, weil SetMicMuted das Tray-Icon aktualisiert.
            // SetMute ist idempotent, ein Vergleich mit dem Ist-Zustand erübrigt sich.
            if (config.UseDefaultState)
            {
                SetMicMuted(config.DefaultMutedState);
            }

            UpdateTrayIcon();

            if (config.ShowToastOnStartup)
            {
                string statusText = (isMuted && !muteIncomplete) ? Translations.MicrophoneOff(config.AppLanguage) : Translations.MicrophoneOn(config.AppLanguage);
                ShowNotification(string.Format("MicMute gestartet - Mikrofon: {0}", statusText));
            }

            hotkeyWindow = new HotkeyMessageWindow();
            // Handle erzwingen: der Tastatur-Hook postet per BeginInvoke hierher,
            // auch wenn gar kein globaler Hotkey registriert wird.
            if (hotkeyWindow.Handle == IntPtr.Zero)
            {
                Log("Fensterhandle für Hotkeys konnte nicht erstellt werden");
            }

            if (!RegisterGlobalHotkeys())
            {
                ShowNotification(Translations.HotkeyRegisterFailed(config.AppLanguage));
            }

            SetupSessionSwitchHandler();

            muteGuardTimer = new System.Windows.Forms.Timer();
            muteGuardTimer.Interval = 2000;
            muteGuardTimer.Tick += MuteGuardTick;
            UpdateMuteGuardTimer();

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
                DialogResult result;
                ReleaseHeldPushToTalkKeys();
                settingsOpen = true;
                try
                {
                    result = settingsForm.ShowDialog();
                }
                finally
                {
                    settingsOpen = false;
                }

                if (result == DialogResult.OK)
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

                    // Deckt Sprachwechsel und alle Schnellschalter in einem ab
                    SyncMenuChecks();

                    if (!RegisterGlobalHotkeys())
                    {
                        MessageBox.Show(
                            Translations.HotkeyRegisterFailed(config.AppLanguage),
                            "MicMute", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        /// <summary>
        /// Registriert alle aktivierten Hotkeys. Liefert false, wenn mindestens einer
        /// abgelehnt wurde - typischerweise weil ein anderes Programm ihn belegt.
        /// </summary>
        private static bool RegisterGlobalHotkeys()
        {
            bool allOk = true;

            try
            {
                allOk &= TryRegister(config.HotkeyToggleEnabled, config.HotkeyToggleKey, config.HotkeyToggleModifiers, HOTKEY_ID_TOGGLE, "Umschalten");
                allOk &= TryRegister(config.HotkeyMuteEnabled, config.HotkeyMuteKey, config.HotkeyMuteModifiers, HOTKEY_ID_MUTE, "Stummschalten");
                allOk &= TryRegister(config.HotkeyUnmuteEnabled, config.HotkeyUnmuteKey, config.HotkeyUnmuteModifiers, HOTKEY_ID_UNMUTE, "Einschalten");
            }
            catch (Exception ex)
            {
                Log("RegisterGlobalHotkeys", ex);
                allOk = false;
            }

            if (allOk)
            {
                Log("Hotkeys registriert");
            }

            SetupPushToTalkHook();
            SetupForegroundHook();

            return allOk;
        }

        private static bool TryRegister(bool enabled, Keys key, Keys modifiers, int id, string label)
        {
            if (!enabled || key == Keys.None)
                return true;

            if (RegisterHotKey(hotkeyWindow.Handle, id, GetModifiers(modifiers), (uint)key))
                return true;

            Log(string.Format("Hotkey '{0}' ({1}) konnte nicht registriert werden - vermutlich bereits belegt", label, key));
            return false;
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
            RemoveForegroundHook();
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
            catch (Exception ex)
            {
                Log("LoadActualMicState", ex);
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

        /// <summary>
        /// Beschafft IAudioEndpointVolume für das Standard-Aufnahmegerät.
        /// Der Aufrufer besitzt den Zeiger und muss SafeRelease() darauf aufrufen.
        /// </summary>
        private static bool TryGetEndpointVolume(out IntPtr endpointVolume, out IAudioEndpointVolumeVtbl volumeVtbl)
        {
            endpointVolume = IntPtr.Zero;
            volumeVtbl = default(IAudioEndpointVolumeVtbl);

            IntPtr deviceEnumerator = IntPtr.Zero;
            IntPtr device = IntPtr.Zero;

            try
            {
                Guid clsid = CLSID_MMDeviceEnumerator;
                Guid iid = IID_IMMDeviceEnumerator;

                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out deviceEnumerator);
                if (hr != 0 || deviceEnumerator == IntPtr.Zero)
                    return false;

                var vtbl = (IMMDeviceEnumeratorVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(deviceEnumerator), typeof(IMMDeviceEnumeratorVtbl));

                hr = vtbl.GetDefaultAudioEndpoint(deviceEnumerator, eCapture, eConsole, out device);
                if (hr != 0 || device == IntPtr.Zero)
                    return false;

                var deviceVtbl = (IMMDeviceVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(device), typeof(IMMDeviceVtbl));

                Guid volumeIid = IID_IAudioEndpointVolume;
                hr = deviceVtbl.Activate(device, ref volumeIid, CLSCTX_ALL, IntPtr.Zero, out endpointVolume);
                if (hr != 0 || endpointVolume == IntPtr.Zero)
                {
                    endpointVolume = IntPtr.Zero;
                    return false;
                }

                volumeVtbl = (IAudioEndpointVolumeVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(endpointVolume), typeof(IAudioEndpointVolumeVtbl));

                return true;
            }
            catch (Exception ex)
            {
                Log("TryGetEndpointVolume", ex);
                return false;
            }
            finally
            {
                SafeRelease(device);
                SafeRelease(deviceEnumerator);
            }
        }

        /// <summary>Nur das Standardgerät - einzig für den WM_APPCOMMAND-Fallback.</summary>
        private static bool? GetDefaultMicrophoneMuteState()
        {
            IntPtr endpointVolume;
            IAudioEndpointVolumeVtbl volumeVtbl;

            if (!TryGetEndpointVolume(out endpointVolume, out volumeVtbl))
                return null;

            try
            {
                int muted;
                if (volumeVtbl.GetMute(endpointVolume, out muted) < 0)
                    return null;

                return muted != 0;
            }
            catch (Exception ex)
            {
                Log("GetDefaultMicrophoneMuteState", ex);
                return null;
            }
            finally
            {
                SafeRelease(endpointVolume);
            }
        }

        /// <summary>Ergebnis einer Stumm-/Einschaltaktion über alle Aufnahmegeräte.</summary>
        private enum MuteApplyResult { Ok, Partial, Unavailable }

        private struct CaptureEndpoint
        {
            public string Id;
            public IntPtr Volume;
        }

        /// <summary>IDs der Geräte, die vor dem Stummschalten offen waren. Nur im Speicher.</summary>
        private static readonly HashSet<string> reactivateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>true, solange isMuted gilt, aber nicht alle Geräte bestätigt stumm sind.</summary>
        private static bool muteIncomplete = false;

        private static string lastGuardMessage;

        private static string GetDeviceId(IntPtr device)
        {
            IntPtr idPtr = IntPtr.Zero;
            try
            {
                var deviceVtbl = (IMMDeviceVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(device), typeof(IMMDeviceVtbl));
                if (deviceVtbl.GetId(device, out idPtr) != 0 || idPtr == IntPtr.Zero)
                    return null;
                return Marshal.PtrToStringUni(idPtr);
            }
            catch (Exception ex)
            {
                Log("GetDeviceId", ex);
                return null;
            }
            finally
            {
                // GetId liefert CoTaskMem-Speicher
                if (idPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(idPtr);
            }
        }

        /// <summary>
        /// Alle aktiven Aufnahmegeräte mit ID und IAudioEndpointVolume, nicht nur das
        /// Standardgerät: Teams & Co. nehmen oft das Kommunikationsgerät oder ein eigenes.
        /// Der Aufrufer muss ReleaseEndpoints() aufrufen. Leere Liste = nicht verfügbar.
        /// </summary>
        private static List<CaptureEndpoint> GetActiveCaptureEndpoints()
        {
            var result = new List<CaptureEndpoint>();
            IntPtr deviceEnumerator = IntPtr.Zero;
            IntPtr collection = IntPtr.Zero;

            try
            {
                Guid clsid = CLSID_MMDeviceEnumerator;
                Guid iid = IID_IMMDeviceEnumerator;

                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out deviceEnumerator);
                if (hr != 0 || deviceEnumerator == IntPtr.Zero)
                    return result;

                var vtbl = (IMMDeviceEnumeratorVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(deviceEnumerator), typeof(IMMDeviceEnumeratorVtbl));

                hr = vtbl.EnumAudioEndpoints(deviceEnumerator, eCapture, DEVICE_STATE_ACTIVE, out collection);
                if (hr != 0 || collection == IntPtr.Zero)
                    return result;

                var collectionVtbl = (IMMDeviceCollectionVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(collection), typeof(IMMDeviceCollectionVtbl));

                uint count;
                if (collectionVtbl.GetCount(collection, out count) != 0)
                    return result;

                for (uint i = 0; i < count; i++)
                {
                    IntPtr device = IntPtr.Zero;
                    try
                    {
                        if (collectionVtbl.Item(collection, i, out device) != 0 || device == IntPtr.Zero)
                            continue;

                        var deviceVtbl = (IMMDeviceVtbl)Marshal.PtrToStructure(
                            Marshal.ReadIntPtr(device), typeof(IMMDeviceVtbl));

                        Guid volumeIid = IID_IAudioEndpointVolume;
                        IntPtr endpointVolume;
                        if (deviceVtbl.Activate(device, ref volumeIid, CLSCTX_ALL, IntPtr.Zero, out endpointVolume) == 0
                            && endpointVolume != IntPtr.Zero)
                        {
                            CaptureEndpoint ep;
                            ep.Id = GetDeviceId(device);
                            ep.Volume = endpointVolume;
                            result.Add(ep);
                        }
                    }
                    finally
                    {
                        SafeRelease(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GetActiveCaptureEndpoints", ex);
            }
            finally
            {
                SafeRelease(collection);
                SafeRelease(deviceEnumerator);
            }

            return result;
        }

        private static void ReleaseEndpoints(List<CaptureEndpoint> endpoints)
        {
            foreach (CaptureEndpoint ep in endpoints)
                SafeRelease(ep.Volume);
        }

        /// <summary>IDs der Standard-Aufnahmegeräte für Konsole und Kommunikation.</summary>
        private static HashSet<string> GetDefaultCaptureIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr deviceEnumerator = IntPtr.Zero;

            try
            {
                Guid clsid = CLSID_MMDeviceEnumerator;
                Guid iid = IID_IMMDeviceEnumerator;

                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out deviceEnumerator);
                if (hr != 0 || deviceEnumerator == IntPtr.Zero)
                    return ids;

                var vtbl = (IMMDeviceEnumeratorVtbl)Marshal.PtrToStructure(
                    Marshal.ReadIntPtr(deviceEnumerator), typeof(IMMDeviceEnumeratorVtbl));

                foreach (int role in new[] { eConsole, eCommunications })
                {
                    IntPtr device = IntPtr.Zero;
                    try
                    {
                        if (vtbl.GetDefaultAudioEndpoint(deviceEnumerator, eCapture, role, out device) == 0 && device != IntPtr.Zero)
                        {
                            string id = GetDeviceId(device);
                            if (id != null) ids.Add(id);
                        }
                    }
                    finally
                    {
                        SafeRelease(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("GetDefaultCaptureIds", ex);
            }
            finally
            {
                SafeRelease(deviceEnumerator);
            }

            return ids;
        }

        private static IAudioEndpointVolumeVtbl GetVolumeVtbl(IntPtr endpointVolume)
        {
            return (IAudioEndpointVolumeVtbl)Marshal.PtrToStructure(
                Marshal.ReadIntPtr(endpointVolume), typeof(IAudioEndpointVolumeVtbl));
        }

        /// <summary>1 = stumm, 0 = offen, -1 = nicht lesbar.</summary>
        private static int ReadMute(IntPtr endpointVolume)
        {
            try
            {
                int muted;
                if (GetVolumeVtbl(endpointVolume).GetMute(endpointVolume, out muted) < 0)
                    return -1;
                return muted != 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Log("ReadMute", ex);
                return -1;
            }
        }

        private static void WriteMute(IntPtr endpointVolume, bool muted)
        {
            try
            {
                Guid eventContext = Guid.Empty;
                // S_FALSE (1) bedeutet "war bereits in diesem Zustand" und ist ein Erfolg.
                GetVolumeVtbl(endpointVolume).SetMute(endpointVolume, muted ? 1 : 0, ref eventContext);
            }
            catch (Exception ex)
            {
                Log("WriteMute", ex);
            }
        }

        /// <summary>Liest nach dem Schreiben bis ~200 ms nach: Bluetooth-Geräte melden den neuen Zustand verzögert.</summary>
        private static bool ConfirmMute(IntPtr endpointVolume, bool expectedMuted)
        {
            int expected = expectedMuted ? 1 : 0;
            Stopwatch watch = Stopwatch.StartNew();

            while (true)
            {
                if (ReadMute(endpointVolume) == expected)
                    return true;
                if (watch.ElapsedMilliseconds >= 200)
                    return false;
                Thread.Sleep(15);
            }
        }

        private static string JoinIds(IEnumerable<string> ids)
        {
            string joined = string.Join(", ", ids.ToArray());
            return joined.Length == 0 ? "-" : joined;
        }

        /// <summary>
        /// Stumm nur, wenn ALLE aktiven Aufnahmegeräte stumm sind. Ein nicht lesbares
        /// Gerät gilt als nicht stumm. null ohne Geräteliste.
        /// </summary>
        private static bool? GetSystemMicrophoneMuteState()
        {
            List<CaptureEndpoint> endpoints = GetActiveCaptureEndpoints();
            try
            {
                if (endpoints.Count == 0)
                    return null;

                foreach (CaptureEndpoint ep in endpoints)
                {
                    if (ReadMute(ep.Volume) != 1)
                        return false;
                }
                return true;
            }
            finally
            {
                ReleaseEndpoints(endpoints);
            }
        }

        /// <summary>
        /// Schaltet alle aktiven Aufnahmegeräte stumm. Geräte, die vorher offen waren, kommen
        /// in reactivateIds - nur sie werden beim Einschalten wieder geöffnet. Danach wird
        /// nachgelesen: Ok nur, wenn wirklich alle stumm sind.
        /// explicitAction = false: Aufruf aus der Kontrolle, loggt nur bei Handlung und ohne Wiederholung.
        /// </summary>
        private static MuteApplyResult MuteAllMicrophones(bool explicitAction)
        {
            string label = explicitAction ? "Stummschalten" : "Kontrolle";
            List<CaptureEndpoint> endpoints = GetActiveCaptureEndpoints();

            try
            {
                if (endpoints.Count == 0)
                {
                    if (explicitAction) Log(label + ": keine aktiven Aufnahmegeräte gefunden");
                    return MuteApplyResult.Unavailable;
                }

                var acted = new List<CaptureEndpoint>();
                foreach (CaptureEndpoint ep in endpoints)
                {
                    int state = ReadMute(ep.Volume);
                    if (state == 1)
                        continue;
                    if (state == 0 && ep.Id != null)
                        reactivateIds.Add(ep.Id);
                    WriteMute(ep.Volume, true);
                    acted.Add(ep);
                }

                if (acted.Count == 0)
                {
                    if (explicitAction)
                        Log(string.Format("{0}: alle {1} Mikrofone bereits stumm", label, endpoints.Count));
                    lastGuardMessage = null;
                    return MuteApplyResult.Ok;
                }

                var failed = new List<string>();
                foreach (CaptureEndpoint ep in acted)
                {
                    if (!ConfirmMute(ep.Volume, true))
                        failed.Add(ep.Id ?? "?");
                }

                string message = string.Format("{0}: {1} Mikrofone aktiv, {2} stummgeschaltet, fehlgeschlagen: {3} - IDs: {4}",
                    label, endpoints.Count, acted.Count - failed.Count, JoinIds(failed),
                    JoinIds(acted.Select(ep => ep.Id ?? "?")));

                // Die Kontrolle alle 2 s soll das Protokoll bei Dauerfehlern nicht fluten.
                if (explicitAction || message != lastGuardMessage)
                    Log(message);
                lastGuardMessage = explicitAction ? null : message;

                return failed.Count == 0 ? MuteApplyResult.Ok : MuteApplyResult.Partial;
            }
            finally
            {
                ReleaseEndpoints(endpoints);
            }
        }

        /// <summary>
        /// Öffnet nur die Geräte, die vor dem Stummschalten offen waren. Ohne Gedächtnis
        /// (Start mit bereits stummen Geräten, oder die gemerkten Geräte sind weg) werden die
        /// Standardgeräte für Konsole und Kommunikation geöffnet - die nutzen Teams & Co.;
        /// "alle" würde ein absichtlich stummes Mikrofon öffnen.
        /// </summary>
        private static MuteApplyResult UnmuteRememberedMicrophones()
        {
            List<CaptureEndpoint> endpoints = GetActiveCaptureEndpoints();

            try
            {
                if (endpoints.Count == 0)
                {
                    Log("Einschalten: keine aktiven Aufnahmegeräte gefunden");
                    return MuteApplyResult.Unavailable;
                }

                string rule = "vorher offene";
                var targets = new HashSet<string>(
                    endpoints.Where(ep => ep.Id != null && reactivateIds.Contains(ep.Id)).Select(ep => ep.Id),
                    StringComparer.OrdinalIgnoreCase);

                if (targets.Count == 0)
                {
                    targets = GetDefaultCaptureIds();
                    rule = "Standardgeräte";
                }

                var opened = new List<string>();
                var failed = new List<string>();
                foreach (CaptureEndpoint ep in endpoints)
                {
                    if (ep.Id == null || !targets.Contains(ep.Id))
                        continue;
                    WriteMute(ep.Volume, false);
                    if (ConfirmMute(ep.Volume, false)) opened.Add(ep.Id); else failed.Add(ep.Id);
                }

                reactivateIds.Clear();

                Log(string.Format("Einschalten ({0}): {1} Mikrofone aktiv, {2} geöffnet, fehlgeschlagen: {3} - IDs: {4}",
                    rule, endpoints.Count, opened.Count, JoinIds(failed), JoinIds(opened)));

                return failed.Count == 0 && opened.Count > 0 ? MuteApplyResult.Ok : MuteApplyResult.Partial;
            }
            finally
            {
                ReleaseEndpoints(endpoints);
            }
        }

        /// <summary>Merkt sich unvollständiges Stummschalten; Hinweis immer, auch bei abgeschalteten Toasts.</summary>
        private static void SetMuteIncomplete(bool incomplete)
        {
            if (incomplete && !muteIncomplete)
            {
                Log("WARNUNG: nicht alle Mikrofone stumm - Icon zeigt nicht \"stumm\"");
                ShowNotification(Translations.MuteIncomplete(config.AppLanguage));
            }
            else if (!incomplete && muteIncomplete && isMuted)
            {
                Log("Alle Mikrofone jetzt stumm");
            }

            muteIncomplete = incomplete;
        }

        // ponytail: Hier bewusst Polling statt Ereignis (anders als der WinEvent-Hook oben):
        // ponytail: IMMNotificationClient bräuchte COM-Rückrufe auf fremden Threads. Der Tick
        // ponytail: läuft nur im stummen Zustand, auf dem UI-Thread, und erkennt neue Geräte
        // ponytail: genauso wie von außen wieder geöffnete.
        private static System.Windows.Forms.Timer muteGuardTimer;

        private static void UpdateMuteGuardTimer()
        {
            if (muteGuardTimer != null)
                muteGuardTimer.Enabled = isMuted;
        }

        private static void MuteGuardTick(object sender, EventArgs e)
        {
            try
            {
                if (!isMuted)
                {
                    UpdateMuteGuardTimer();
                    return;
                }

                MuteApplyResult result = MuteAllMicrophones(false);
                if (result == MuteApplyResult.Unavailable)
                    return;

                bool wasIncomplete = muteIncomplete;
                SetMuteIncomplete(result != MuteApplyResult.Ok);
                if (wasIncomplete != muteIncomplete)
                {
                    UpdateTrayIcon();
                    SyncLed(isMuted && !muteIncomplete);
                }
            }
            catch (Exception ex)
            {
                Log("MuteGuardTick", ex);
            }
        }

        /// <summary>Idempotent: aus dem Beenden-Menü und aus dem finally von Main.</summary>
        private static void StopMuteGuard()
        {
            if (muteGuardTimer == null)
                return;

            try
            {
                muteGuardTimer.Stop();
                muteGuardTimer.Dispose();
            }
            catch (Exception ex)
            {
                Log("StopMuteGuard", ex);
            }

            muteGuardTimer = null;
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
            // EnumAudioEndpoints ist Slot 3 des Interfaces. Fehlte bis v2.2.0, dadurch
            // landete jeder GetDefaultAudioEndpoint-Aufruf auf EnumAudioEndpoints und
            // scheiterte mit E_INVALIDARG (0x80070057) - der Mute-Status wurde nie gelesen.
            public EnumAudioEndpointsDelegate EnumAudioEndpoints;
            public GetDefaultAudioEndpointDelegate GetDefaultAudioEndpoint;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int EnumAudioEndpointsDelegate(IntPtr This, int dataFlow, int dwStateMask, out IntPtr ppDevices);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetDefaultAudioEndpointDelegate(IntPtr This, int dataFlow, int role, out IntPtr ppDevice);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMMDeviceCollectionVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public GetCountDelegate GetCount;
            public ItemDelegate Item;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetCountDelegate(IntPtr This, out uint pcDevices);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int ItemDelegate(IntPtr This, uint nDevice, out IntPtr ppDevice);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMMDeviceVtbl
        {
            public IntPtr QueryInterface;
            public IntPtr AddRef;
            public IntPtr Release;
            public ActivateDelegate Activate;
            public IntPtr OpenPropertyStore;
            public GetIdDelegate GetId;
            public IntPtr GetState;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int ActivateDelegate(IntPtr This, ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetIdDelegate(IntPtr This, out IntPtr ppstrId);
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

        /// <summary>Auslöser einer Zustandsänderung - steuert Toast und Logeintrag.</summary>
        private enum MuteSource { Silent, Toggle, Mute, Unmute, PushToTalk, Lock, Profile }

        private static void ToggleMic(object sender, EventArgs e)
        {
            // Umschalten nach dem angezeigten Zustand: bei unvollständigem Stumm erneut stummschalten.
            SetMicMuted(!(isMuted && !muteIncomplete), MuteSource.Toggle);
        }

        private static void SetMicMutedExplicit(object sender, EventArgs e)
        {
            SetMicMuted(true, MuteSource.Mute);
        }

        private static void SetMicUnmutedExplicit(object sender, EventArgs e)
        {
            SetMicMuted(false, MuteSource.Unmute);
        }

        private static void SetMicMuted(bool muted)
        {
            SetMicMuted(muted, MuteSource.Silent);
        }

        private static void SetMicMuted(bool muted, MuteSource source)
        {
            isMuted = muted;

            MuteApplyResult result = muted ? MuteAllMicrophones(true) : UnmuteRememberedMicrophones();

            if (result == MuteApplyResult.Unavailable)
            {
                // Fallback nur ohne nutzbare Geräteliste. WM_APPCOMMAND ist ein Toggle und trifft
                // nur das Standardgerät: senden nur, wenn dessen Zustand bekannt und anders ist.
                bool? defaultState = GetDefaultMicrophoneMuteState();
                IntPtr hwnd = GetForegroundWindow();
                if (defaultState.HasValue && defaultState.Value != muted && hwnd != IntPtr.Zero)
                {
                    SendMessageW(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)APPCOMMAND_MICROPHONE_VOLUME_MUTE);
                    Log("Geräteliste nicht verfügbar - WM_APPCOMMAND an das Standardgerät gesendet");
                }
                else
                {
                    Log("Geräteliste nicht verfügbar - kein Fallback (Zustand des Standardgeräts unbekannt oder schon richtig)");
                }
            }

            // Rot nur bei bestätigtem Stumm aller Mikrofone.
            SetMuteIncomplete(muted && result != MuteApplyResult.Ok);

            UpdateTrayIcon();
            SyncLed(isMuted && !muteIncomplete);
            UpdateMuteGuardTimer();

            // Push-to-Talk feuert pro Tastendruck - dabei nicht bei jedem Anschlag
            // die komplette Konfigurationsdatei neu schreiben.
            if (source != MuteSource.PushToTalk)
            {
                SaveMicStateToFile();
            }

            ReportStateChange(muted, source);
        }

        /// <summary>Logeintrag und Toast für eine Zustandsänderung - eine Stelle statt fünf.</summary>
        private static void ReportStateChange(bool muted, MuteSource source)
        {
            if (source == MuteSource.Silent)
                return;

            string label;
            bool showToast;

            switch (source)
            {
                case MuteSource.Toggle:     label = "Toggle";       showToast = config.ShowToastOnToggle; break;
                case MuteSource.Mute:       label = "Stummschalten"; showToast = config.ShowToastOnMute; break;
                case MuteSource.Unmute:     label = "Einschalten";  showToast = config.ShowToastOnUnmute; break;
                case MuteSource.PushToTalk: label = "Push-to-Talk"; showToast = config.ShowToastOnPushToTalk; break;
                case MuteSource.Lock:       label = "Sperre";       showToast = false; break;
                case MuteSource.Profile:    label = "App-Profil";   showToast = false; break;
                default: return;
            }

            Log(string.Format("{0}: Mikrofon {1}", label, muted ? "ausgeschaltet" : "eingeschaltet"));

            if (showToast)
            {
                string statusText = muted
                    ? Translations.MicrophoneOff(config.AppLanguage)
                    : Translations.MicrophoneOn(config.AppLanguage);

                string title = source == MuteSource.PushToTalk
                    ? "Push-to-Talk"
                    : Translations.Microphone(config.AppLanguage);

                ShowNotification(string.Format("{0}: {1}", title, statusText));
            }
        }

        private static void UpdateTrayIcon()
        {
            try
            {
                // Rot nur, wenn das Nachlesen bestätigt hat, dass wirklich alle stumm sind.
                bool shownMuted = isMuted && !muteIncomplete;
                trayIcon.Icon = shownMuted ? iconMuted : iconUnmuted;
                trayIcon.Text = Translations.TrayStatus(config.AppLanguage, shownMuted);

                if (statusItem != null)
                    statusItem.Text = Translations.TrayStatus(config.AppLanguage, shownMuted);

                // Bei unvollständigem Stummschalten beides anbieten: erneut versuchen oder aufgeben.
                muteItem.Visible = !shownMuted;
                unmuteItem.Visible = isMuted;
            }
            catch (Exception ex)
            {
                Log("UpdateTrayIcon", ex);
            }
        }

        private static bool AnyToastEnabled()
        {
            return config.ShowToastOnToggle || config.ShowToastOnMute || config.ShowToastOnUnmute
                || config.ShowToastOnStartup || config.ShowToastOnPushToTalk;
        }

        /// <summary>
        /// Bringt Haken, Beschriftungen und Sichtbarkeit im Tray-Menü auf den Stand
        /// der Konfiguration. Nach dem Einstellungsdialog und nach jedem Schnellschalter.
        /// </summary>
        private static void SyncMenuChecks()
        {
            if (statusItem == null)
                return;

            try
            {
                Language lang = config.AppLanguage;

                statusItem.Text = Translations.TrayStatus(lang, isMuted);

                muteItem.Text = Translations.MuteMicrophone(lang);
                unmuteItem.Text = Translations.UnmuteMicrophone(lang);
                settingsItem.Text = Translations.Settings(lang);
                exitItem.Text = Translations.Exit(lang);

                pttItem.Text = Translations.PushToTalk(lang);
                pttItem.Checked = config.PushToTalkEnabled;
                // Ohne belegte Taste ist der Schalter wirkungslos
                pttItem.Visible = config.HasPushToTalkKey;

                toastItem.Text = Translations.ToastNotifications(lang);
                toastItem.Checked = AnyToastEnabled();

                lockItem.Text = Translations.AutoMuteOnLock(lang);
                lockItem.Checked = config.AutoMuteOnLock;

                autostartItem.Text = Translations.StartWithWindows(lang);
                autostartItem.Checked = config.AutostartEnabled;

                logItem.Text = Translations.OpenLog(lang);
                logItem.Visible = config.LoggingEnabled;

                UpdateTrayIcon();
            }
            catch (Exception ex)
            {
                Log("SyncMenuChecks", ex);
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
            catch (Exception ex)
            {
                Log("ShowNotification", ex);
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
                    // SetMute ist idempotent: kein Vergleich mit isMuted, damit "Stumm"
                    // bei unvollständigem Stummschalten erneut versucht wird.
                    else if (m.WParam.ToInt32() == HOTKEY_ID_MUTE)
                    {
                        SetMicMutedExplicit(null, EventArgs.Empty);
                    }
                    else if (m.WParam.ToInt32() == HOTKEY_ID_UNMUTE)
                    {
                        SetMicUnmutedExplicit(null, EventArgs.Empty);
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

        public static string MuteIncomplete(Language lang)
        {
            return lang == Language.German
                ? "Nicht alle Mikrofone konnten stummgeschaltet werden - siehe Protokoll."
                : "Not all microphones could be muted - see log.";
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
            return lang == Language.German
                ? "Taste gedrückt halten = Mikrofon an, Loslassen = Mikrofon aus. Solange Push-to-Talk aktiv ist, hat die Taste keine andere Funktion."
                : "Hold a key = microphone on, release = microphone off. While push-to-talk is active, the key does nothing else.";
        }

        public static string AddPushToTalkKey(Language lang)
        {
            return lang == Language.German ? "Weitere Taste hinzufügen" : "Add another key";
        }

        public static string RemovePushToTalkKey(Language lang)
        {
            return lang == Language.German ? "Taste entfernen" : "Remove key";
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

        /// <summary>Statuszeile für Tray-Tooltip und Menükopf.</summary>
        public static string TrayStatus(Language lang, bool muted)
        {
            return string.Format("{0}: {1}", Microphone(lang), muted ? MicrophoneOff(lang) : MicrophoneOn(lang));
        }

        public static string OK(Language lang)
        {
            return "OK";
        }

        public static string Cancel(Language lang)
        {
            return lang == Language.German ? "Abbrechen" : "Cancel";
        }

        public static string OpenLog(Language lang)
        {
            return lang == Language.German ? "Protokoll öffnen" : "Open log";
        }

        public static string Logging(Language lang)
        {
            return lang == Language.German ? "Protokollierung" : "Logging";
        }

        public static string EnableLogging(Language lang)
        {
            return lang == Language.German ? "Ereignisse in MicMuteLog.txt protokollieren" : "Log events to MicMuteLog.txt";
        }

        public static string Automation(Language lang)
        {
            return lang == Language.German ? "Automatik" : "Automation";
        }

        public static string AutoMuteOnLock(Language lang)
        {
            return lang == Language.German ? "Stumm bei Sperre" : "Mute on lock";
        }

        public static string AutoMuteOnLockDescription(Language lang)
        {
            return lang == Language.German
                ? "Mikrofon beim Sperren stummschalten und beim Entsperren wiederherstellen"
                : "Mute the microphone on lock and restore it on unlock";
        }

        public static string LedSync(Language lang)
        {
            return lang == Language.German ? "Tastatur-LED" : "Keyboard LED";
        }

        public static string EnableLedSync(Language lang)
        {
            return lang == Language.German ? "LED leuchtet, wenn das Mikrofon stumm ist" : "LED lights up while the microphone is muted";
        }

        public static string LedSyncWarning(Language lang)
        {
            return lang == Language.German
                ? "Achtung: schaltet den echten Tastaturmodus mit. Rollen ist z. B. in Excel funktional."
                : "Note: this also toggles the actual keyboard mode. Scroll Lock is functional in Excel, for example.";
        }

        public static string AppProfiles(Language lang)
        {
            return lang == Language.German ? "App-Profile" : "App profiles";
        }

        public static string AppProfilesDescription(Language lang)
        {
            return lang == Language.German
                ? "Verhalten, solange die Anwendung im Vordergrund ist"
                : "Behaviour while the application is in the foreground";
        }

        public static string Add(Language lang)
        {
            return lang == Language.German ? "Hinzufügen" : "Add";
        }

        public static string Remove(Language lang)
        {
            return lang == Language.German ? "Entfernen" : "Remove";
        }

        public static string ProfileModeMute(Language lang)
        {
            return lang == Language.German ? "Stumm" : "Muted";
        }

        public static string ProfileModeUnmute(Language lang)
        {
            return lang == Language.German ? "Aktiv" : "Unmuted";
        }

        public static string ProfileModePtt(Language lang)
        {
            return lang == Language.German ? "Push-to-Talk" : "Push-to-talk";
        }

        public static string HotkeyRegisterFailed(Language lang)
        {
            return lang == Language.German
                ? "Mindestens ein globaler Hotkey konnte nicht registriert werden. Vermutlich wird er bereits von einem anderen Programm benutzt."
                : "At least one global hotkey could not be registered. It is probably already in use by another application.";
        }

        public static string HotkeyNeedsModifier(Language lang)
        {
            return lang == Language.German
                ? "Ein globaler Hotkey braucht mindestens Strg, Umschalt oder Alt.\nEinzelne Tasten sind nur für F13-F24, Pause und Rollen erlaubt."
                : "A global hotkey needs at least Ctrl, Shift or Alt.\nStandalone keys are only allowed for F13-F24, Pause and Scroll Lock.";
        }

        public static string HotkeyDuplicate(Language lang)
        {
            return lang == Language.German
                ? "Dieselbe Tastenkombination ist mehrfach vergeben. Bitte unterschiedliche Kombinationen wählen."
                : "The same key combination is assigned more than once. Please choose different combinations.";
        }
    }

    /// <summary>Eine Taste mit ihren Modifiern (Strg, Umschalt, Alt).</summary>
    public struct HotkeyBinding
    {
        public readonly Keys Key;
        public readonly Keys Modifiers;

        public HotkeyBinding(Keys key, Keys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
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
        /// <summary>
        /// Push-to-Talk-Tasten. Die erste steht wie bisher in PUSH_TO_TALK_KEY/_MODIFIERS,
        /// alle weiteren in PUSH_TO_TALK_EXTRA_KEYS. Enthält nie Keys.None.
        /// </summary>
        public List<HotkeyBinding> PushToTalkKeys { get; set; }

        public bool HasPushToTalkKey { get { return PushToTalkKeys != null && PushToTalkKeys.Count > 0; } }
        
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

        public bool LoggingEnabled { get; set; }
        public bool AutoMuteOnLock { get; set; }
        public bool LedSyncEnabled { get; set; }
        public Keys LedSyncKey { get; set; }
        public string ProfileApps { get; set; }

        /// <summary>
        /// Zeilen, die diese Version nicht kennt. Werden unverändert zurückgeschrieben,
        /// damit eine ältere Version die Konfiguration einer neueren nicht zerstört.
        /// </summary>
        private List<string> unknownLines = new List<string>();

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
            PushToTalkKeys = new List<HotkeyBinding>();
            
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

            LoggingEnabled = true;
            AutoMuteOnLock = false;
            LedSyncEnabled = false;
            LedSyncKey = Keys.Scroll;
            ProfileApps = "";
        }

        /// <summary>
        /// Vollständige Kopie inklusive unbekannter Zeilen. Bewusst statt eines
        /// Objektinitialisierers, damit neue Properties nicht vergessen werden können.
        /// </summary>
        public Config Clone()
        {
            Config copy = (Config)this.MemberwiseClone();
            copy.unknownLines = new List<string>(this.unknownLines);
            copy.PushToTalkKeys = new List<HotkeyBinding>(this.PushToTalkKeys);
            return copy;
        }

        /// <summary>
        /// PUSH_TO_TALK_EXTRA_KEYS: "Taste|Modifier;Taste|Modifier", z. B. "F14|None;LaunchApplication1|Control".
        /// Kommas gehören zu Keys.ToString() ("Shift, Control"), deshalb | und ; als Trenner.
        /// Ungültige und doppelte Einträge werden übersprungen.
        /// </summary>
        private static List<HotkeyBinding> ParsePushToTalkKeys(Keys firstKey, Keys firstModifiers, string extra)
        {
            var result = new List<HotkeyBinding>();
            AddPushToTalkKey(result, firstKey, firstModifiers);

            foreach (string entry in (extra ?? "").Split(';'))
            {
                string[] parts = entry.Split('|');
                Keys key;
                Keys modifiers = Keys.None;
                if (!Enum.TryParse(parts[0].Trim(), out key))
                    continue;
                if (parts.Length > 1 && !Enum.TryParse(parts[1].Trim(), out modifiers))
                    continue;
                AddPushToTalkKey(result, key, modifiers);
            }

            return result;
        }

        private static void AddPushToTalkKey(List<HotkeyBinding> list, Keys key, Keys modifiers)
        {
            // Taste und Modifier sauber trennen, falls die Datei von Hand bearbeitet wurde
            key &= Keys.KeyCode;
            modifiers &= Keys.Modifiers;
            if (key == Keys.None)
                return;

            foreach (HotkeyBinding b in list)
            {
                if (b.Key == key && b.Modifiers == modifiers)
                    return;
            }
            list.Add(new HotkeyBinding(key, modifiers));
        }

        public static Config Load()
        {
            Config config = new Config();
            
            if (!File.Exists(configFile))
            {
                config.SaveWithMutedState(true);
                return config;
            }

            Keys pttKey = Keys.None;
            Keys pttModifiers = Keys.None;
            string pttExtra = "";

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
                            if(Enum.TryParse(val, out kVal)) pttKey = kVal;
                            break;
                        case "PUSH_TO_TALK_MODIFIERS":
                            if(Enum.TryParse(val, out kVal)) pttModifiers = kVal;
                            break;
                        case "PUSH_TO_TALK_EXTRA_KEYS":
                            pttExtra = val;
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

                        case "LOGGING_ENABLED":
                            if(bool.TryParse(val, out bVal)) config.LoggingEnabled = bVal;
                            break;
                        case "AUTO_MUTE_ON_LOCK":
                            if(bool.TryParse(val, out bVal)) config.AutoMuteOnLock = bVal;
                            break;
                        case "LED_SYNC_ENABLED":
                            if(bool.TryParse(val, out bVal)) config.LedSyncEnabled = bVal;
                            break;
                        case "LED_SYNC_KEY":
                            if(Enum.TryParse(val, out kVal)) config.LedSyncKey = kVal;
                            break;
                        case "PROFILE_APPS":
                            config.ProfileApps = val;
                            break;

                        // Wird von LoadMicStateFromFile gelesen und von SaveWithMutedState
                        // geschrieben - hier nur abfangen, damit es nicht als unbekannt gilt.
                        case "MUTED":
                            break;

                        default:
                            config.unknownLines.Add(line);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Bei groben Fehlern werden Defaults genutzt
                Program.Log("Config.Load", ex);
            }

            config.PushToTalkKeys = ParsePushToTalkKeys(pttKey, pttModifiers, pttExtra);

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
                // Erste Taste in den alten Schlüsseln, damit ältere Versionen sie weiter lesen
                HotkeyBinding firstPtt = PushToTalkKeys.Count > 0 ? PushToTalkKeys[0] : new HotkeyBinding(Keys.None, Keys.None);
                sb.AppendLine("PUSH_TO_TALK_KEY=" + firstPtt.Key);
                sb.AppendLine("PUSH_TO_TALK_MODIFIERS=" + firstPtt.Modifiers);
                sb.AppendLine("PUSH_TO_TALK_EXTRA_KEYS=" + string.Join(";",
                    PushToTalkKeys.Skip(1).Select(b => b.Key + "|" + b.Modifiers).ToArray()));
                
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

                sb.AppendLine("LOGGING_ENABLED=" + LoggingEnabled);
                sb.AppendLine("AUTO_MUTE_ON_LOCK=" + AutoMuteOnLock);
                sb.AppendLine("LED_SYNC_ENABLED=" + LedSyncEnabled);
                sb.AppendLine("LED_SYNC_KEY=" + LedSyncKey);
                sb.AppendLine("PROFILE_APPS=" + ProfileApps);

                sb.AppendLine("MUTED=" + isMuted.ToString().ToUpper());

                // Unbekannte Zeilen unverändert erhalten
                foreach (string unknown in unknownLines)
                {
                    sb.AppendLine(unknown);
                }

                File.WriteAllText(configFile, sb.ToString());
            }
            catch (Exception ex)
            {
                Program.Log("Config.Save", ex);
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
            catch (Exception ex)
            {
                Program.Log("Config.SetAutostart", ex);
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
            catch (Exception ex)
            {
                Program.Log("Config.GetAutostartStatus", ex);
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
        private FlowLayoutPanel pnlPushToTalkKeys;
        private Button btnAddPushToTalkKey;
        private ToolTip toolTipPushToTalk;
        private readonly List<TextBox> pushToTalkBoxes = new List<TextBox>();
        private readonly List<Button> pushToTalkRemoveButtons = new List<Button>();
		private Label lblPushToTalkDesc;
        private CheckBox chkShowToastOnToggle;
        private CheckBox chkShowToastOnMute;
        private CheckBox chkShowToastOnUnmute;
        private CheckBox chkShowToastOnStartup;
        private CheckBox chkShowToastOnPushToTalk;
        private GroupBox grpPushToTalk;
        private GroupBox grpNotifications;

        private GroupBox grpLogging;
        private CheckBox chkLogging;

        private TabPage tabAutomation;
        private GroupBox grpAutoLock;
        private CheckBox chkAutoMuteOnLock;
        private Label lblAutoLockDesc;
        private GroupBox grpLed;
        private CheckBox chkLedSync;
        private ComboBox cmbLedKey;
        private Label lblLedWarning;
        private GroupBox grpProfiles;
        private Label lblProfilesDesc;
        private ListBox lstProfiles;
        private ComboBox cmbProfileApp;
        private ComboBox cmbProfileMode;
        private Button btnAddProfile;
        private Button btnRemoveProfile;

        private GroupBox grpToggle;
        private GroupBox grpMute;
        private GroupBox grpUnmute;
        private Label lblInfo;
        
        private Keys toggleKey = Keys.None;
        private Keys toggleModifiers = Keys.None;
        private Keys muteKey = Keys.None;
        private Keys muteModifiers = Keys.None;
        private Keys unmuteKey = Keys.None;
        private Keys unmuteModifiers = Keys.None;
        private List<HotkeyBinding> pushToTalkBindings = new List<HotkeyBinding>();

        private TextBox activeTextBox = null;

        public SettingsForm(Config cfg)
        {
            this.config = cfg.Clone();

            toggleKey = config.HotkeyToggleKey;
            toggleModifiers = config.HotkeyToggleModifiers;
            muteKey = config.HotkeyMuteKey;
            muteModifiers = config.HotkeyMuteModifiers;
            unmuteKey = config.HotkeyUnmuteKey;
            unmuteModifiers = config.HotkeyUnmuteModifiers;
            pushToTalkBindings = new List<HotkeyBinding>(config.PushToTalkKeys);

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = Translations.SettingsTitle(config.AppLanguage);
            this.Size = new Size(470, 570);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            tabControl = new TabControl
            {
                Location = new Point(10, 10),
                Size = new Size(435, 460)
            };

            TabPage tabHotkeys = new TabPage(Translations.GlobalHotkeys(config.AppLanguage));
            TabPage tabGeneral = new TabPage(config.AppLanguage == Language.German ? "Allgemein" : "General");

            grpToggle = new GroupBox
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

            grpMute = new GroupBox
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

            grpUnmute = new GroupBox
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

            lblInfo = new Label
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

            grpLogging = new GroupBox
            {
                Text = Translations.Logging(config.AppLanguage),
                Location = new Point(10, 350),
                Size = new Size(405, 55)
            };

            chkLogging = new CheckBox
            {
                Text = Translations.EnableLogging(config.AppLanguage),
                Location = new Point(15, 22),
                Size = new Size(370, 20),
                Checked = config.LoggingEnabled
            };

            grpLogging.Controls.Add(chkLogging);

            tabGeneral.Controls.Add(grpClickBehavior);
            tabGeneral.Controls.Add(grpLanguage);
            tabGeneral.Controls.Add(grpAutostart);
            tabGeneral.Controls.Add(grpDefaultState);
            tabGeneral.Controls.Add(grpLogging);

            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabHotkeys);
            
            // Advanced Tab
            TabPage tabAdvanced = new TabPage(Translations.AdvancedSettings(config.AppLanguage));
            
            grpPushToTalk = new GroupBox
            {
                Text = Translations.PushToTalk(config.AppLanguage),
                Location = new Point(10, 10),
                Size = new Size(405, 195)
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
                UpdatePushToTalkEnabled();
            };

            // Feste Höhe mit Scrollleiste: viele Tasten verschieben das Layout darunter nicht
            pnlPushToTalkKeys = new FlowLayoutPanel
            {
                Location = new Point(15, 50),
                Size = new Size(380, 87),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            toolTipPushToTalk = new ToolTip();
            this.FormClosed += delegate(object s, FormClosedEventArgs e) { toolTipPushToTalk.Dispose(); };

            btnAddPushToTalkKey = new Button
            {
                Text = "+",
                Location = new Point(15, 142),
                Size = new Size(28, 25)
            };
            toolTipPushToTalk.SetToolTip(btnAddPushToTalkKey, Translations.AddPushToTalkKey(config.AppLanguage));
            btnAddPushToTalkKey.Click += delegate(object s, EventArgs e)
            {
                pushToTalkBindings.Add(new HotkeyBinding(Keys.None, Keys.None));
                RebuildPushToTalkRows();
                TextBox last = pushToTalkBoxes[pushToTalkBoxes.Count - 1];
                pnlPushToTalkKeys.ScrollControlIntoView(last.Parent);
                last.Focus();
            };

            lblPushToTalkDesc = new Label
            {
                Text = Translations.PushToTalkDescription(config.AppLanguage),
                Location = new Point(50, 145),
                Size = new Size(345, 45),
                ForeColor = Color.Gray
            };

            grpPushToTalk.Controls.Add(chkEnablePushToTalk);
            grpPushToTalk.Controls.Add(pnlPushToTalkKeys);
            grpPushToTalk.Controls.Add(btnAddPushToTalkKey);
            grpPushToTalk.Controls.Add(lblPushToTalkDesc);

            RebuildPushToTalkRows();

            grpNotifications = new GroupBox
            {
                Text = Translations.ToastNotifications(config.AppLanguage),
                Location = new Point(10, 215),
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

            BuildAutomationTab();
            tabControl.TabPages.Add(tabAutomation);

            btnOK = new Button
            {
                Text = Translations.OK(config.AppLanguage),
                DialogResult = DialogResult.OK,
                Location = new Point(275, 480),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;

            btnCancel = new Button
            {
                Text = Translations.Cancel(config.AppLanguage),
                DialogResult = DialogResult.Cancel,
                Location = new Point(365, 480),
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
            UpdatePushToTalkDisplays();
        }

        /// <summary>Eine Profilregel. ToString() liefert die Anzeige, Raw das Speicherformat.</summary>
        private class ProfileEntry
        {
            public string App;
            public string Mode;
            public Language Lang;

            public string Raw { get { return App + ":" + Mode; } }

            public override string ToString()
            {
                string modeText;
                if (Mode == "mute") modeText = Translations.ProfileModeMute(Lang);
                else if (Mode == "unmute") modeText = Translations.ProfileModeUnmute(Lang);
                else modeText = Translations.ProfileModePtt(Lang);

                return App + "  →  " + modeText;
            }
        }

        private void BuildAutomationTab()
        {
            tabAutomation = new TabPage(Translations.Automation(config.AppLanguage));

            grpAutoLock = new GroupBox
            {
                Text = Translations.AutoMuteOnLock(config.AppLanguage),
                Location = new Point(10, 10),
                Size = new Size(405, 75)
            };

            chkAutoMuteOnLock = new CheckBox
            {
                Text = Translations.AutoMuteOnLock(config.AppLanguage),
                Location = new Point(15, 22),
                Size = new Size(370, 20),
                Checked = config.AutoMuteOnLock
            };

            lblAutoLockDesc = new Label
            {
                Text = Translations.AutoMuteOnLockDescription(config.AppLanguage),
                Location = new Point(15, 44),
                Size = new Size(375, 25),
                ForeColor = Color.Gray
            };

            grpAutoLock.Controls.Add(chkAutoMuteOnLock);
            grpAutoLock.Controls.Add(lblAutoLockDesc);

            grpLed = new GroupBox
            {
                Text = Translations.LedSync(config.AppLanguage),
                Location = new Point(10, 95),
                Size = new Size(405, 100)
            };

            chkLedSync = new CheckBox
            {
                Text = Translations.EnableLedSync(config.AppLanguage),
                Location = new Point(15, 22),
                Size = new Size(280, 20),
                Checked = config.LedSyncEnabled
            };
            chkLedSync.CheckedChanged += delegate(object s, EventArgs e)
            {
                cmbLedKey.Enabled = chkLedSync.Checked;
            };

            cmbLedKey = new ComboBox
            {
                Location = new Point(300, 20),
                Size = new Size(90, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = config.LedSyncEnabled
            };
            cmbLedKey.Items.Add(Keys.Scroll);
            cmbLedKey.Items.Add(Keys.NumLock);
            cmbLedKey.Items.Add(Keys.CapsLock);
            cmbLedKey.SelectedItem = config.LedSyncKey;
            if (cmbLedKey.SelectedIndex < 0) cmbLedKey.SelectedIndex = 0;

            lblLedWarning = new Label
            {
                Text = Translations.LedSyncWarning(config.AppLanguage),
                Location = new Point(15, 48),
                Size = new Size(375, 42),
                ForeColor = Color.Gray
            };

            grpLed.Controls.Add(chkLedSync);
            grpLed.Controls.Add(cmbLedKey);
            grpLed.Controls.Add(lblLedWarning);

            grpProfiles = new GroupBox
            {
                Text = Translations.AppProfiles(config.AppLanguage),
                Location = new Point(10, 205),
                Size = new Size(405, 195)
            };

            lblProfilesDesc = new Label
            {
                Text = Translations.AppProfilesDescription(config.AppLanguage),
                Location = new Point(15, 20),
                Size = new Size(375, 18),
                ForeColor = Color.Gray
            };

            lstProfiles = new ListBox
            {
                Location = new Point(15, 42),
                Size = new Size(245, 140)
            };

            cmbProfileApp = new ComboBox
            {
                Location = new Point(270, 42),
                Size = new Size(120, 21),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            FillRunningApps();

            cmbProfileMode = new ComboBox
            {
                Location = new Point(270, 70),
                Size = new Size(120, 21),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            FillProfileModes();

            btnAddProfile = new Button
            {
                Text = Translations.Add(config.AppLanguage),
                Location = new Point(270, 100),
                Size = new Size(120, 26)
            };
            btnAddProfile.Click += BtnAddProfile_Click;

            btnRemoveProfile = new Button
            {
                Text = Translations.Remove(config.AppLanguage),
                Location = new Point(270, 132),
                Size = new Size(120, 26)
            };
            btnRemoveProfile.Click += delegate(object s, EventArgs e)
            {
                if (lstProfiles.SelectedIndex >= 0)
                {
                    lstProfiles.Items.RemoveAt(lstProfiles.SelectedIndex);
                }
            };

            grpProfiles.Controls.Add(lblProfilesDesc);
            grpProfiles.Controls.Add(lstProfiles);
            grpProfiles.Controls.Add(cmbProfileApp);
            grpProfiles.Controls.Add(cmbProfileMode);
            grpProfiles.Controls.Add(btnAddProfile);
            grpProfiles.Controls.Add(btnRemoveProfile);

            tabAutomation.Controls.Add(grpAutoLock);
            tabAutomation.Controls.Add(grpLed);
            tabAutomation.Controls.Add(grpProfiles);

            LoadProfilesIntoList();
        }

        /// <summary>Laufende Anwendungen mit sichtbarem Fenster - spart das Tippen von Pfaden.</summary>
        private void FillRunningApps()
        {
            string previous = cmbProfileApp.Text;
            cmbProfileApp.Items.Clear();

            try
            {
                var names = new List<string>();
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        if (p.MainWindowHandle == IntPtr.Zero)
                            continue;

                        string name = p.ProcessName + ".exe";
                        if (!names.Contains(name))
                            names.Add(name);
                    }
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string n in names)
                    cmbProfileApp.Items.Add(n);
            }
            catch (Exception ex)
            {
                Program.Log("FillRunningApps", ex);
            }

            cmbProfileApp.Text = previous;
        }

        private void FillProfileModes()
        {
            object selected = cmbProfileMode.SelectedIndex >= 0 ? cmbProfileMode.SelectedIndex : (object)0;

            cmbProfileMode.Items.Clear();
            cmbProfileMode.Items.Add(Translations.ProfileModeMute(config.AppLanguage));
            cmbProfileMode.Items.Add(Translations.ProfileModeUnmute(config.AppLanguage));
            cmbProfileMode.Items.Add(Translations.ProfileModePtt(config.AppLanguage));
            cmbProfileMode.SelectedIndex = (int)selected;
        }

        private static string ModeFromIndex(int index)
        {
            if (index == 0) return "mute";
            if (index == 1) return "unmute";
            return "ptt";
        }

        private void BtnAddProfile_Click(object sender, EventArgs e)
        {
            string app = cmbProfileApp.Text.Trim();
            if (app.Length == 0)
                return;

            // Doppelte Regeln für dieselbe App vermeiden - die letzte gewinnt sonst stillschweigend
            for (int i = lstProfiles.Items.Count - 1; i >= 0; i--)
            {
                ProfileEntry existing = (ProfileEntry)lstProfiles.Items[i];
                if (string.Equals(existing.App, app, StringComparison.OrdinalIgnoreCase))
                {
                    lstProfiles.Items.RemoveAt(i);
                }
            }

            lstProfiles.Items.Add(new ProfileEntry
            {
                App = app,
                Mode = ModeFromIndex(cmbProfileMode.SelectedIndex),
                Lang = config.AppLanguage
            });
        }

        private void LoadProfilesIntoList()
        {
            lstProfiles.Items.Clear();

            if (string.IsNullOrEmpty(config.ProfileApps))
                return;

            foreach (string entry in config.ProfileApps.Split(';'))
            {
                string[] parts = entry.Split(':');
                if (parts.Length != 2)
                    continue;

                string app = parts[0].Trim();
                string mode = parts[1].Trim().ToLowerInvariant();

                if (app.Length > 0 && (mode == "mute" || mode == "unmute" || mode == "ptt"))
                {
                    lstProfiles.Items.Add(new ProfileEntry { App = app, Mode = mode, Lang = config.AppLanguage });
                }
            }
        }

        private string ProfilesToString()
        {
            var parts = new List<string>();
            foreach (object item in lstProfiles.Items)
            {
                parts.Add(((ProfileEntry)item).Raw);
            }
            return string.Join(";", parts.ToArray());
        }

        private void UpdateLanguage(Language newLanguage)
        {
            config.AppLanguage = newLanguage;

            this.Text = Translations.SettingsTitle(newLanguage);
            
            tabControl.TabPages[0].Text = newLanguage == Language.German ? "Allgemein" : "General";
            tabControl.TabPages[1].Text = Translations.GlobalHotkeys(newLanguage);
            tabControl.TabPages[2].Text = Translations.AdvancedSettings(newLanguage);
            tabAutomation.Text = Translations.Automation(newLanguage);

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

            grpToggle.Text = Translations.ToggleHotkey(newLanguage);
            chkEnableToggle.Text = Translations.EnableHotkey(newLanguage);
            lblToggleHotkey.Text = Translations.Hotkey(newLanguage);

            grpMute.Text = Translations.MuteHotkey(newLanguage);
            chkEnableMute.Text = Translations.EnableHotkey(newLanguage);
            lblMuteHotkey.Text = Translations.Hotkey(newLanguage);

            grpUnmute.Text = Translations.UnmuteHotkey(newLanguage);
            chkEnableUnmute.Text = Translations.EnableHotkey(newLanguage);
            lblUnmuteHotkey.Text = Translations.Hotkey(newLanguage);

            lblInfo.Text = Translations.HotkeyInfo(newLanguage);

            UpdateHotkeyDisplay(txtToggleHotkey, chkEnableToggle.Checked, toggleKey, toggleModifiers);
            UpdateHotkeyDisplay(txtMuteHotkey, chkEnableMute.Checked, muteKey, muteModifiers);
            UpdateHotkeyDisplay(txtUnmuteHotkey, chkEnableUnmute.Checked, unmuteKey, unmuteModifiers);
            UpdatePushToTalkDisplays();

            // Advanced tab translations
            grpPushToTalk.Text = Translations.PushToTalk(newLanguage);
            chkEnablePushToTalk.Text = Translations.EnableHotkey(newLanguage);
            toolTipPushToTalk.SetToolTip(btnAddPushToTalkKey, Translations.AddPushToTalkKey(newLanguage));
            foreach (Button btn in pushToTalkRemoveButtons)
                toolTipPushToTalk.SetToolTip(btn, Translations.RemovePushToTalkKey(newLanguage));
			lblPushToTalkDesc.Text = Translations.PushToTalkDescription(newLanguage);
            
            grpNotifications.Text = Translations.ToastNotifications(newLanguage);
            chkShowToastOnToggle.Text = Translations.ShowToastOnToggle(newLanguage);
            chkShowToastOnMute.Text = Translations.ShowToastOnMute(newLanguage);
            chkShowToastOnUnmute.Text = Translations.ShowToastOnUnmute(newLanguage);
            chkShowToastOnStartup.Text = Translations.ShowToastOnStartup(newLanguage);
            chkShowToastOnPushToTalk.Text = Translations.ShowToastOnPushToTalk(newLanguage);

            grpLogging.Text = Translations.Logging(newLanguage);
            chkLogging.Text = Translations.EnableLogging(newLanguage);

            grpAutoLock.Text = Translations.AutoMuteOnLock(newLanguage);
            chkAutoMuteOnLock.Text = Translations.AutoMuteOnLock(newLanguage);
            lblAutoLockDesc.Text = Translations.AutoMuteOnLockDescription(newLanguage);

            grpLed.Text = Translations.LedSync(newLanguage);
            chkLedSync.Text = Translations.EnableLedSync(newLanguage);
            lblLedWarning.Text = Translations.LedSyncWarning(newLanguage);

            grpProfiles.Text = Translations.AppProfiles(newLanguage);
            lblProfilesDesc.Text = Translations.AppProfilesDescription(newLanguage);
            btnAddProfile.Text = Translations.Add(newLanguage);
            btnRemoveProfile.Text = Translations.Remove(newLanguage);
            FillProfileModes();

            // Die Anzeige der Profilregeln enthält übersetzte Modusnamen
            foreach (object item in lstProfiles.Items)
            {
                ((ProfileEntry)item).Lang = newLanguage;
            }
            RefreshProfileList();

            btnOK.Text = Translations.OK(newLanguage);
            btnCancel.Text = Translations.Cancel(newLanguage);
        }

        private void RefreshProfileList()
        {
            int selected = lstProfiles.SelectedIndex;
            var items = new object[lstProfiles.Items.Count];
            lstProfiles.Items.CopyTo(items, 0);

            lstProfiles.Items.Clear();
            lstProfiles.Items.AddRange(items);

            if (selected >= 0 && selected < lstProfiles.Items.Count)
                lstProfiles.SelectedIndex = selected;
        }

        /// <summary>
        /// Baut die Zeilen der Push-to-Talk-Tasten aus pushToTalkBindings neu auf.
        /// Mindestens eine Zeile bleibt sichtbar, damit man eine Taste eintragen kann.
        /// </summary>
        private void RebuildPushToTalkRows()
        {
            if (pushToTalkBindings.Count == 0)
                pushToTalkBindings.Add(new HotkeyBinding(Keys.None, Keys.None));

            activeTextBox = null;
            pushToTalkBoxes.Clear();
            pushToTalkRemoveButtons.Clear();

            pnlPushToTalkKeys.SuspendLayout();
            while (pnlPushToTalkKeys.Controls.Count > 0)
                pnlPushToTalkKeys.Controls[0].Dispose();

            for (int i = 0; i < pushToTalkBindings.Count; i++)
            {
                Panel row = new Panel { Size = new Size(350, 27), Margin = new Padding(0) };

                TextBox txt = new TextBox
                {
                    Location = new Point(0, 2),
                    Size = new Size(312, 23),
                    ReadOnly = true,
                    Tag = "pushtotalk"
                };
                txt.Enter += delegate(object s, EventArgs e) { activeTextBox = (TextBox)s; };
                txt.KeyDown += TxtHotkey_KeyDown;
                txt.PreviewKeyDown += TxtHotkey_PreviewKeyDown;

                Button btnRemove = new Button
                {
                    Text = "-",
                    Location = new Point(318, 1),
                    Size = new Size(28, 25)
                };
                toolTipPushToTalk.SetToolTip(btnRemove, Translations.RemovePushToTalkKey(config.AppLanguage));
                btnRemove.Click += delegate(object s, EventArgs e)
                {
                    Button clicked = (Button)s;
                    int index = pushToTalkRemoveButtons.IndexOf(clicked);
                    if (index < 0)
                        return;

                    // Erst nach dem Click-Ereignis neu aufbauen: der Button darf sich
                    // nicht in seinem eigenen Handler entsorgen. Doppelklick abfangen.
                    clicked.Enabled = false;
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        pushToTalkBindings.RemoveAt(index);
                        RebuildPushToTalkRows();
                    });
                };

                row.Controls.Add(txt);
                row.Controls.Add(btnRemove);
                pnlPushToTalkKeys.Controls.Add(row);

                pushToTalkBoxes.Add(txt);
                pushToTalkRemoveButtons.Add(btnRemove);
            }
            pnlPushToTalkKeys.ResumeLayout();

            UpdatePushToTalkEnabled();
        }

        private void UpdatePushToTalkEnabled()
        {
            // Wird auch während des Aufbaus aufgerufen
            if (pnlPushToTalkKeys == null || btnAddPushToTalkKey == null)
                return;

            pnlPushToTalkKeys.Enabled = chkEnablePushToTalk.Checked;
            btnAddPushToTalkKey.Enabled = chkEnablePushToTalk.Checked;
            UpdatePushToTalkDisplays();
        }

        private void UpdatePushToTalkDisplays()
        {
            for (int i = 0; i < pushToTalkBoxes.Count; i++)
            {
                UpdateHotkeyDisplay(pushToTalkBoxes[i], chkEnablePushToTalk.Checked,
                    pushToTalkBindings[i].Key, pushToTalkBindings[i].Modifiers);
            }
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
            int pttIndex = pushToTalkBoxes.IndexOf(activeTextBox);

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
                else if (pttIndex >= 0)
                {
                    pushToTalkBindings[pttIndex] = new HotkeyBinding(Keys.None, Keys.None);
                    UpdateHotkeyDisplay(activeTextBox, chkEnablePushToTalk.Checked, Keys.None, Keys.None);
                }
                return;
            }

            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu)
            {
                return;
            }

            // Copilot-Taste: Windows sendet LWin + LShift + F23. Win ist in Keys kein
            // Modifier und als Hotkey ungeeignet: übergehen, die Folgetaste zählt.
            if (key == Keys.LWin || key == Keys.RWin)
                return;

            Keys modifiers = Keys.None;
            if (e.Control)
                modifiers |= Keys.Control;
            if (e.Shift)
                modifiers |= Keys.Shift;
            if (e.Alt)
                modifiers |= Keys.Alt;

            // Mit gehaltener Win-Taste (Copilot-Taste) nur die Taste selbst übernehmen:
            // F23 statt "Shift + F23". Der Hook prüft nur geforderte Modifier.
            if (Program.IsKeyDownNow(Keys.LWin) || Program.IsKeyDownNow(Keys.RWin))
                modifiers = Keys.None;

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
            else if (pttIndex >= 0)
            {
                pushToTalkBindings[pttIndex] = new HotkeyBinding(key, modifiers);
                UpdateHotkeyDisplay(activeTextBox, chkEnablePushToTalk.Checked, key, modifiers);
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

        /// <summary>
        /// Ein globaler Hotkey ohne Modifier schluckt diese Taste systemweit. Nur für
        /// Tasten erlaubt, die beim Schreiben ohnehin nicht vorkommen. Push-to-Talk sperrt
        /// seine Tasten bewusst (sie wirken nur noch als Push-to-Talk): dort ist jede Taste erlaubt.
        /// </summary>
        private static bool AllowsStandaloneKey(Keys key, bool pushToTalk)
        {
            return pushToTalk
                || (key >= Keys.F13 && key <= Keys.F24)
                || key == Keys.Pause
                || key == Keys.Scroll;
        }

        private bool ValidateHotkeys()
        {
            var active = new List<Keys>();
            // Tasten der Hotkeys; die Hotkeys stehen in candidates vor Push-to-Talk
            var hotkeyKeys = new List<Keys>();

            var candidates = new[]
            {
                new { On = chkEnableToggle.Checked, Key = toggleKey, Mod = toggleModifiers, Ptt = false },
                new { On = chkEnableMute.Checked,   Key = muteKey,   Mod = muteModifiers,   Ptt = false },
                new { On = chkEnableUnmute.Checked, Key = unmuteKey, Mod = unmuteModifiers, Ptt = false }
            }.Concat(pushToTalkBindings.Select(b =>
                new { On = chkEnablePushToTalk.Checked, Key = b.Key, Mod = b.Modifiers, Ptt = true }));

            foreach (var c in candidates)
            {
                if (!c.On || c.Key == Keys.None)
                    continue;

                if (c.Mod == Keys.None && !AllowsStandaloneKey(c.Key, c.Ptt))
                {
                    MessageBox.Show(Translations.HotkeyNeedsModifier(config.AppLanguage),
                        "MicMute", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                Keys combo = c.Key | c.Mod;
                // Push-to-Talk ohne Modifier sperrt die Taste auch mit Strg/Umschalt/Alt
                // (CheckModifiers duldet zusätzliche Modifier): dann kein Hotkey auf derselben Taste
                bool sharesHotkeyKey = c.Ptt && c.Mod == Keys.None && hotkeyKeys.Contains(c.Key);
                if (active.Contains(combo) || sharesHotkeyKey)
                {
                    MessageBox.Show(Translations.HotkeyDuplicate(config.AppLanguage),
                        "MicMute", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                active.Add(combo);
                if (!c.Ptt)
                    hotkeyKeys.Add(c.Key);
            }

            return true;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (!ValidateHotkeys())
            {
                // Dialog offen lassen, damit die Eingabe korrigiert werden kann
                this.DialogResult = DialogResult.None;
                return;
            }

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
            // Leere Zeilen werden nicht gespeichert
            config.PushToTalkKeys = pushToTalkBindings.Where(b => b.Key != Keys.None).ToList();
            
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

            config.LoggingEnabled = chkLogging.Checked;
            config.AutoMuteOnLock = chkAutoMuteOnLock.Checked;
            config.LedSyncEnabled = chkLedSync.Checked;
            config.LedSyncKey = cmbLedKey.SelectedItem is Keys ? (Keys)cmbLedKey.SelectedItem : Keys.Scroll;
            config.ProfileApps = ProfilesToString();

            Config.SetAutostart(config.AutostartEnabled);
        }

        public Config GetConfig()
        {
            return config;
        }
    }
}