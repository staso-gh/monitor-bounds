using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Threading;
using System.Timers;
using MonitorBounds.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Windows.Forms;  // Requires reference to System.Windows.Forms

namespace MonitorBounds.Services
{
    /// <summary>
    /// A simplified service that monitors specified application windows and forces them to remain within a designated monitor's bounds.
    /// </summary>
    public class WindowMonitorService : IDisposable
    {
        private readonly List<ApplicationWindow> _targetApplications = new();
        private readonly System.Timers.Timer _monitorTimer;
        private readonly Dictionary<IntPtr, NativeMethods.RECT> _lastWindowPositions = new();
        private bool _disposed = false;
        private const int POLLING_INTERVAL_MS = 50;

        public event EventHandler<WindowRepositionedEventArgs> WindowRepositioned;
        public event EventHandler<WindowMovedEventArgs> WindowMoved;

        public WindowMonitorService()
        {
            _monitorTimer = new System.Timers.Timer(POLLING_INTERVAL_MS)
            {
                AutoReset = true
            };
            _monitorTimer.Elapsed += MonitorTimerElapsed;
        }

        public void AddTargetApplication(ApplicationWindow app)
        {
            if (_disposed) return;
            lock (_targetApplications)
            {
                if (!_targetApplications.Contains(app))
                    _targetApplications.Add(app);
            }
        }

        public void RemoveTargetApplication(ApplicationWindow app)
        {
            if (_disposed) return;
            lock (_targetApplications)
            {
                _targetApplications.Remove(app);
            }
        }

        public void StartMonitoring()
        {
            if (_disposed) return;
            _monitorTimer.Start();
        }

        public void StopMonitoring()
        {
            if (_disposed) return;
            _monitorTimer.Stop();
            _lastWindowPositions.Clear();
        }

        public void SetDormantMode(bool isDormant)
        {
            if (_monitorTimer != null)
            {
                _monitorTimer.Interval = isDormant ? POLLING_INTERVAL_MS * 2 : POLLING_INTERVAL_MS;
            }
        }

        private void MonitorTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_disposed) return;

