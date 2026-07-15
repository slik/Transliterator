using System.Runtime.InteropServices;

namespace Translit;

public sealed class HotKey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;
    private const int HOTKEY_ID_PAUSE = 2;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_U = 0x55;
    private const uint VK_PAUSE = 0x13;

    private readonly MessageWindow _window;
    private bool _registered;
    private bool _registeredPause;
    private bool _disposed;

    public event Action? Pressed;

    public HotKey()
    {
        _window = new MessageWindow(OnHotKey);
        _registered = RegisterHotKey(_window.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_U);
        _registeredPause = RegisterHotKey(_window.Handle, HOTKEY_ID_PAUSE, MOD_NOREPEAT, VK_PAUSE);
    }

    private void OnHotKey() => Pressed?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (_registered) {
            UnregisterHotKey(_window.Handle, HOTKEY_ID);
            _registered = false;
        }

        if (_registeredPause) {
            UnregisterHotKey(_window.Handle, HOTKEY_ID_PAUSE);
            _registeredPause = false;
        }

        _window.DestroyHandle();
    }

    private sealed class MessageWindow : NativeWindow
    {
        private const int HWND_MESSAGE = -3; // message-only window: no display, no focus

        private readonly Action _onHotKey;

        public MessageWindow(Action onHotKey)
        {
            _onHotKey = onHotKey;
            var cp = new CreateParams { Parent = new IntPtr(HWND_MESSAGE) };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && ((int)m.WParam == HOTKEY_ID || (int)m.WParam == HOTKEY_ID_PAUSE)) {
                _onHotKey();
            }

            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}