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

namespace ScreenRegionProtector.Services
{
    
    //Service for monitoring windows and enforcing monitor restrictions
    
    public class WindowMonitorService : IDisposable
    {
        private readonly List<ApplicationWindow> _targetApplications = new();
        private System.Timers.Timer _monitorTimer;
        private System.Timers.Timer _watchdogTimer;
        private const int POLLING_INTERVAL_MS = 50; // More frequent polling for better responsiveness
        private const int WATCHDOG_INTERVAL_MS = 5000; // Check every 5 seconds that monitoring is still active
        private bool _isMonitoring = false;
        
        // Use standard Dictionary instead of limited size dictionary
        private readonly Dictionary<IntPtr, WindowsAPI.RECT> _lastWindowPositions = new Dictionary<IntPtr, WindowsAPI.RECT>();
        
        private bool _isDisposed = false;
        private DateTime _lastMonitorActivity;
        
        // Event properties using standard event pattern
        public event EventHandler<WindowMovedEventArgs> WindowMoved;
        
        public event EventHandler<WindowRepositionedEventArgs> WindowRepositioned;

        public WindowMonitorService()
        {
            _monitorTimer = new System.Timers.Timer(POLLING_INTERVAL_MS);
            _monitorTimer.Elapsed += OnMonitorTimerElapsed;
            _monitorTimer.AutoReset = true;
            
            // Set up watchdog timer to ensure monitoring stays active
            _watchdogTimer = new System.Timers.Timer(WATCHDOG_INTERVAL_MS);
            _watchdogTimer.Elapsed += OnWatchdogTimerElapsed;
            _watchdogTimer.AutoReset = true;
            _watchdogTimer.Start();
            
            _lastMonitorActivity = DateTime.Now;
        }

        // Public method to set application minimized state
        public void SetDormantMode(bool isDormant)
        {
            // Just keep basic dormant mode setting without memory optimization
            if (_monitorTimer != null)
            {
                _monitorTimer.Interval = isDormant ? POLLING_INTERVAL_MS * 2 : POLLING_INTERVAL_MS;
            }
        }

        //Add an application window to be monitored
        public void AddTargetApplication(ApplicationWindow application)
        {
            if (_isDisposed) return;
            
            lock (_targetApplications)
            {
                if (!_targetApplications.Contains(application))
                {
                    _targetApplications.Add(application);
                }
            }
        }


        //Remove an application window from monitoring
        public void RemoveTargetApplication(ApplicationWindow application)
        {
            if (_isDisposed) return;
            
            lock (_targetApplications)
            {
                _targetApplications.Remove(application);
            }
        }

