using System;
using System.Runtime.InteropServices;

namespace RefinedStorage.Ime
{
    public static class NativeImeSuppressor
    {
        private static readonly object SyncRoot = new object();
        private static IntPtr _savedImc;
        private static IntPtr _suppressedHwnd;

        public static bool IsSuppressed { get; private set; }

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_StopTextInput();

        [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_StartTextInput();

        public static void Suppress()
        {
            // 独立逻辑，无上文
            // 作用：在 Windows 上通过当前线程活动 HWND 剥离系统 IMM 上下文并停止 SDL 文本输入，避免原生输入法候选窗弹出
            lock (SyncRoot)
            {
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        IntPtr hwnd = GetActiveWindow();
                        if (hwnd != IntPtr.Zero)
                        {
                            if (!IsSuppressed)
                            {
                                _savedImc = ImmAssociateContext(hwnd, IntPtr.Zero);
                                _suppressedHwnd = hwnd;
                            }
                            else if (hwnd != _suppressedHwnd)
                            {
                                if (_suppressedHwnd != IntPtr.Zero)
                                {
                                    _ = ImmAssociateContext(_suppressedHwnd, _savedImc);
                                }

                                _savedImc = ImmAssociateContext(hwnd, IntPtr.Zero);
                                _suppressedHwnd = hwnd;
                            }
                            else
                            {
                                _ = ImmAssociateContext(hwnd, IntPtr.Zero);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    SDL_StopTextInput();
                }
                catch
                {
                }

                IsSuppressed = true;
            }
        }

        public static void Restore()
        {
            // 与上文：承接 Suppress 暂存的 _suppressedHwnd 与 _savedImc
            // 作用：无条件将原 IMM 上下文恢复到原窗口句柄、清理内部状态并重新开启 SDL 文本输入
            lock (SyncRoot)
            {
                if (!IsSuppressed)
                {
                    return;
                }

                if (OperatingSystem.IsWindows() && _suppressedHwnd != IntPtr.Zero)
                {
                    try
                    {
                        _ = ImmAssociateContext(_suppressedHwnd, _savedImc);
                    }
                    catch
                    {
                    }
                }

                _suppressedHwnd = IntPtr.Zero;
                _savedImc = IntPtr.Zero;

                try
                {
                    SDL_StartTextInput();
                }
                catch
                {
                }

                IsSuppressed = false;
            }
        }
    }
}
