using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace gInk
{
    /// <summary>
    /// Receives binary input frames from WebSocket clients (phone app)
    /// and injects them into the Windows input system.
    /// </summary>
    public class MobileInputHandler
    {
        public static MobileInputHandler Instance;
        private Root Root;

        // --- SendInput (system-level cursor control) ---
        public struct INPUT { public uint type; public MOUSEINPUT mi; }
        public struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint MOUSEEVENTF_VIRTUALDESK = 0x00040000;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // --- SendMessage (target the ppInk FormCollection window) ---
        const uint WM_LBUTTONDOWN = 0x0101;
        const uint WM_LBUTTONUP = 0x0102;
        const uint WM_MOUSEMOVE = 0x0200;
        const uint MK_LBUTTON = 0x0001;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public MobileInputHandler(Root root) { Root = root; Instance = this; }

        public void ProcessFrame(MobileSession sess, byte[] frame)
        {
            if (frame.Length < 1) return;
            byte msgType = frame[0];
            switch (msgType)
            {
                case 0x01: // TouchDown
                case 0x02: // TouchMove
                case 0x03: // TouchUp
                    HandleTouch(sess, msgType, frame);
                    break;
                case 0x07: // ModeSwitch  0=Drawing 1=Cursor
                    if (frame.Length >= 2) sess.Mode = (MobileInputMode)frame[1];
                    break;
            }
        }

        // Map normalized 0..1 phone coords to PC screen coords per mapping config
        private Point MapToScreen(float nx, float ny)
        {
            var vs = SystemInformation.VirtualScreen;
            if (Root.MobileInput_Mapping == "AnnotationArea" && Root.WindowRect.Width > 0)
            {
                return new Point(
                    Root.WindowRect.Left + (int)(nx * Root.WindowRect.Width),
                    Root.WindowRect.Top + (int)(ny * Root.WindowRect.Height));
            }
            if (Root.MobileInput_Mapping == "Custom")
            {
                return new Point(
                    (int)(Root.MobileInput_CustomX + nx * Root.MobileInput_CustomWidth),
                    (int)(Root.MobileInput_CustomY + ny * Root.MobileInput_CustomHeight));
            }
            return new Point(
                (int)(nx * vs.Width),
                (int)(ny * vs.Height));
        }

        private void HandleTouch(MobileSession sess, byte msgType, byte[] frame)
        {
            if (Root.FormCollection == null) return;
            if (sess.Mode == MobileInputMode.Cursor)
                HandleCursor(sess, msgType, frame);
            else
                HandleDrawing(sess, msgType, frame);
        }

        // ---- Drawing mode: SendInput → InkOverlay receives mouse as ink ----
        private void HandleDrawing(MobileSession sess, byte msgType, byte[] frame)
        {
            if (frame.Length < 5) return;
            float nx = BitConverter.ToSingle(frame, 1);
            float ny = BitConverter.ToSingle(frame, 5);
            Point pt = MapToScreen(nx, ny);

            var vs = SystemInformation.VirtualScreen;
            uint normX = (uint)(pt.X / (double)vs.Width * 65535);
            uint normY = (uint)(pt.Y / (double)vs.Height * 65535);

            if (msgType == 0x01) // TouchDown
            {
                // Auto-start inking if not yet active
                if (!Root.FormCollection.Visible)
                    Root.StartInk();

                INPUT[] inputs = new INPUT[2];
                inputs[0].type = INPUT_MOUSE;
                inputs[0].mi.dx = (int)normX; inputs[0].mi.dy = (int)normY;
                inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
                inputs[1].type = INPUT_MOUSE;
                inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
                SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            }
            else if (msgType == 0x02) // TouchMove
            {
                INPUT input = new INPUT();
                input.type = INPUT_MOUSE;
                input.mi.dx = (int)normX; input.mi.dy = (int)normY;
                input.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
                SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            }
            else if (msgType == 0x03) // TouchUp
            {
                INPUT input = new INPUT();
                input.type = INPUT_MOUSE;
                input.mi.dwFlags = MOUSEEVENTF_LEFTUP;
                SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            }
        }

        // ---- Cursor mode: system cursor control ----
        private void HandleCursor(MobileSession sess, byte msgType, byte[] frame)
        {
            if (frame.Length < 5) return;
            float nx = BitConverter.ToSingle(frame, 1);
            float ny = BitConverter.ToSingle(frame, 5);
            Point pt = MapToScreen(nx, ny);

            var vs = SystemInformation.VirtualScreen;
            uint normX = (uint)(pt.X / (double)vs.Width * 65535);
            uint normY = (uint)(pt.Y / (double)vs.Height * 65535);

            if (msgType == 0x01) // TouchDown -> left down
            {
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = INPUT_MOUSE;
                inputs[0].mi.dx = (int)normX; inputs[0].mi.dy = (int)normY;
                inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
                inputs[1].type = INPUT_MOUSE;
                inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
                SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            }
            else if (msgType == 0x02) // TouchMove -> cursor move
            {
                INPUT input = new INPUT();
                input.type = INPUT_MOUSE;
                input.mi.dx = (int)normX; input.mi.dy = (int)normY;
                input.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
                SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            }
            else if (msgType == 0x03) // TouchUp -> left up
            {
                INPUT input = new INPUT();
                input.type = INPUT_MOUSE;
                input.mi.dwFlags = MOUSEEVENTF_LEFTUP;
                SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            }
        }
    }
}