            try
            {
                // Manually iterate through _targetApplications to avoid LINQ overhead
                List<ApplicationWindow> activeTargets = new(_targetApplications.Count);
                lock (_targetApplications)
                {
                    for (int i = 0; i < _targetApplications.Count; i++)
                    {
                        var app = _targetApplications[i];
                        if (app.IsActive && app.RestrictToMonitor.HasValue)
                        {
                            activeTargets.Add(app);
                        }
                    }
                }
                if (activeTargets.Count == 0)
                    return;

                // Preallocate a list for window handles
                var windowHandles = new List<IntPtr>(64);

                // Cache GetWindowTitle calls to avoid duplicate work
                var windowTitleCache = new Dictionary<IntPtr, string>(64);

                // Enumerate windows only once, caching titles along the way
                NativeMethods.EnumWindows((hWnd, lParam) =>
                {
                    if (!NativeMethods.IsWindowVisible(hWnd))
                        return true;

                    if (!windowTitleCache.TryGetValue(hWnd, out string title))
                    {
                        title = GetWindowTitle(hWnd);
                        windowTitleCache[hWnd] = title;
                    }

                    if (!string.IsNullOrWhiteSpace(title) && !ShouldIgnoreSystemWindow(hWnd, title))
                    {
                        windowHandles.Add(hWnd);
                    }
                    return true;
                }, IntPtr.Zero);

                // Process each window using the cached title value
                for (int i = 0; i < windowHandles.Count; i++)
                {
                    IntPtr hwnd = windowHandles[i];
                    if (!windowTitleCache.TryGetValue(hwnd, out string title))
                    {
                        title = GetWindowTitle(hwnd);
                    }
                    if (string.IsNullOrWhiteSpace(title) || !NativeMethods.IsWindow(hwnd))
                        continue;

                    // Use a simple loop for matching instead of LINQ
                    ApplicationWindow matchingApp = null;
                    for (int j = 0; j < activeTargets.Count; j++)
                    {
                        if (activeTargets[j].Matches(hwnd, title))
                        {
                            matchingApp = activeTargets[j];
                            break;
                        }
                    }

                    if (matchingApp != null)
                    {
                        ProcessTargetWindow(hwnd, title, matchingApp);
                    }
                    else
                    {
                        HandleWindowMovement(hwnd);
                    }
                }

                CleanupInvalidWindowHandles();
            }
            catch (Exception)
            {
                // Suppress exceptions to prevent crashes in the monitoring thread
            }
        }

        private void ProcessTargetWindow(IntPtr hwnd, string windowTitle, ApplicationWindow matchingApp)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT windowRect))
                return;

            int targetMonitorIndex = matchingApp.RestrictToMonitor.Value;

            // Use Screen.AllScreens to get monitor information
            if (targetMonitorIndex < 0 || targetMonitorIndex >= Screen.AllScreens.Length)
                return;

            var targetScreen = Screen.AllScreens[targetMonitorIndex];
            var targetBounds = targetScreen.Bounds;

            // Check if window is on the correct monitor
            bool needsRepositioning = !IsWindowFullyOnMonitor(windowRect, targetBounds);

            if (needsRepositioning)
            {
                NativeMethods.RECT lastRect = new();
                _lastWindowPositions.TryGetValue(hwnd, out lastRect);
                ForceWindowToMonitor(hwnd, windowTitle, windowRect, lastRect, targetMonitorIndex, targetBounds);
            }
            else
            {
                // Track movement even if window is on the correct monitor
                HandleWindowMovement(hwnd);
            }
        }

        private bool IsWindowFullyOnMonitor(NativeMethods.RECT windowRect, System.Drawing.Rectangle monitorBounds)
        {
            // Allow a small margin (10 pixels) to avoid constant repositioning for slight offsets
            int margin = 10;
            return windowRect.Left >= monitorBounds.Left - margin &&
                   windowRect.Right <= monitorBounds.Right + margin &&
                   windowRect.Top >= monitorBounds.Top - margin &&
                   windowRect.Bottom <= monitorBounds.Bottom + margin;
        }

        private void ForceWindowToMonitor(IntPtr hwnd, string windowTitle, NativeMethods.RECT windowRect,
                                        NativeMethods.RECT lastRect, int targetMonitorIndex, System.Drawing.Rectangle targetBounds)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return;

            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;
            int newX, newY;

            // Stop any active window drag
            NativeMethods.StopWindowDrag(hwnd);

            if (lastRect.Left == 0 && lastRect.Top == 0 && lastRect.Right == 0 && lastRect.Bottom == 0)
            {
                // First time seeing this window, center it on the target monitor
                newX = targetBounds.Left + ((targetBounds.Width - windowWidth) / 2);
                newY = targetBounds.Top + ((targetBounds.Height - windowHeight) / 2);
            }
            else
            {
                // Try to preserve relative position
                float relativeX = 0.5f;  // Default to center
                float relativeY = 0.5f;

                // Get current monitor
                var currentMonitor = GetMonitorFromWindow(hwnd);
                if (currentMonitor != null)
                {
                    // Calculate relative position within current monitor
                    relativeX = (float)(windowRect.Left - currentMonitor.Bounds.Left) / currentMonitor.Bounds.Width;
                    relativeY = (float)(windowRect.Top - currentMonitor.Bounds.Top) / currentMonitor.Bounds.Height;

                    // Clamp values to valid range
                    relativeX = Math.Max(0.0f, Math.Min(1.0f, relativeX));
                    relativeY = Math.Max(0.0f, Math.Min(1.0f, relativeY));
                }

                // Apply relative position to target monitor
                newX = (int)(targetBounds.Left + targetBounds.Width * relativeX);
                newY = (int)(targetBounds.Top + targetBounds.Height * relativeY);

                // Ensure window fits within the target monitor
                int margin = 5;
                newX = Math.Max(targetBounds.Left + margin, Math.Min(targetBounds.Right - windowWidth - margin, newX));
                newY = Math.Max(targetBounds.Top + margin, Math.Min(targetBounds.Bottom - windowHeight - margin, newY));
            }

            // Try to reposition with up to 3 attempts
            bool success = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    NativeMethods.StopWindowDrag(hwnd);
                    Thread.Sleep(50 * attempt);
                }

                success = NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    newX,
                    newY,
                    windowWidth,
                    windowHeight,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER
                );

                if (success)
                    break;
            }

            if (!success)
                return;

            // Update last position and notify listeners
            NativeMethods.RECT newRect = new()
            {
                Left = newX,
                Top = newY,
                Right = newX + windowWidth,
                Bottom = newY + windowHeight
            };

            _lastWindowPositions[hwnd] = newRect;

            WindowRepositioned?.Invoke(this, new WindowRepositionedEventArgs
            {
                WindowHandle = hwnd,
                WindowTitle = windowTitle,
                OldPosition = new System.Windows.Point(windowRect.Left, windowRect.Top),
                NewPosition = new System.Windows.Point(newX, newY),
                MonitorIndex = targetMonitorIndex
            });
        }

        private void HandleWindowMovement(IntPtr hwnd)
        {
            string windowTitle = GetWindowTitle(hwnd);

            if (string.IsNullOrWhiteSpace(windowTitle) || ShouldIgnoreSystemWindow(hwnd, windowTitle))
                return;

            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT windowRect))
                return;

            bool hasMoved = true;
            if (_lastWindowPositions.TryGetValue(hwnd, out var storedRect))
            {
                hasMoved = storedRect.Left != windowRect.Left ||
                           storedRect.Top != windowRect.Top ||
                           storedRect.Right != windowRect.Right ||
                           storedRect.Bottom != windowRect.Bottom;
            }

            // Update the last known position
            _lastWindowPositions[hwnd] = windowRect;

            if (hasMoved)
            {
                var movedArgs = new WindowMovedEventArgs
                {
                    WindowHandle = hwnd,
                    WindowTitle = windowTitle,
                    WindowRect = windowRect
                };

                WindowMoved?.Invoke(this, movedArgs);
            }
        }

        private void CleanupInvalidWindowHandles()
        {
            if (_disposed) return;

            // Reuse a single list to hold invalid handles
            var invalidHandles = new List<IntPtr>(_lastWindowPositions.Count);
            foreach (var handle in _lastWindowPositions.Keys)
            {
                if (!NativeMethods.IsWindow(handle))
                {
                    invalidHandles.Add(handle);
                }
            }
            for (int i = 0; i < invalidHandles.Count; i++)
            {
                _lastWindowPositions.Remove(invalidHandles[i]);
            }
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            // Use a fixed-size StringBuilder to reduce allocations
            StringBuilder titleBuilder = new(256);
            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length > 0)
            {
                NativeMethods.GetWindowText(hWnd, titleBuilder, length + 1);
                return titleBuilder.ToString();
            }
            return string.Empty;
        }

        private bool ShouldIgnoreSystemWindow(IntPtr windowHandle, string windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle))
                return true;

            string className = NativeMethods.GetClassNameFromHandle(windowHandle);

            // These arrays are defined as static to avoid recreating them on each call
            ReadOnlySpan<string> systemClassNames = new[]
            {
                "Progman", "WorkerW", "Shell_TrayWnd", "DV2ControlHost",
                "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow"
            };

            ReadOnlySpan<string> systemTitles = new[]
            {
                "Program Manager", "Windows Shell Experience Host",
                "Task View", "Task Switching", "Start"
            };

            foreach (var systemClass in systemClassNames)
            {
                if (className.Contains(systemClass))
                    return true;
            }

            foreach (var sysTitle in systemTitles)
            {
                if (windowTitle.Contains(sysTitle))
                    return true;
            }

            return false;
        }

        private Screen GetMonitorFromWindow(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                return null;

            // Calculate window center point
            var centerPoint = new System.Drawing.Point(
                rect.Left + ((rect.Right - rect.Left) / 2),
                rect.Top + ((rect.Bottom - rect.Top) / 2)
            );

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Contains(centerPoint))
                    return screen;
            }

            // Return the closest monitor if no exact match is found
            Screen closestScreen = Screen.PrimaryScreen;
            int minDistance = int.MaxValue;
            foreach (Screen screen in Screen.AllScreens)
            {
                int screenCenterX = screen.Bounds.X + (screen.Bounds.Width / 2);
                int screenCenterY = screen.Bounds.Y + (screen.Bounds.Height / 2);
                int dx = centerPoint.X - screenCenterX;
                int dy = centerPoint.Y - screenCenterY;
                int distance = (dx * dx) + (dy * dy);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestScreen = screen;
                }
            }
            return closestScreen;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _monitorTimer.Stop();
            _monitorTimer.Elapsed -= MonitorTimerElapsed;
            _monitorTimer.Dispose();
            _lastWindowPositions.Clear();
            _targetApplications.Clear();
        }
    }

    public class WindowMovedEventArgs : EventArgs
    {
        public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; }
        public NativeMethods.RECT WindowRect { get; set; }
    }

    public class WindowRepositionedEventArgs : EventArgs
    {
        public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; }
        public System.Windows.Point OldPosition { get; set; }
        public System.Windows.Point NewPosition { get; set; }
        public int MonitorIndex { get; set; }
    }

    /// <summary>
    /// Native methods for Windows API calls
    /// </summary>
    public static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetMessageExtraInfo();

        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint WM_LBUTTONUP = 0x0202;
        public const int INPUT_MOUSE = 0;
        public const int MOUSEEVENTF_LEFTUP = 0x0004;

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

        public static string GetClassNameFromHandle(IntPtr hWnd)
        {
            StringBuilder className = new(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }

        public static void StopWindowDrag(IntPtr hWnd)
        {
            ReleaseCapture();
            SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
            SimulateMouseLeftButtonUp();
        }

        public static void SimulateMouseLeftButtonUp()
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dx = 0;
            inputs[0].mi.dy = 0;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = GetMessageExtraInfo();
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
