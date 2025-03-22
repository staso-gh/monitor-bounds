using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Threading;
using System.Timers;
using ScreenRegionProtector.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Windows.Forms;  // Requires reference to System.Windows.Forms

namespace ScreenRegionProtector.Services
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
                // Create a copy of the current targets for thread safety
                List<ApplicationWindow> activeTargets;
                lock (_targetApplications)
                {
                    activeTargets = _targetApplications
                        .Where(a => a.IsActive && a.RestrictToMonitor.HasValue)
                        .ToList();
                }

                if (activeTargets.Count == 0)
                    return;

                // Find all visible windows
                List<IntPtr> windowHandles = new List<IntPtr>();
                NativeMethods.EnumWindows((hWnd, lParam) => {
                    if (NativeMethods.IsWindowVisible(hWnd))
                    {
                        string title = GetWindowTitle(hWnd);
                        if (!string.IsNullOrWhiteSpace(title) && !ShouldIgnoreSystemWindow(hWnd, title))
                        {
                            windowHandles.Add(hWnd);
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                // Process each window
                foreach (var hwnd in windowHandles)
                {
                    string title = GetWindowTitle(hwnd);
                    if (string.IsNullOrWhiteSpace(title) || !NativeMethods.IsWindow(hwnd))
                        continue;
                    
                    ApplicationWindow matchingApp = activeTargets.FirstOrDefault(a => a.Matches(hwnd, title));
                    if (matchingApp != null)
                    {
                        ProcessTargetWindow(hwnd, title, matchingApp);
                    }
                    else
                    {
                        // For non-target windows, just track movement
                        HandleWindowMovement(hwnd);
                    }
                }

                // Clean up invalid window handles
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
                NativeMethods.RECT lastRect = new NativeMethods.RECT();
                if (_lastWindowPositions.TryGetValue(hwnd, out var storedRect))
                {
                    lastRect = storedRect;
                }

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
            return 
                windowRect.Left >= monitorBounds.Left - margin && 
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
                    System.Threading.Thread.Sleep(50 * attempt);
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
            NativeMethods.RECT newRect = new NativeMethods.RECT
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

            NativeMethods.RECT windowRect = new NativeMethods.RECT();
            if (!NativeMethods.GetWindowRect(hwnd, out windowRect))
                return;

            bool hasMoved = false;
            NativeMethods.RECT lastRect = new NativeMethods.RECT();

            if (_lastWindowPositions.TryGetValue(hwnd, out var storedRect))
            {
                lastRect = storedRect;
                hasMoved = lastRect.Left != windowRect.Left || 
                           lastRect.Top != windowRect.Top || 
                           lastRect.Right != windowRect.Right || 
                           lastRect.Bottom != windowRect.Bottom;
            }
            else
            {
                hasMoved = true;
            }

            // Update the last known position
            _lastWindowPositions[hwnd] = windowRect;

            // If the window has moved, notify listeners
            if (hasMoved)
            {
                WindowMovedEventArgs movedArgs = new WindowMovedEventArgs
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
            
            List<IntPtr> invalidHandles = new List<IntPtr>();
            
            foreach (var handle in _lastWindowPositions.Keys)
            {
                if (!NativeMethods.IsWindow(handle))
                {
                    invalidHandles.Add(handle);
                }
            }
            
            foreach (var handle in invalidHandles)
            {
                _lastWindowPositions.Remove(handle);
            }
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder titleBuilder = new StringBuilder(256);
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

            string[] systemClassNames = {
                "Progman", "WorkerW", "Shell_TrayWnd", "DV2ControlHost",
                "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow"
            };
            
            foreach (var systemClass in systemClassNames)
            {
                if (className.Contains(systemClass))
                    return true;
            }
            
            string[] systemTitles = {
                "Program Manager", "Windows Shell Experience Host",
                "Task View", "Task Switching", "Start"
            };
            
            foreach (var systemTitle in systemTitles)
            {
                if (windowTitle.Contains(systemTitle))
                    return true;
            }
            
            return false;
        }

        private Screen GetMonitorFromWindow(IntPtr hwnd)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                return null;

            // Calculate window center point
            System.Drawing.Point centerPoint = new System.Drawing.Point(
                rect.Left + ((rect.Right - rect.Left) / 2),
                rect.Top + ((rect.Bottom - rect.Top) / 2)
            );

            // Find the monitor containing the center point
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Contains(centerPoint))
                    return screen;
            }

            // If center point is not on any monitor, return the closest one
            Screen closestScreen = Screen.PrimaryScreen;
            int minDistance = int.MaxValue;

            foreach (Screen screen in Screen.AllScreens)
            {
                // Calculate distance to screen center
                int screenCenterX = screen.Bounds.X + (screen.Bounds.Width / 2);
                int screenCenterY = screen.Bounds.Y + (screen.Bounds.Height / 2);
                int distanceX = centerPoint.X - screenCenterX;
                int distanceY = centerPoint.Y - screenCenterY;
                int distance = (distanceX * distanceX) + (distanceY * distanceY);

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

        // Constants
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOREDRAW = 0x0008;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_SYSCOMMAND = 0x0112;
        public const uint SC_MOVE = 0xF010;

        // Constants for mouse input
        public const int INPUT_MOUSE = 0;
        public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
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
            StringBuilder className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }

        public static void StopWindowDrag(IntPtr hWnd)
        {
            ReleaseCapture();
            SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
            SimulateMouseLeftButtonUp(); // Also simulate the physical mouse up event
        }

        public static void SimulateMouseLeftButtonUp()
        {
            // Create an INPUT structure for mouse up
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dx = 0;
            inputs[0].mi.dy = 0;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = GetMessageExtraInfo();
            
            // Send the input
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
} 