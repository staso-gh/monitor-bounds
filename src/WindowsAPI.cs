using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace MonitorBounds
{
    
    // Provides access to Windows API functions for window management and monitoring
    
    public static class WindowsAPI
    {
        #region Window Constants
        public const int WM_MOVE = 0x0003;
        public const int WM_SIZE = 0x0005;
        public const int WH_CALLWNDPROC = 4;
        public const int GWLP_WNDPROC = -4;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        
        // Mouse input constants
        public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const int MOUSEEVENTF_LEFTUP = 0x0004;
        
        // Virtual key codes
        public const int VK_LBUTTON = 0x01; // Left mouse button
        #endregion

        // Static cached resources to reduce allocations
        private static readonly INPUT[] _cachedInputs = new INPUT[1];
        private static readonly Dictionary<int, System.Text.StringBuilder> _stringBuilderCache = 
            new Dictionary<int, System.Text.StringBuilder>();
        
        // Non-readonly static field for monitor info since we need to modify it
        private static MONITORINFO _monitorInfoCache;
        
        // Thread-local string builder cache for window title retrieval
        [ThreadStatic]
        private static System.Text.StringBuilder _threadLocalStringBuilder;

        #region Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }
        #endregion

        #region Monitor Constants
        public const int MONITORINFOF_PRIMARY = 1;
        public const int MONITOR_DEFAULTTONEAREST = 2;
        #endregion

        #region Delegates
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
        #endregion

        #region API Functions
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        // Added: For window class name retrieval
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        // Mouse cursor functions
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetCursorPos(int X, int Y);

        // Monitor functions
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        // Mouse input functions
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        #endregion


        // Gets the title of a window

        public static string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0) return string.Empty;

            // Get or create the thread-local StringBuilder
            if (_threadLocalStringBuilder == null)
            {
                _threadLocalStringBuilder = new System.Text.StringBuilder(256);
            }
            
            // Ensure the StringBuilder has enough capacity
            if (_threadLocalStringBuilder.Capacity < length + 1)
            {
                _threadLocalStringBuilder.Capacity = length + 1;
            }
            
            // Clear the StringBuilder (faster than creating a new one)
            _threadLocalStringBuilder.Length = 0;
            
            GetWindowText(hWnd, _threadLocalStringBuilder, _threadLocalStringBuilder.Capacity);
            return _threadLocalStringBuilder.ToString();
        }

        // Gets the class name of a window
        public static string GetClassNameFromHandle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            // Get or create the thread-local StringBuilder
            if (_threadLocalStringBuilder == null)
            {
                _threadLocalStringBuilder = new System.Text.StringBuilder(256);
            }
            
            // Clear the StringBuilder (faster than creating a new one)
            _threadLocalStringBuilder.Length = 0;
            
            int length = GetClassName(hWnd, _threadLocalStringBuilder, _threadLocalStringBuilder.Capacity);
            if (length == 0)
            {
                int error = Marshal.GetLastWin32Error();
                return string.Empty;
            }
            
            return _threadLocalStringBuilder.ToString();
        }

        // Get information about the monitor

        public static MONITORINFO GetMonitorInfo(IntPtr hMonitor)
        {
            // Initialize the monitor info struct
            _monitorInfoCache.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            
            // Get the monitor info
            GetMonitorInfo(hMonitor, ref _monitorInfoCache);
            
            // Return a copy of the cached value
            return _monitorInfoCache;
        }


        // Gets the monitor that contains the specified point

        public static IntPtr GetMonitorFromPoint(int x, int y)
        {
            POINT pt = new POINT { X = x, Y = y };
            return MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        }


        // Simulates releasing the left mouse button

        public static void SimulateLeftMouseButtonUp()
        {
            // Use cached INPUT array to avoid repeated allocations
            _cachedInputs[0].type = 0; // INPUT_MOUSE
            _cachedInputs[0].mi.dx = 0;
            _cachedInputs[0].mi.dy = 0;
            _cachedInputs[0].mi.mouseData = 0;
            _cachedInputs[0].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            _cachedInputs[0].mi.time = 0;
            _cachedInputs[0].mi.dwExtraInfo = GetMessageExtraInfo();
            
            SendInput(1, _cachedInputs, Marshal.SizeOf(typeof(INPUT)));
        }
        

        // Releases the mouse capture on a window

        public static void ReleaseMouseCapture()
        {
            ReleaseCapture();
        }


        // Checks if the left mouse button is currently pressed

        public static bool IsLeftMouseButtonPressed()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }
        

        // Stops a window drag operation by releasing mouse capture and simulating a mouse button up event

        public static void StopWindowDrag(IntPtr windowHandle)
        {
            ReleaseMouseCapture();
            SimulateLeftMouseButtonUp();
        }
    }
} 