        //Start monitoring window movements
        public void StartMonitoring()
        {
            if (_isDisposed || _isMonitoring)
                return;

            try
            {
                // Start the timer to poll window positions
                _monitorTimer.Start();
                _isMonitoring = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to start window monitoring: {ex.Message}",
                    "Monitoring Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }


        //Stop monitoring window movements
        public void StopMonitoring()
        {
            if (_isDisposed || !_isMonitoring)
                return;

            _monitorTimer.Stop();
            _isMonitoring = false;
            
            _lastWindowPositions.Clear();
        }

        // Timer event that polls for window positions
        private void OnMonitorTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isDisposed)
                return;
                
            try
            {
                // Record activity time for watchdog
                _lastMonitorActivity = DateTime.Now;
                
                // Create a new collection for window handles
                List<IntPtr> windowHandles = new List<IntPtr>();
                
                // This will hold all our monitor rectangles
                List<WindowsAPI.RECT> monitorRects = GetAllMonitorRects();
                System.Diagnostics.Debug.WriteLine($"---------------------- BEGIN MONITOR CYCLE ----------------------");
                System.Diagnostics.Debug.WriteLine($"Found {monitorRects.Count} monitors");
                for (int i = 0; i < monitorRects.Count; i++)
                {
                    var rect = monitorRects[i];
                    System.Diagnostics.Debug.WriteLine($"Monitor {i}: Left={rect.Left}, Top={rect.Top}, Right={rect.Right}, Bottom={rect.Bottom}, Size={rect.Width}x{rect.Height}");
                }
                
                // First check what we're monitoring
                List<ApplicationWindow> activeTargets = new List<ApplicationWindow>();
                lock (_targetApplications)
                {
                    activeTargets = _targetApplications
                        .Where(a => a.IsActive && a.RestrictToMonitor.HasValue)
                        .ToList();
                        
                    int activeApps = activeTargets.Count;
                    System.Diagnostics.Debug.WriteLine($"OnMonitorTimerElapsed: Monitoring {activeApps} active applications");
                    
                    foreach (var app in activeTargets)
                    {
                        System.Diagnostics.Debug.WriteLine($"Monitoring: '{app.TitlePattern}' on monitor {app.RestrictToMonitor}");
                    }
                }
                
                // Check if we're actually monitoring anything
                if (activeTargets.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: No active applications to monitor. Skipping monitoring cycle.");
                    return;
                }
 
                // Use EnumWindows to find all top-level windows
                System.Diagnostics.Debug.WriteLine("Enumerating all windows...");
                WindowsAPI.EnumWindows((hWnd, lParam) => {
                    if (WindowsAPI.IsWindowVisible(hWnd))
                    {
                        string title = GetWindowTitle(hWnd);
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            // Add all visible windows with titles for checking
                            windowHandles.Add(hWnd);
                            System.Diagnostics.Debug.WriteLine($"Found window: Handle={hWnd}, Title='{title}'");
                        }
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);
                
                System.Diagnostics.Debug.WriteLine($"Found {windowHandles.Count} visible windows with titles");
                
                // Check each window - first look for pattern matches, then enforce monitor restrictions
                int matchedCount = 0;
                foreach (var hwnd in windowHandles)
                {
                    string title = GetWindowTitle(hwnd);
                    
                    // Check if this window matches any of our target applications
                    ApplicationWindow matchingApp = null;
                    
                    foreach (var app in activeTargets)
                    {
                        if (app.Matches(hwnd, title))
                        {
                            matchingApp = app;
                            matchedCount++;
                            break;
                        }
                    }
                    
                    // If it's a match, output detailed info for debugging
                    if (matchingApp != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"MATCH FOUND: Window '{title}' - Pattern: '{matchingApp.TitlePattern}', Target Monitor: {matchingApp.RestrictToMonitor}");
                        
                        // Get the window rectangle
                        WindowsAPI.RECT windowRect = new WindowsAPI.RECT();
                        if (!WindowsAPI.GetWindowRect(hwnd, out windowRect))
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to get window rectangle for '{title}'");
                            continue;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Window position: Left={windowRect.Left}, Top={windowRect.Top}, Right={windowRect.Right}, Bottom={windowRect.Bottom}, Size={windowRect.Width}x{windowRect.Height}");
                        
                        // Check window's current monitor
                        int currentMonitorIndex = GetMonitorIndexForWindow(windowRect);
                        System.Diagnostics.Debug.WriteLine($"Window '{title}' is on monitor {currentMonitorIndex}, should be on {matchingApp.RestrictToMonitor}");
                        
                        // If on wrong monitor, immediately try to fix
                        if (currentMonitorIndex != matchingApp.RestrictToMonitor.Value)
                        {
                            System.Diagnostics.Debug.WriteLine($"ACTION NEEDED: REPOSITIONING window '{title}' from monitor {currentMonitorIndex} to {matchingApp.RestrictToMonitor.Value}");
                            
                            // Get the last known position if available
                            WindowsAPI.RECT lastRect = new WindowsAPI.RECT();
                            if (_lastWindowPositions.TryGetValue(hwnd, out var storedRect))
                            {
                                lastRect = storedRect;
                                System.Diagnostics.Debug.WriteLine($"Last known position: Left={lastRect.Left}, Top={lastRect.Top}, Right={lastRect.Right}, Bottom={lastRect.Bottom}");
                            }
                            
                            // Force window back to its correct monitor
                            ForceWindowToMonitor(hwnd, title, windowRect, lastRect, matchingApp.RestrictToMonitor.Value);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Window is already on correct monitor {currentMonitorIndex}");
                        }
                    }
                    
                    // Always handle movement for all windows we're tracking
                    HandleWindowMovement(hwnd);
                }
                
                System.Diagnostics.Debug.WriteLine($"Total windows matched: {matchedCount} out of {windowHandles.Count}");
                System.Diagnostics.Debug.WriteLine($"---------------------- END MONITOR CYCLE ----------------------");
                
                // Clean up window handles that no longer exist
                CleanupInvalidWindowHandles();
            }
            catch (Exception ex)
            {
                // Prevent crashes in the monitoring thread
                System.Diagnostics.Debug.WriteLine($"ERROR in window monitoring: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        // Gets the window title
        private string GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder titleBuilder = new StringBuilder(256);
            int length = WindowsAPI.GetWindowTextLength(hWnd);
            if (length > 0)
            {
                WindowsAPI.GetWindowText(hWnd, titleBuilder, length + 1);
                return titleBuilder.ToString();
            }
            return string.Empty;
        }
        
        // Remove window handles that no longer represent valid windows
        private void CleanupInvalidWindowHandles()
        {
            if (_isDisposed) return;
            
            List<IntPtr> invalidHandles = new List<IntPtr>();
            
            // Find invalid handles
            foreach (var handle in _lastWindowPositions.Keys)
            {
                if (!WindowsAPI.IsWindow(handle))
                {
                    invalidHandles.Add(handle);
                }
            }
            
            // Remove invalid handles
            foreach (var handle in invalidHandles)
            {
                _lastWindowPositions.Remove(handle);
            }
        }

        //Handle window movement or resize
        private void HandleWindowMovement(IntPtr windowHandle)
        {
            if (_isDisposed)
                return;
                
            // Get window information
            string windowTitle = GetWindowTitle(windowHandle);
            
            // Skip windows without titles
            if (string.IsNullOrWhiteSpace(windowTitle))
                return;
            
            // Skip system windows that should be ignored
            if (ShouldIgnoreSystemWindow(windowHandle, windowTitle))
                return;
            
            // Get the window rectangle
            WindowsAPI.RECT windowRect = new WindowsAPI.RECT();
            if (!WindowsAPI.GetWindowRect(windowHandle, out windowRect))
            {
                return;
            }

            // Check if window has moved since last check
            bool hasMoved = false;
            WindowsAPI.RECT lastRect = new WindowsAPI.RECT();
            
            if (_lastWindowPositions.TryGetValue(windowHandle, out var storedRect))
            {
                // Copy values to our rect
                lastRect.Left = storedRect.Left;
                lastRect.Top = storedRect.Top;
                lastRect.Right = storedRect.Right;
                lastRect.Bottom = storedRect.Bottom;
                
                // Even a single pixel change counts as movement
                hasMoved = lastRect.Left != windowRect.Left || 
                           lastRect.Top != windowRect.Top || 
                           lastRect.Right != windowRect.Right || 
                           lastRect.Bottom != windowRect.Bottom;
            }
            else
            {
                // First time seeing this window
                hasMoved = true;
            }
            
            // Update the last known position immediately
            _lastWindowPositions[windowHandle] = windowRect;

            // Check if this window is one of our target applications
            ApplicationWindow matchingApp = null;
            
            lock (_targetApplications)
            {
                matchingApp = _targetApplications
                    .Where(a => a.IsActive && a.RestrictToMonitor.HasValue)
                    .FirstOrDefault(a => a.Matches(windowHandle, windowTitle));
            }

            // If it's a target application with a monitor restriction
            if (matchingApp != null && matchingApp.RestrictToMonitor.HasValue)
            {
                int targetMonitorIndex = matchingApp.RestrictToMonitor.Value;
                int currentMonitorIndex = GetMonitorIndexForWindow(windowRect);
                
                // Log window position info 
                System.Diagnostics.Debug.WriteLine($"Window '{windowTitle}' - hasMoved: {hasMoved}, Current Monitor: {currentMonitorIndex}, Target Monitor: {targetMonitorIndex}");
                
                // Always check window's monitor position, even if it hasn't moved
                // as the window could be dragged continuously
                if (currentMonitorIndex != targetMonitorIndex)
                {
                    // Force the window to move back to its designated monitor immediately
                    ForceWindowToMonitor(windowHandle, windowTitle, windowRect, lastRect, targetMonitorIndex);
                }
            }
            
            // If the window has moved, notify listeners (after potential repositioning)
            if (hasMoved)
            {
                WindowMovedEventArgs movedArgs = new WindowMovedEventArgs
                {
                    WindowHandle = windowHandle,
                    WindowTitle = windowTitle,
                    WindowRect = windowRect
                };
                
                // Raise window moved event
                WindowMoved?.Invoke(this, movedArgs);
            }
        }

        // Method to handle the window repositioning logic
        private void ForceWindowToMonitor(IntPtr windowHandle, string windowTitle, WindowsAPI.RECT windowRect, 
                                       WindowsAPI.RECT lastRect, int targetMonitorIndex)
        {
            System.Diagnostics.Debug.WriteLine($"ForceWindowToMonitor: Moving '{windowTitle}' to monitor {targetMonitorIndex}");
            
            // Always position the window on the correct monitor, regardless of edge conditions
            int currentMonitorIndex = GetMonitorIndexForWindow(windowRect);
            
            // Skip if already on correct monitor (shouldn't happen due to caller check)
            if (currentMonitorIndex == targetMonitorIndex)
            {
                System.Diagnostics.Debug.WriteLine($"Window '{windowTitle}' is already on monitor {targetMonitorIndex}");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"Moving window from monitor {currentMonitorIndex} to {targetMonitorIndex}");
            
            // Get the target monitor rect for repositioning
            WindowsAPI.RECT targetMonitorRect = GetMonitorRect(targetMonitorIndex);
            
            // Calculate new position on target monitor
            int newX, newY;
            int windowWidth = windowRect.Width;
            int windowHeight = windowRect.Height;
            
            // If this is the first move, center the window on the target monitor
            if (lastRect.Left == 0 && lastRect.Top == 0 && lastRect.Right == 0 && lastRect.Bottom == 0)
            {
                // Center the window on the target monitor
                newX = targetMonitorRect.Left + ((targetMonitorRect.Right - targetMonitorRect.Left) - windowWidth) / 2;
                newY = targetMonitorRect.Top + ((targetMonitorRect.Bottom - targetMonitorRect.Top) - windowHeight) / 2;
                
                System.Diagnostics.Debug.WriteLine($"Initial positioning (centered): ({newX},{newY})");
            }
            else
            {
                // Calculate a position that preserves relative position in the target monitor
                WindowsAPI.RECT sourceMonitorRect = GetMonitorRect(currentMonitorIndex);
                
                // Determine relative position within source monitor (0.0 to 1.0)
                float relativeX = (float)(windowRect.Left - sourceMonitorRect.Left) / (sourceMonitorRect.Right - sourceMonitorRect.Left);
                float relativeY = (float)(windowRect.Top - sourceMonitorRect.Top) / (sourceMonitorRect.Bottom - sourceMonitorRect.Top);
                
                // Apply that relative position to target monitor
                newX = (int)(targetMonitorRect.Left + (targetMonitorRect.Right - targetMonitorRect.Left) * relativeX);
                newY = (int)(targetMonitorRect.Top + (targetMonitorRect.Bottom - targetMonitorRect.Top) * relativeY);
                
                System.Diagnostics.Debug.WriteLine($"Relative positioning: ({relativeX:F2},{relativeY:F2}) -> ({newX},{newY})");
                
                // Ensure the window fits within the target monitor bounds
                newX = Math.Max(targetMonitorRect.Left, Math.Min(targetMonitorRect.Right - windowWidth, newX));
                newY = Math.Max(targetMonitorRect.Top, Math.Min(targetMonitorRect.Bottom - windowHeight, newY));
            }
            
            // Release mouse capture to prevent sticky dragging
            WindowsAPI.ReleaseMouseCapture();
            
            // Simulate mouse button up to prevent sticky dragging
            WindowsAPI.SimulateLeftMouseButtonUp();
            
            System.Diagnostics.Debug.WriteLine($"Final window position: ({newX},{newY}) with size {windowWidth}x{windowHeight}");
            
            // Attempt repositioning with a retry
            bool success = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                success = WindowsAPI.SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    newX,
                    newY,
                    windowWidth,
                    windowHeight,
                    WindowsAPI.SWP_NOACTIVATE | WindowsAPI.SWP_NOZORDER
                );
                
                if (success) 
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully repositioned window on attempt {attempt + 1}");
                    break;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"Failed to reposition window on attempt {attempt + 1}. Error: {error}");
                    
                    // Small delay before retry
                    System.Threading.Thread.Sleep(50);
                }
            }
            
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine("All window repositioning attempts failed");
                return;
            }
            
            // Create event args for notification
            WindowRepositionedEventArgs repositionedArgs = new WindowRepositionedEventArgs
            {
                WindowHandle = windowHandle,
                WindowTitle = windowTitle,
                OldPosition = new System.Windows.Point(windowRect.Left, windowRect.Top),
                NewPosition = new System.Windows.Point(newX, newY),
                MonitorIndex = targetMonitorIndex
            };
            
            // Update the last known position
            WindowsAPI.RECT newRect = new WindowsAPI.RECT
            {
                Left = newX,
                Top = newY,
                Right = newX + windowWidth,
                Bottom = newY + windowHeight
            };
            
            _lastWindowPositions[windowHandle] = newRect;
            
            // Raise the repositioned event
            WindowRepositioned?.Invoke(this, repositionedArgs);
        }

        //Determines if a window should be monitored based on the target applications list
        private bool IsTargetWindow(IntPtr windowHandle, string windowTitle)
        {
            if (_isDisposed || string.IsNullOrWhiteSpace(windowTitle))
                return false;
                
            // Skip system windows that should be ignored
            if (ShouldIgnoreSystemWindow(windowHandle, windowTitle))
                return false;
            
            lock (_targetApplications)
            {
                // If no specific targets are defined, monitor all visible windows
                if (_targetApplications.Count == 0)
                {
                    return WindowsAPI.IsWindowVisible(windowHandle);
                }

                // Otherwise, check if the window matches any of our target applications
                return _targetApplications
                    .Where(a => a.IsActive)
                    .Any(a => a.Matches(windowHandle, windowTitle));
            }
        }

        // Helper method to determine if a system window should be ignored
        private bool ShouldIgnoreSystemWindow(IntPtr windowHandle, string windowTitle)
        {
            // Ignore windows without titles
            if (string.IsNullOrWhiteSpace(windowTitle))
                return true;
                
            // Get window class name
            string className = WindowsAPI.GetClassNameFromHandle(windowHandle);

            // Ignore specific system windows by class name - definite exclusions
            string[] systemClassNames = {
                "Progman",
                "WorkerW", 
                "Shell_TrayWnd",
                "DV2ControlHost",
                "Windows.UI.Core.CoreWindow",
                "ApplicationFrameWindow"  // UWP app container
            };
            
            foreach (var systemClass in systemClassNames)
            {
                if (className.Contains(systemClass))
                    return true;
            }
            
            // Ignore specific system windows by title
            string[] systemTitles = {
                "Program Manager",
                "Windows Shell Experience Host",
                "Task View",
                "Task Switching",
                "Start"
            };
            
            foreach (var systemTitle in systemTitles)
            {
                if (windowTitle.Contains(systemTitle))
                    return true;
            }
            
            return false;
        }

        //Checks if a window is contained within the specified monitor

        private bool IsWindowInMonitor(WindowsAPI.RECT windowRect, int monitorIndex)
        {
            var monitorRect = GetMonitorRect(monitorIndex);
            
            return windowRect.Left >= monitorRect.Left && 
                   windowRect.Right <= monitorRect.Right &&
                   windowRect.Top >= monitorRect.Top &&
                   windowRect.Bottom <= monitorRect.Bottom;
        }


        //Gets the boundaries of the specified monitor

        private WindowsAPI.RECT GetMonitorRect(int monitorIndex)
        {
            List<WindowsAPI.RECT> monitorRects = GetAllMonitorRects();
            
            // Return the requested monitor rect if it exists
            if (monitorIndex >= 0 && monitorIndex < monitorRects.Count)
            {
                return monitorRects[monitorIndex];
            }
            
            return new WindowsAPI.RECT();
        }


        //Gets the index of the monitor that contains most of the window

        private int GetMonitorIndexForWindow(WindowsAPI.RECT windowRect)
        {
            // Find the center point of the window
            int centerX = windowRect.Left + (windowRect.Width / 2);
            int centerY = windowRect.Top + (windowRect.Height / 2);
            
            // Get all monitors
            List<WindowsAPI.RECT> monitorRects = GetAllMonitorRects();
            
            // Find which monitor contains the center point of the window
            for (int i = 0; i < monitorRects.Count; i++)
            {
                var monitorRect = monitorRects[i];
                if (centerX >= monitorRect.Left && centerX <= monitorRect.Right &&
                    centerY >= monitorRect.Top && centerY <= monitorRect.Bottom)
                {
                    return i;
                }
            }
            
            // If no monitor contains the center, find the monitor with maximum overlap
            int bestMonitorIndex = 0;
            int maxOverlapArea = 0;
            
            for (int i = 0; i < monitorRects.Count; i++)
            {
                var monitorRect = monitorRects[i];
                
                // Calculate the intersection rectangle
                int overlapLeft = Math.Max(windowRect.Left, monitorRect.Left);
                int overlapTop = Math.Max(windowRect.Top, monitorRect.Top);
                int overlapRight = Math.Min(windowRect.Right, monitorRect.Right);
                int overlapBottom = Math.Min(windowRect.Bottom, monitorRect.Bottom);
                
                // Check if there's an actual overlap
                if (overlapLeft < overlapRight && overlapTop < overlapBottom)
                {
                    int overlapArea = (overlapRight - overlapLeft) * (overlapBottom - overlapTop);
                    if (overlapArea > maxOverlapArea)
                    {
                        maxOverlapArea = overlapArea;
                        bestMonitorIndex = i;
                    }
                }
            }
            
            // If we found a monitor with overlap, return it
            if (maxOverlapArea > 0)
            {
                return bestMonitorIndex;
            }
            
            // If there's still no overlap, find the closest monitor based on distance to center
            int closestMonitorIndex = 0;
            int minDistance = int.MaxValue;
            
            for (int i = 0; i < monitorRects.Count; i++)
            {
                var monitorRect = monitorRects[i];
                int monitorCenterX = monitorRect.Left + ((monitorRect.Right - monitorRect.Left) / 2);
                int monitorCenterY = monitorRect.Top + ((monitorRect.Bottom - monitorRect.Top) / 2);
                
                int distanceX = centerX - monitorCenterX;
                int distanceY = centerY - monitorCenterY;
                int squareDistance = (distanceX * distanceX) + (distanceY * distanceY);
                
                if (squareDistance < minDistance)
                {
                    minDistance = squareDistance;
                    closestMonitorIndex = i;
                }
            }
            
            return closestMonitorIndex;
        }

        private List<WindowsAPI.RECT> GetAllMonitorRects()
        {
            // Return a new list each time without caching
            List<WindowsAPI.RECT> monitorRects = new List<WindowsAPI.RECT>();
            
            WindowsAPI.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, 
                (IntPtr hMonitor, IntPtr hdcMonitor, ref WindowsAPI.RECT lprcMonitor, IntPtr dwData) =>
                {
                    monitorRects.Add(lprcMonitor);
                    return true;
                }, IntPtr.Zero);
            
            return monitorRects;
        }

        //Determines if a window is at an edge that connects to another monitor

        private bool IsWindowAtEdgeConnectingToOtherMonitor(WindowsAPI.RECT windowRect, int targetMonitorIndex, List<WindowsAPI.RECT> allMonitorRects)
        {
            // If we don't have a valid target monitor rect, return false
            if (targetMonitorIndex < 0 || targetMonitorIndex >= allMonitorRects.Count)
                return false;
                
            WindowsAPI.RECT targetMonitorRect = allMonitorRects[targetMonitorIndex];
            
            // Window center coordinates
            int windowCenterX = windowRect.Left + (windowRect.Width / 2);
            int windowCenterY = windowRect.Top + (windowRect.Height / 2);
            
            // Check each edge of the target monitor to see if the window is near it
            foreach (var otherMonitorRect in allMonitorRects)
            {
                // Skip the target monitor
                if (otherMonitorRect.Left == targetMonitorRect.Left && 
                    otherMonitorRect.Top == targetMonitorRect.Top &&
                    otherMonitorRect.Right == targetMonitorRect.Right && 
                    otherMonitorRect.Bottom == targetMonitorRect.Bottom)
                    continue;
                
                // Check if the monitors are adjacent
                bool adjacentLeft = targetMonitorRect.Left == otherMonitorRect.Right;
                bool adjacentRight = targetMonitorRect.Right == otherMonitorRect.Left;
                bool adjacentTop = targetMonitorRect.Top == otherMonitorRect.Bottom;
                bool adjacentBottom = targetMonitorRect.Bottom == otherMonitorRect.Top;
                
                // If the monitors are adjacent, check if the window is at that edge
                if (adjacentLeft && Math.Abs(windowRect.Left - targetMonitorRect.Left) < 20)
                    return true;
                if (adjacentRight && Math.Abs(windowRect.Right - targetMonitorRect.Right) < 20)
                    return true;
                if (adjacentTop && Math.Abs(windowRect.Top - targetMonitorRect.Top) < 20)
                    return true;
                if (adjacentBottom && Math.Abs(windowRect.Bottom - targetMonitorRect.Bottom) < 20)
                    return true;
                
                if (adjacentLeft || adjacentRight || adjacentTop || adjacentBottom)
                    return true;
            }
            
            return false;
        }

        // Watchdog timer to ensure monitoring stays active
        private void OnWatchdogTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isDisposed) return;
            
            // If monitoring is supposed to be active but no activity for a while
            if (_isMonitoring && (DateTime.Now - _lastMonitorActivity).TotalSeconds > 10)
            {
                System.Diagnostics.Debug.WriteLine("Watchdog: Monitoring seems to have stalled, restarting monitor timer");
                
                // Try to restart the timer
                try
                {
                    _monitorTimer.Stop();
                    _monitorTimer.Start();
                    _lastMonitorActivity = DateTime.Now;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Watchdog: Error restarting monitoring: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            
            // Stop monitoring
            if (_isMonitoring)
            {
                _monitorTimer.Stop();
                _isMonitoring = false;
            }
            
            // Dispose timers
            _monitorTimer.Elapsed -= OnMonitorTimerElapsed;
            _monitorTimer.Dispose();
            
            // Dispose watchdog timer
            if (_watchdogTimer != null)
            {
                _watchdogTimer.Elapsed -= OnWatchdogTimerElapsed;
                _watchdogTimer.Stop();
                _watchdogTimer.Dispose();
            }
            
            // Clear collections
            _lastWindowPositions.Clear();
            _targetApplications.Clear();
        }
    }

    // Standard event args classes without pooling
    public class WindowMovedEventArgs : EventArgs
    {
        public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; }
        public WindowsAPI.RECT WindowRect { get; set; }
    }
    
    public class WindowRepositionedEventArgs : EventArgs
    {
        public IntPtr WindowHandle { get; set; }
        public string WindowTitle { get; set; }
        public System.Windows.Point OldPosition { get; set; }
        public System.Windows.Point NewPosition { get; set; }
        public int MonitorIndex { get; set; }
    }
} 