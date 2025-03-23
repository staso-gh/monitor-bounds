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
        private const int POLLING_INTERVAL_MS = 100;

        // Reusable objects to reduce allocations
        private readonly List<ApplicationWindow> _activeTargets = new();
        private readonly List<IntPtr> _windowHandles = new(16); // Reduced initial capacity
        private readonly Dictionary<IntPtr, string> _windowTitleCache = new(16); // Reduced initial capacity
        private readonly List<IntPtr> _invalidHandles = new(8);
        private readonly StringBuilder _titleBuilder = new(256);
        private readonly StringBuilder _classNameBuilder = new(256);

        // Static arrays to avoid recreating them on each call
        private static readonly string[] _systemClassNames = {
            "Progman", "WorkerW", "Shell_TrayWnd", "DV2ControlHost",
            "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow"
        };

        private static readonly string[] _systemTitles = {
            "Program Manager", "Windows Shell Experience Host",
            "Task View", "Task Switching", "Start"
        };

        public event EventHandler<WindowRepositionedEventArgs> WindowRepositioned;
        public event EventHandler<WindowMovedEventArgs> WindowMoved;

        // Reusable event args objects
        private readonly WindowMovedEventArgs _movedEventArgs = new();
        private readonly WindowRepositionedEventArgs _repositionedEventArgs = new();

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
                
                // Clean up any window position tracking for windows that no longer match any rules
                CleanupOrphanedWindows();
            }
        }

        private void CleanupOrphanedWindows()
        {
            if (_disposed || _lastWindowPositions.Count == 0) return;
            
            _invalidHandles.Clear();
            
            foreach (var hwnd in _lastWindowPositions.Keys)
            {
                bool stillNeeded = false;
                string title = GetWindowTitle(hwnd);
                
                if (!string.IsNullOrWhiteSpace(title) && NativeMethods.IsWindow(hwnd))
                {
                    // Check if any active rule still matches this window
                    foreach (var app in _targetApplications)
                    {
                        if (app.IsActive && app.Matches(hwnd, title))
                        {
                            stillNeeded = true;
                            break;
                        }
                    }
                }
                
                if (!stillNeeded)
                {
                    _invalidHandles.Add(hwnd);
                }
            }
            
            // Remove windows that don't match any rules
            foreach (var handle in _invalidHandles)
            {
                _lastWindowPositions.Remove(handle);
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
            _windowTitleCache.Clear();
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
                // Reuse active targets list instead of creating a new one
                _activeTargets.Clear();
                
                lock (_targetApplications)
                {
                    for (int i = 0; i < _targetApplications.Count; i++)
                    {
                        var app = _targetApplications[i];
                        if (app.IsActive && app.RestrictToMonitor.HasValue)
                        {
                            _activeTargets.Add(app);
                        }
                    }
                }
                
                if (_activeTargets.Count == 0)
                    return;

                // Clear existing collections to reuse them
                _windowHandles.Clear();
                _windowTitleCache.Clear();

                // Track all windows that match our active target applications
                foreach (var app in _activeTargets)
                {
                    FindMatchingWindows(app);
                }

                // Process each window that matches our target applications
                for (int i = 0; i < _windowHandles.Count; i++)
                {
                    IntPtr hwnd = _windowHandles[i];
                    if (!_windowTitleCache.TryGetValue(hwnd, out string title))
                    {
                        continue; // Skip if title not found (unlikely but safe)
                    }
                    
                    // Find matching app
                    ApplicationWindow matchingApp = FindMatchingApp(hwnd, title);

                    if (matchingApp != null)
                    {
                        ProcessTargetWindow(hwnd, title, matchingApp);
                    }
                }

                CleanupInvalidWindowHandles();
            }
            catch (Exception)
            {
                // Suppress exceptions to prevent crashes in the monitoring thread
            }
        }

        private void FindMatchingWindows(ApplicationWindow app)
        {
            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd) || !NativeMethods.IsWindow(hWnd))
                    return true;

                string title = GetWindowTitle(hWnd);
                string processName = null;
                
                // Only get process name if we're using process name matching
                if (app.UseProcessNameMatching)
                {
                    processName = GetProcessNameFromHandle(hWnd);
                }
                
                if ((!app.UseProcessNameMatching && !string.IsNullOrWhiteSpace(title)) || 
                    (app.UseProcessNameMatching && !string.IsNullOrWhiteSpace(processName)))
                {
                    if (!ShouldIgnoreSystemWindow(hWnd, title) && 
                        app.Matches(hWnd, title, processName))
                    {
                        // Only add if not already in the list
                        if (!_windowHandles.Contains(hWnd))
                        {
                            _windowHandles.Add(hWnd);
                            _windowTitleCache[hWnd] = title;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        private ApplicationWindow FindMatchingApp(IntPtr hwnd, string title)
        {
            for (int j = 0; j < _activeTargets.Count; j++)
            {
                var app = _activeTargets[j];
                string processName = null;
                
                // Only get process name if we're using process name matching
                if (app.UseProcessNameMatching)
                {
                    processName = GetProcessNameFromHandle(hwnd);
                }
                
                if (app.Matches(hwnd, title, processName))
                {
                    return app;
                }
            }
            return null;
        }

        private void ProcessTargetWindow(IntPtr hwnd, string windowTitle, ApplicationWindow matchingApp)
        {
            // Use a local variable instead of readonly field for out parameter
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
            
            // Also check if window exceeds monitor dimensions
            bool isOversized = (windowRect.Right - windowRect.Left) > targetBounds.Width || 
                             (windowRect.Bottom - windowRect.Top) > targetBounds.Height;

            if (needsRepositioning || isOversized)
            {
                if (!_lastWindowPositions.TryGetValue(hwnd, out var lastRect))
                {
                    lastRect = new NativeMethods.RECT();
                }
                ForceWindowToMonitor(hwnd, windowTitle, windowRect, lastRect, targetMonitorIndex, targetBounds);
            }
            else
            {
                // Track movement even if window is on the correct monitor
                TrackWindowMovement(hwnd, windowTitle, windowRect);
            }
        }

        private bool IsWindowFullyOnMonitor(NativeMethods.RECT windowRect, System.Drawing.Rectangle monitorBounds)
        {
            // Allow a small margin (10 pixels) to avoid constant repositioning for slight offsets
            const int margin = 10;
            
            // Check if window is within the monitor bounds
            bool isWithinBounds = windowRect.Left >= monitorBounds.Left - margin &&
                   windowRect.Right <= monitorBounds.Right + margin &&
                   windowRect.Top >= monitorBounds.Top - margin &&
                   windowRect.Bottom <= monitorBounds.Bottom + margin;
            
            if (isWithinBounds)
                return true;
                
            // If window is outside bounds, check if it's near a border that's shared with another monitor
            bool nearLeftBorder = Math.Abs(windowRect.Left - monitorBounds.Left) < margin;
            bool nearRightBorder = Math.Abs(windowRect.Right - monitorBounds.Right) < margin;
            bool nearTopBorder = Math.Abs(windowRect.Top - monitorBounds.Top) < margin;
            bool nearBottomBorder = Math.Abs(windowRect.Bottom - monitorBounds.Bottom) < margin;
            
            if (!(nearLeftBorder || nearRightBorder || nearTopBorder || nearBottomBorder))
                return false;
                
            // Check if there's another monitor that shares this boundary
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Equals(monitorBounds))
                    continue; // Skip the current monitor
                    
                // Check if this other monitor shares a boundary with current monitor
                bool sharesLeftBorder = nearLeftBorder && 
                    Math.Abs(screen.Bounds.Right - monitorBounds.Left) < margin &&
                    !(screen.Bounds.Bottom < monitorBounds.Top - margin || 
                      screen.Bounds.Top > monitorBounds.Bottom + margin);
                      
                bool sharesRightBorder = nearRightBorder && 
                    Math.Abs(screen.Bounds.Left - monitorBounds.Right) < margin &&
                    !(screen.Bounds.Bottom < monitorBounds.Top - margin || 
                      screen.Bounds.Top > monitorBounds.Bottom + margin);
                      
                bool sharesTopBorder = nearTopBorder && 
                    Math.Abs(screen.Bounds.Bottom - monitorBounds.Top) < margin &&
                    !(screen.Bounds.Right < monitorBounds.Left - margin || 
                      screen.Bounds.Left > monitorBounds.Right + margin);
                      
                bool sharesBottomBorder = nearBottomBorder && 
                    Math.Abs(screen.Bounds.Top - monitorBounds.Bottom) < margin &&
                    !(screen.Bounds.Right < monitorBounds.Left - margin || 
                      screen.Bounds.Left > monitorBounds.Right + margin);
                      
                if (sharesLeftBorder || sharesRightBorder || sharesTopBorder || sharesBottomBorder)
                    return false; // Window is near a shared border, so don't reposition
            }
            
            return false; // Window is outside bounds and not near a shared border
        }

        private void ForceWindowToMonitor(IntPtr hwnd, string windowTitle, NativeMethods.RECT windowRect,
                                        NativeMethods.RECT lastRect, int targetMonitorIndex, System.Drawing.Rectangle targetBounds)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return;

            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;
            int newX, newY;
            
            // Check if window dimensions exceed monitor dimensions and adjust if necessary
            if (windowWidth > targetBounds.Width)
            {
                windowWidth = targetBounds.Width;
            }
            
            if (windowHeight > targetBounds.Height)
            {
                windowHeight = targetBounds.Height;
            }

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
                const int margin = 5;
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
            _lastWindowPositions[hwnd] = new NativeMethods.RECT
            {
                Left = newX,
                Top = newY,
                Right = newX + windowWidth,
                Bottom = newY + windowHeight
            };

            // Reuse event args object
            _repositionedEventArgs.WindowHandle = hwnd;
            _repositionedEventArgs.WindowTitle = windowTitle;
            _repositionedEventArgs.OldPosition = new System.Windows.Point(windowRect.Left, windowRect.Top);
            _repositionedEventArgs.NewPosition = new System.Windows.Point(newX, newY);
            _repositionedEventArgs.MonitorIndex = targetMonitorIndex;

            WindowRepositioned?.Invoke(this, _repositionedEventArgs);
        }

        private void TrackWindowMovement(IntPtr hwnd, string windowTitle, NativeMethods.RECT windowRect)
        {
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
                // Reuse event args object
                _movedEventArgs.WindowHandle = hwnd;
                _movedEventArgs.WindowTitle = windowTitle;
                _movedEventArgs.WindowRect = windowRect;

                WindowMoved?.Invoke(this, _movedEventArgs);
            }
        }

        private void CleanupInvalidWindowHandles()
        {
            if (_disposed) return;

            // Reuse invalid handles list
            _invalidHandles.Clear();
            
            foreach (var handle in _lastWindowPositions.Keys)
            {
                if (!NativeMethods.IsWindow(handle))
                {
                    _invalidHandles.Add(handle);
                }
            }
            
            for (int i = 0; i < _invalidHandles.Count; i++)
            {
                _lastWindowPositions.Remove(_invalidHandles[i]);
            }
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            // Reuse StringBuilder instead of creating a new one each time
            _titleBuilder.Clear();
            int length = NativeMethods.GetWindowTextLength(hWnd);
            if (length > 0)
            {
                if (length >= _titleBuilder.Capacity)
                {
                    _titleBuilder.Capacity = length + 1;
                }
                NativeMethods.GetWindowText(hWnd, _titleBuilder, length + 1);
                return _titleBuilder.ToString();
            }
            return string.Empty;
        }

        private bool ShouldIgnoreSystemWindow(IntPtr windowHandle, string windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle))
                return true;

            string className = GetClassNameFromHandle(windowHandle);

            // Check against static arrays
            for (int i = 0; i < _systemClassNames.Length; i++)
            {
                if (className.Contains(_systemClassNames[i]))
                    return true;
            }

            for (int i = 0; i < _systemTitles.Length; i++)
            {
                if (windowTitle.Contains(_systemTitles[i]))
                    return true;
            }

            return false;
        }

        private string GetClassNameFromHandle(IntPtr hWnd)
        {
            // Reuse StringBuilder
            _classNameBuilder.Clear();
            NativeMethods.GetClassName(hWnd, _classNameBuilder, _classNameBuilder.Capacity);
            return _classNameBuilder.ToString();
        }

        private string GetProcessNameFromHandle(IntPtr hWnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId > 0)
                {
                    using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                    // Note: Process.ProcessName doesn't include the .exe extension
                    return process.ProcessName;
                }
            }
            catch (Exception)
            {
                // Process might have exited or access denied
            }
            return string.Empty;
        }

        private Screen GetMonitorFromWindow(IntPtr hwnd)
        {
            // Use a local variable for out parameter
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
            _activeTargets.Clear();
            _windowHandles.Clear();
            _windowTitleCache.Clear();
            _invalidHandles.Clear();
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
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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

        // Reuse a single INPUT array for mouse operations
        private static readonly INPUT[] _inputs = new INPUT[1];

        static NativeMethods()
        {
            // Pre-initialize the INPUT structure
            _inputs[0].type = INPUT_MOUSE;
            _inputs[0].mi.dx = 0;
            _inputs[0].mi.dy = 0;
            _inputs[0].mi.mouseData = 0;
            _inputs[0].mi.time = 0;
        }

        public static void StopWindowDrag(IntPtr hWnd)
        {
            ReleaseCapture();
            SendMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
            SimulateMouseLeftButtonUp();
        }

        public static void SimulateMouseLeftButtonUp()
        {
            _inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTUP;
            _inputs[0].mi.dwExtraInfo = GetMessageExtraInfo();
            SendInput(1, _inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
