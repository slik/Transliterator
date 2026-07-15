using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Translit;

public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private const uint LLKHF_INJECTED = 0x10;

    private const int VK_BACK = 0x08;
    private const int VK_SHIFT = 0x10;
    private const int VK_CAPITAL = 0x14;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;

    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;

    private readonly Transliterator _core = new();

    private readonly Thread _hookThread;
    private uint _hookThreadId;

    private readonly ManualResetEventSlim _installEvent = new(false);
    private bool _installOk;
    private int _installError;

    private readonly BlockingCollection<Emit> _queue = new(new ConcurrentQueue<Emit>());
    private readonly Thread _injectThread;

    private readonly INPUT[] _injectBuf = new INPUT[64];

    private bool _disposed;

    private volatile bool _enabled;

    public bool Enabled {
        get => _enabled;
        set => _enabled = value;
    }

    public KeyboardHook()
    {
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;

        _hookThread = new Thread(HookThreadProc) {
            IsBackground = true,
            Name = "Translit-Hook",
        };
        _hookThread.Start();

        if (!_installEvent.Wait(5000) || !_installOk) {
            _hookThread.Join(1000);
            throw new InvalidOperationException($"Failed to install hooks (win32 error {_installError}).");
        }

        _injectThread = new Thread(InjectLoop) {
            IsBackground = true,
            Name = "Translit-Inject",
        };
        _injectThread.Start();
    }

    private void HookThreadProc()
    {
        var hMod = GetModuleHandleForHook();
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);

        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero) {
            _installError = Marshal.GetLastWin32Error();

            if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);

            _keyboardHook = _mouseHook = IntPtr.Zero;
            _installOk = false;
            _installEvent.Set();

            return;
        }

        _hookThreadId = GetCurrentThreadId();
        _installOk = true;
        _installEvent.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_keyboardHook != IntPtr.Zero) {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero) {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private void InjectLoop()
    {
        foreach (var emit in _queue.GetConsumingEnumerable()) {
            try {
                Inject(emit);
            } catch {
                /* nothing */
            }
        }
    }

    private static IntPtr GetModuleHandleForHook()
    {
        using var cur = Process.GetCurrentProcess();
        var moduleName = cur.MainModule?.ModuleName;
        return moduleName is null ? IntPtr.Zero : GetModuleHandle(moduleName);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try {
            if (nCode < 0)
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            var vk = (uint)Marshal.ReadInt32(lParam, 0);
            var flags = (uint)Marshal.ReadInt32(lParam, 8);

            if ((flags & LLKHF_INJECTED) != 0) {
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (!Enabled) {
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var msg = (int)wParam;
            if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN) {
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN)) {
                _core.Reset();
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            if (vk == VK_BACK) {
                _core.Undo();
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            var caps = (GetKeyState(VK_CAPITAL) & 0x1) != 0;
            var decoded = VkToChar(vk, shift ^ caps, shift);

            if (decoded is null) {
                _core.Reset();
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            var ch = decoded.Value;
            var emit = _core.ProcessChar(ch);

            if (emit.PassThrough) {
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            try {
                _queue.Add(emit);
            } catch (InvalidOperationException) {
                /* nothing */
            }

            return (IntPtr)1;
        } catch {
            try {
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            } catch {
                return IntPtr.Zero;
            }
        }
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try {
            if (nCode >= 0) {
                var msg = (int)wParam;
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN) {
                    _core.Reset();
                }
            }
        } catch {
            /* nothing */
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static char? VkToChar(uint vk, bool upperLetters, bool shift)
    {
        // A..Z -> a..z
        if (vk >= 0x41 && vk <= 0x5A) {
            var c = (char)('a' + (int)(vk - 0x41));
            return upperLetters ? char.ToUpperInvariant(c) : c;
        }

        switch (vk) {
            case 0xC0: return shift ? '~' : '`';
            case 0xDE: return shift ? '"' : '\'';
            default: return null;
        }
    }

    private void Inject(Emit emit)
    {
        var count = emit.Backspaces * 2 + emit.Text.Length * 2;
        if (count == 0) return;
        
        var inputs = count <= _injectBuf.Length ? _injectBuf : new INPUT[count];
        var i = 0;

        for (var b = 0; b < emit.Backspaces; b++) {
            inputs[i++] = KeyInput(VK_BACK, 0, 0);
            inputs[i++] = KeyInput(VK_BACK, 0, KEYEVENTF_KEYUP);
        }

        foreach (var c in emit.Text) {
            inputs[i++] = KeyInput(0, c, KEYEVENTF_UNICODE);
            inputs[i++] = KeyInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
        }

        SendInput((uint)count, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort vk, ushort scan, uint flags) => new() {
        type = INPUT_KEYBOARD,
        U = new InputUnion {
            ki = new KEYBDINPUT {
                wVk = vk,
                wScan = scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _hookThread?.Join(1000);

        if (_keyboardHook != IntPtr.Zero) {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero) {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _queue.CompleteAdding();
        _injectThread?.Join(1000);
        _queue.Dispose();

        _installEvent.Dispose();
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}