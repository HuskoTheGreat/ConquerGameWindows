using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Conquer.Launcher
{
    /// <summary>
    /// The launcher's only UI: a small borderless progress box while it downloads, and an error box.
    /// Plain Win32 keeps the launcher a few megabytes instead of pulling in a UI framework.
    /// </summary>
    static class Win32
    {
        const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_BORDER = 0x00800000;
        const uint SS_CENTER = 0x1, SS_CENTERIMAGE = 0x200;
        const uint WS_EX_APPWINDOW = 0x40000, WS_EX_TOPMOST = 0x8;
        const uint WM_SETFONT = 0x30, PM_REMOVE = 0x1, MB_ICONERROR = 0x10;
        const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, DEFAULT_GUI_FONT = 17;

        /// <summary>Shows a progress box until <paramref name="work"/> finishes. Does nothing off Windows.</summary>
        public static void RunSplash(Task work, Func<string> text)
        {
            if (!OperatingSystem.IsWindows())
            {
                work.Wait();
                return;
            }

            const int width = 360, height = 90;
            int x = (GetSystemMetrics(SM_CXSCREEN) - width) / 2;
            int y = (GetSystemMetrics(SM_CYSCREEN) - height) / 2;
            string shown = text();
            IntPtr hwnd = CreateWindowExW(WS_EX_APPWINDOW | WS_EX_TOPMOST, "STATIC", shown,
                WS_POPUP | WS_VISIBLE | WS_BORDER | SS_CENTER | SS_CENTERIMAGE,
                x, y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                work.Wait();
                return;
            }
            SendMessageW(hwnd, WM_SETFONT, GetStockObject(DEFAULT_GUI_FONT), (IntPtr)1);

            while (!work.IsCompleted)
            {
                while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
                string now = text();
                if (now != shown) SetWindowTextW(hwnd, shown = now);
                Thread.Sleep(30);
            }
            DestroyWindow(hwnd);
        }

        public static void ShowError(string title, string message)
        {
            if (OperatingSystem.IsWindows()) MessageBoxW(IntPtr.Zero, message, title, MB_ICONERROR);
            else Console.Error.WriteLine(message);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int ptX, ptY;
            public uint lPrivate;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SetWindowTextW(IntPtr hwnd, string text);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool PeekMessageW(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG msg);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int obj);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
    }
}
