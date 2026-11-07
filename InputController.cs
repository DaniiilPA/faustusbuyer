using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExileCore;
using SharpDX;

namespace faustusbuyer
{
    public class InputController
    {
        private readonly GameController _gameController;
        private readonly Random _random = new Random();
        private readonly int _screenWidth;
        private readonly int _screenHeight;

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUT
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(8)] public MOUSEINPUT mi;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        public InputController(GameController gameController)
        {
            _gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
            _screenWidth = Screen.PrimaryScreen.Bounds.Width;
            _screenHeight = Screen.PrimaryScreen.Bounds.Height;
        }

        /// <summary>
        /// Гарантирует, что окно PoE находится на переднем плане перед отправкой ввода,
        /// даже если сейчас активно другое окно (браузер и т.д.).
        /// </summary>
        public bool EnsureForeground()
        {
            if (_gameController.Window == null) return false;
            if (_gameController.Window.IsForeground()) return true;

            try
            {
                var handle = _gameController.Window.Process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    IntPtr fgWindow = GetForegroundWindow();
                    uint fgThreadId = GetWindowThreadProcessId(fgWindow, IntPtr.Zero);
                    uint curThreadId = GetCurrentThreadId();

                    if (fgThreadId != 0 && fgThreadId != curThreadId)
                    {
                        AttachThreadInput(curThreadId, fgThreadId, true);
                        SetForegroundWindow(handle);
                        AttachThreadInput(curThreadId, fgThreadId, false);
                    }
                    else
                    {
                        SetForegroundWindow(handle);
                    }

                    System.Threading.Thread.Sleep(50);
                    return _gameController.Window.IsForeground();
                }
            }
            catch { }

            return _gameController.Window.IsForeground();
        }

        public bool MoveMouse(Vector2 screenRelativePos)
        {
            if (!EnsureForeground())
                return false;

            var windowRect = _gameController.Window.GetWindowRectangle();
            if (windowRect.Width <= 100 || windowRect.Height <= 100)
                return false;

            // Антифрод-рандомизация: смещение на ±2..3 пикселя от идеального центра
            float jitterX = (float)(_random.NextDouble() * 6.0 - 3.0);
            float jitterY = (float)(_random.NextDouble() * 6.0 - 3.0);

            int targetX = (int)(windowRect.X + screenRelativePos.X + jitterX);
            int targetY = (int)(windowRect.Y + screenRelativePos.Y + jitterY);

            // Прямая установка аппаратного курсора по точным экранным пикселям (работает на любых мониторах и DPI)
            SetCursorPos(targetX, targetY);
            return true;
        }

        public bool ClickScreenPos(Vector2 screenRelativePos, bool ctrlClick)
        {
            if (!MoveMouse(screenRelativePos))
                return false;

            // Небольшая задержка после наведения мыши (имитация наведения человека)
            System.Threading.Thread.Sleep(_random.Next(40, 75));

            if (ctrlClick)
            {
                KeyDown(Keys.ControlKey);
                System.Threading.Thread.Sleep(_random.Next(25, 45));
            }

            INPUT[] clickDown = {
                new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } }
            };
            SendInput((uint)clickDown.Length, clickDown, Marshal.SizeOf(typeof(INPUT)));

            System.Threading.Thread.Sleep(_random.Next(35, 65));

            INPUT[] clickUp = {
                new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } }
            };
            SendInput((uint)clickUp.Length, clickUp, Marshal.SizeOf(typeof(INPUT)));

            if (ctrlClick)
            {
                System.Threading.Thread.Sleep(_random.Next(25, 45));
                KeyUp(Keys.ControlKey);
            }

            return true;
        }

        public void KeyDown(Keys key)
        {
            INPUT[] input = {
                new INPUT {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = 0 }
                }
            };
            SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT)));
        }

        public void KeyUp(Keys key)
        {
            INPUT[] input = {
                new INPUT {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = (ushort)key, dwFlags = KEYEVENTF_KEYUP }
                }
            };
            SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT)));
        }

        public void KeyPress(Keys key)
        {
            KeyDown(key);
            System.Threading.Thread.Sleep(_random.Next(25, 50));
            KeyUp(key);
        }

        public bool SendChatCommand(string command)
        {
            if (!EnsureForeground())
                return false;

            // Повторные попытки установки текста в буфер (защита от временной блокировки буфера Windows)
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetText(command);
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }

            // 1. Открыть чат (Enter)
            KeyPress(Keys.Return);
            System.Threading.Thread.Sleep(_random.Next(45, 75));

            // 2. Вставить команду (Ctrl + V)
            KeyDown(Keys.ControlKey);
            KeyPress(Keys.V);
            KeyUp(Keys.ControlKey);
            System.Threading.Thread.Sleep(_random.Next(35, 65));

            // 3. Отправить (Enter)
            KeyPress(Keys.Return);
            return true;
        }

        public void PressEscape()
        {
            EnsureForeground();
            KeyPress(Keys.Escape);
        }

        public string ReadClipboardText()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        return Clipboard.GetText();
                    }
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
            return string.Empty;
        }

        public void ClearClipboard()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.Clear();
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}
