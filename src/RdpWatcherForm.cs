using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.ComponentModel;

namespace at365.WallpaperSlideshow
{
    public class RdpWatcherForm : Form
    {
        private const int WM_WTSSESSION_CHANGE = 0x02B1;
        private const int WTS_SESSION_REMOTE_CONNECT = 0x03;
        private const int WTS_SESSION_REMOTE_DISCONNECT = 0x04;

        [DllImport("wtsapi32.dll")]
        private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);
        [DllImport("wtsapi32.dll")]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

        private static CancellationTokenSource? _cts;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Action? OnRdpConnect { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Action? OnRdpDisconnect { get; set; }

        public RdpWatcherForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WTSRegisterSessionNotification(Handle, 0);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            WTSUnRegisterSessionNotification(Handle);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_WTSSESSION_CHANGE)
            {
                int code = m.WParam.ToInt32();

                if (code == WTS_SESSION_REMOTE_CONNECT)
                    OnRdpConnect?.Invoke();

                if (code == WTS_SESSION_REMOTE_DISCONNECT)
                    OnRdpDisconnect?.Invoke();
            }

            base.WndProc(ref m);
        }
    }
}
