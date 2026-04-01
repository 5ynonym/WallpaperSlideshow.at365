using Microsoft.Win32;

namespace at365.WallpaperSlideshow
{
    internal class WndProcForm : Form
    {
        private const int WM_DPICHANGED = 0x02E0;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_DEVICECHANGE = 0x0219;
        private const int WM_WTSSESSION_CHANGE = 0x02B1;

        private const int WTS_SESSION_REMOTE_CONNECT = 0x0003;
        private const int WTS_SESSION_REMOTE_DISCONNECT = 0x0004;

        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;

        public WndProcForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
            Width = 0;
            Height = 0;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_QUERYENDSESSION:
                    m.Result = 1;
                    return;

                case WM_ENDSESSION:
                    if (m.WParam != IntPtr.Zero)
                    {
                        OnShutdown();
                    }
                    break;
                case WM_DPICHANGED:
                case WM_DISPLAYCHANGE:
                case WM_DEVICECHANGE:
                    OnReinitialize();
                    break;

                case WM_WTSSESSION_CHANGE:
                    int code = m.WParam.ToInt32();
                    if (code == WTS_SESSION_REMOTE_CONNECT || code == WTS_SESSION_REMOTE_DISCONNECT)
                    {
                        Task.Delay(3000).Wait();
                        OnReinitialize();
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    OnScreenLocked();
                    break;

                case SessionSwitchReason.SessionUnlock:
                    OnScreenUnlocked();
                    break;
            }
        }

        private void OnReinitialize()
        {
            Program.InitializeApplication();
        }

        private void OnShutdown()
        {
            Program.ApplicationShutdown();
        }

        private void OnScreenLocked()
        {
            Program.TogglePause(true);
        }

        private void OnScreenUnlocked()
        {
            Program.TogglePause(false);
        }
    }

}
