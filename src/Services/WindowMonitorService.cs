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
        private const int POLLING_INTERVAL_MS = 500; // Increased polling interval to reduce CPU and memory usage
        private bool _isMonitoring = false;
        
        // Replace the unlimited dictionary with a size-limited collection
        private readonly LimitedSizeDictionary<IntPtr, WindowsAPI.RECT> _lastWindowPositions;
        private const int MAX_TRACKED_WINDOWS = 500; // Maximum number of windows to track
        
        private readonly ConcurrentQueue<WindowsAPI.RECT> _rectPool = new ConcurrentQueue<WindowsAPI.RECT>();
        private bool _isDisposed = false;
        private int _cleanupCounter = 0;
        private const int CLEANUP_FREQUENCY = 10; // Clean up obsolete window handles every 10 timer ticks
        
        // Reusable collections to avoid allocations
        private readonly List<IntPtr> _windowHandles = new List<IntPtr>(50);
        private readonly StringBuilder _windowTitleBuilder = new StringBuilder(256);
        
        // Add object pools for event arguments
        private readonly ConcurrentQueue<WindowMovedEventArgs> _windowMovedArgsPool = new ConcurrentQueue<WindowMovedEventArgs>();
        private readonly ConcurrentQueue<WindowRepositionedEventArgs> _windowRepositionedArgsPool = new ConcurrentQueue<WindowRepositionedEventArgs>();
        private const int MAX_POOL_SIZE = 20; // Limit pool sizes to prevent excessive memory usage
        
        // Memory pressure indicator
        private bool _isUnderMemoryPressure = false;
        private DateTime _lastMemoryPressureCheck = DateTime.MinValue;
        private readonly TimeSpan _memoryCheckInterval = TimeSpan.FromSeconds(30);
        
        // WeakEvent pattern to prevent memory leaks
        private WeakEventManager<WindowMovedEventArgs> _windowMovedEventManager = new WeakEventManager<WindowMovedEventArgs>();
        private WeakEventManager<WindowRepositionedEventArgs> _windowRepositionedEventManager = new WeakEventManager<WindowRepositionedEventArgs>();
        
        // Flag to indicate if app is minimized
        private bool _isApplicationMinimized = false;
        
        // Use event properties with custom add/remove to use the weak event manager
        public event EventHandler<WindowMovedEventArgs> WindowMoved
        {
            add { _windowMovedEventManager.AddHandler(value); }
            remove { _windowMovedEventManager.RemoveHandler(value); }
        }
        
        public event EventHandler<WindowRepositionedEventArgs> WindowRepositioned
        {
            add { _windowRepositionedEventManager.AddHandler(value); }
            remove { _windowRepositionedEventManager.RemoveHandler(value); }
        }

        public WindowMonitorService()
        {
            _monitorTimer = new System.Timers.Timer(POLLING_INTERVAL_MS);
            _monitorTimer.Elapsed += OnMonitorTimerElapsed;
            _monitorTimer.AutoReset = true;
            
            // Initialize the size-limited dictionary
            _lastWindowPositions = new LimitedSizeDictionary<IntPtr, WindowsAPI.RECT>(MAX_TRACKED_WINDOWS);
            
            // Pre-allocate more RECT objects for reuse
            for (int i = 0; i < 50; i++)
            {
                _rectPool.Enqueue(new WindowsAPI.RECT());
            }
            
            // Pre-allocate event args objects for reuse
            for (int i = 0; i < 10; i++)
            {
                _windowMovedArgsPool.Enqueue(new WindowMovedEventArgs());
                _windowRepositionedArgsPool.Enqueue(new WindowRepositionedEventArgs());
            }
            
            // Register for memory pressure notifications
            GC.RegisterForFullGCNotification(10, 10);
            
            // Start a background thread to monitor for memory pressure
            ThreadPool.QueueUserWorkItem(MonitorMemoryPressure);
        }

        // Method to monitor for memory pressure
        private void MonitorMemoryPressure(object state)
        {
            while (!_isDisposed)
            {
                try
                {
                    // Check for memory pressure
                    if (DateTime.Now - _lastMemoryPressureCheck > _memoryCheckInterval)
                    {
                        _lastMemoryPressureCheck = DateTime.Now;
                        
                        // Check if we're under memory pressure
                        if (GC.GetTotalMemory(false) > 100 * 1024 * 1024) // 100MB threshold
                        {
                            _isUnderMemoryPressure = true;
                            
                            // Trim excess objects from pools
                            TrimObjectPools();
                            
                            // Force a collection to free memory
                            GC.Collect(1, GCCollectionMode.Optimized);
                        }
                        else
                        {
                            _isUnderMemoryPressure = false;
                        }
                    }
                    
                    // Wait for 5 seconds before checking again
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in memory pressure monitoring: {ex.Message}");
                }
            }
        }
        
        // Method to trim object pools when under memory pressure
        private void TrimObjectPools()
        {
            // Trim RECT pool
            int rectCount = _rectPool.Count;
            for (int i = 0; i < rectCount - 20 && i < rectCount; i++)
            {
                _rectPool.TryDequeue(out _);
            }
            
            // Trim WindowMovedEventArgs pool
            int movedArgsCount = _windowMovedArgsPool.Count;
            for (int i = 0; i < movedArgsCount - 5 && i < movedArgsCount; i++)
            {
                _windowMovedArgsPool.TryDequeue(out _);
            }
            
            // Trim WindowRepositionedEventArgs pool
            int repositionedArgsCount = _windowRepositionedArgsPool.Count;
            for (int i = 0; i < repositionedArgsCount - 5 && i < repositionedArgsCount; i++)
            {
                _windowRepositionedArgsPool.TryDequeue(out _);
            }
        }
        
        // Public method to set application minimized state
        public void SetDormantMode(bool isDormant)
        {
            _isApplicationMinimized = isDormant;
            
            // Adjust polling interval based on minimized state
            if (_monitorTimer != null)
            {
                _monitorTimer.Interval = isDormant ? POLLING_INTERVAL_MS * 4 : POLLING_INTERVAL_MS;
            }
            
            // If we're entering dormant mode, perform memory optimization
            if (isDormant)
            {
                // Trim object pools
                TrimObjectPools();
                
                // Force a garbage collection
                GC.Collect(1, GCCollectionMode.Optimized);
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


        //Timer event that polls for window positions

        private void OnMonitorTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isDisposed)
                return;
                
            // If application is minimized, poll less frequently
            if (_isApplicationMinimized)
            {
                // Skip some polls when minimized
                if (DateTime.Now.Second % 4 != 0)
                    return;
            }
                
            try
            {
                // Clear reusable collection rather than creating a new one
                _windowHandles.Clear();
                
                // Debug: Log active applications we're monitoring
                lock (_targetApplications)
                {
                    foreach (var app in _targetApplications.Where(a => a.IsActive))
                    {
                        System.Diagnostics.Debug.WriteLine($"Active monitor restriction: '{app.TitlePattern}' to monitor {app.RestrictToMonitor}");
                    }
                }

                // Use EnumWindows to find all top-level windows
                WindowsAPI.EnumWindows((hWnd, lParam) => {
                    if (WindowsAPI.IsWindowVisible(hWnd))
                    {
                        string title = GetWindowTitle(hWnd);
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            // Add all visible windows with titles for checking
                            _windowHandles.Add(hWnd);
                        }
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);
                
                // Check each window - first look for pattern matches, then enforce monitor restrictions
                foreach (var hwnd in _windowHandles)
                {
                    HandleWindowMovement(hwnd);
                }
                
                // Periodically clean up window handles that no longer exist
                _cleanupCounter++;
                if (_cleanupCounter >= CLEANUP_FREQUENCY)
                {
                    CleanupInvalidWindowHandles();
                    _cleanupCounter = 0;
                    
                    // Force garbage collection after cleanup
                    if (_isUnderMemoryPressure)
                    {
                        GC.Collect(1, GCCollectionMode.Optimized);
                    }
                }
            }
            catch (Exception ex)
            {
                // Prevent crashes in the monitoring thread
                System.Diagnostics.Debug.WriteLine($"Error in window monitoring: {ex.Message}");
            }
        }
        
        // Uses the reusable StringBuilder to avoid string allocations
        private string GetWindowTitle(IntPtr hWnd)
        {
            _windowTitleBuilder.Clear();
            int length = WindowsAPI.GetWindowTextLength(hWnd);
            if (length > 0)
            {
                // Ensure capacity
                if (_windowTitleBuilder.Capacity < length + 1)
                {
                    _windowTitleBuilder.Capacity = length + 1;
                }
                
                WindowsAPI.GetWindowText(hWnd, _windowTitleBuilder, length + 1);
                return _windowTitleBuilder.ToString();
            }
            return string.Empty;
        }
        
        // Remove window handles that no longer represent valid windows
        private void CleanupInvalidWindowHandles()
        {
            if (_isDisposed) return;
            
            List<IntPtr> invalidHandles = new List<IntPtr>();
            
            // Get all keys from our limited size dictionary
            foreach (var handle in _lastWindowPositions.GetAllKeys())
            {
                if (!WindowsAPI.IsWindow(handle))
                {
                    invalidHandles.Add(handle);
                }
            }
            
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
            if (!WindowsAPI.GetWindowRect(windowHandle, out WindowsAPI.RECT windowRect))
            {
                return;
            }

            // Check if window has moved since last check
            bool hasMoved = false;
            WindowsAPI.RECT lastRect = GetRectFromPool();
            
            // Use the size-limited dictionary (which is thread-safe)
            if (_lastWindowPositions.TryGetValue(windowHandle, out var storedRect))
            {
                // Copy values to our pooled rect
                lastRect.Left = storedRect.Left;
                lastRect.Top = storedRect.Top;
                lastRect.Right = storedRect.Right;
                lastRect.Bottom = storedRect.Bottom;
                
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
            
            // Update the last known position
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
                
                // Always check window's monitor position, not just when it moves
                if (currentMonitorIndex != targetMonitorIndex)
                {
                    // Force the window to move back to its designated monitor
                    ForceWindowToMonitor(windowHandle, windowTitle, windowRect, lastRect, targetMonitorIndex);
                }
            }
            
            // If the window has moved, notify listeners (after potential repositioning)
            if (hasMoved)
            {
                // Get a pooled WindowMovedEventArgs object
                WindowMovedEventArgs movedArgs = GetWindowMovedArgsFromPool();
                movedArgs.WindowHandle = windowHandle;
                movedArgs.WindowTitle = windowTitle;
                movedArgs.WindowRect = windowRect;
                
                // Raise window moved event
                _windowMovedEventManager.RaiseEvent(this, movedArgs);
                
                // Return the args to the pool
                ReturnWindowMovedArgsToPool(movedArgs);
            }
            
            ReturnRectToPool(lastRect);
        }

        // New method to handle the window repositioning logic
        private void ForceWindowToMonitor(IntPtr windowHandle, string windowTitle, WindowsAPI.RECT windowRect, 
                                        WindowsAPI.RECT lastRect, int targetMonitorIndex)
        {
            // Only reposition windows that aren't at monitor connection edges
            if (!IsWindowAtEdgeConnectingToOtherMonitor(windowRect, targetMonitorIndex, GetAllMonitorRects()))
            {
                // Check if the user is dragging the window
                bool isPotentiallyDragging = WindowsAPI.IsLeftMouseButtonPressed();

                // Get the target monitor rect for repositioning
                WindowsAPI.RECT targetMonitorRect = GetMonitorRect(targetMonitorIndex);
                
                // Calculate new position on target monitor, maintaining relative position
                int newX, newY;
                
                // If this is the first move, center the window on the target monitor
                if (lastRect.Left == 0 && lastRect.Top == 0 && lastRect.Right == 0 && lastRect.Bottom == 0)
                {
                    // Center the window on the target monitor
                    newX = targetMonitorRect.Left + ((targetMonitorRect.Right - targetMonitorRect.Left) - windowRect.Width) / 2;
                    newY = targetMonitorRect.Top + ((targetMonitorRect.Bottom - targetMonitorRect.Top) - windowRect.Height) / 2;
                }
                else
                {
                    // Keep relative position, but ensure window fits within monitor bounds
                    newX = Math.Max(targetMonitorRect.Left, Math.Min(
                        targetMonitorRect.Right - windowRect.Width,
                        targetMonitorRect.Left + (windowRect.Left - lastRect.Left)
                    ));
                    
                    newY = Math.Max(targetMonitorRect.Top, Math.Min(
                        targetMonitorRect.Bottom - windowRect.Height,
                        targetMonitorRect.Top + (windowRect.Top - lastRect.Top)
                    ));
                }
                
                // Debug logging
                System.Diagnostics.Debug.WriteLine($"Moving window '{windowTitle}' to monitor {targetMonitorIndex}: " + 
                                               $"From ({windowRect.Left},{windowRect.Top}) to ({newX},{newY})");
                
                // Stop any ongoing drag operation before repositioning
                if (isPotentiallyDragging)
                {
                    WindowsAPI.ReleaseMouseCapture();
                }
                
                // Repositioning the window
                bool success = WindowsAPI.SetWindowPos(
                    windowHandle,
                    IntPtr.Zero,
                    newX,
                    newY,
                    windowRect.Width,
                    windowRect.Height,
                    WindowsAPI.SWP_NOACTIVATE | WindowsAPI.SWP_NOZORDER
                );
                
                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"Failed to reposition window. Error: {error}");
                }
                
                // If we were dragging, simulate mouse button up to prevent sticky dragging
                if (isPotentiallyDragging)
                {
                    WindowsAPI.SimulateLeftMouseButtonUp();
                }
                
                // Get a pooled WindowRepositionedEventArgs object
                WindowRepositionedEventArgs repositionedArgs = GetWindowRepositionedArgsFromPool();
                repositionedArgs.WindowHandle = windowHandle;
                repositionedArgs.WindowTitle = windowTitle;
                repositionedArgs.OldPosition = new System.Windows.Point(windowRect.Left, windowRect.Top);
                repositionedArgs.NewPosition = new System.Windows.Point(newX, newY);
                repositionedArgs.MonitorIndex = targetMonitorIndex;
                
                // Raise the repositioned event
                _windowRepositionedEventManager.RaiseEvent(this, repositionedArgs);
                
                // Return the args to the pool
                ReturnWindowRepositionedArgsToPool(repositionedArgs);
            }
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
            
            // Get all monitors using our cached method
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

        // Cache for monitor rects to avoid repeated API calls
        private List<WindowsAPI.RECT> _cachedMonitorRects = null;
        private DateTime _lastMonitorCheck = DateTime.MinValue;
        private readonly TimeSpan _monitorCacheValidity = TimeSpan.FromSeconds(10); // Extended cache validity


        //Gets all monitor rectangles with caching to reduce API calls

        private List<WindowsAPI.RECT> GetAllMonitorRects()
        {
            // Use cached rects if still valid
            if (_cachedMonitorRects != null && 
                (DateTime.Now - _lastMonitorCheck) < _monitorCacheValidity)
            {
                return _cachedMonitorRects;
            }
            
            // Either we don't have cached data, or it's expired - need to refresh
            List<WindowsAPI.RECT> monitorRects = _cachedMonitorRects ?? new List<WindowsAPI.RECT>();
            monitorRects.Clear();
            
            WindowsAPI.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, 
                (IntPtr hMonitor, IntPtr hdcMonitor, ref WindowsAPI.RECT lprcMonitor, IntPtr dwData) =>
                {
                    monitorRects.Add(lprcMonitor);
                    return true;
                }, IntPtr.Zero);
            
            // Cache the result
            _cachedMonitorRects = monitorRects;
            _lastMonitorCheck = DateTime.Now;
                
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
        
        // Object pooling methods for RECT objects to reduce allocations
        private WindowsAPI.RECT GetRectFromPool()
        {
            if (_rectPool.TryDequeue(out var rect))
            {
                return rect;
            }
            return new WindowsAPI.RECT();
        }
        
        private void ReturnRectToPool(WindowsAPI.RECT rect)
        {
            // Limit pool size to prevent excessive memory usage
            if (_rectPool.Count < 100) // Increased pool size for better reuse
            {
                _rectPool.Enqueue(rect);
            }
        }

        // Object pooling methods for WindowMovedEventArgs
        private WindowMovedEventArgs GetWindowMovedArgsFromPool()
        {
            if (_windowMovedArgsPool.TryDequeue(out var args))
            {
                return args;
            }
            return new WindowMovedEventArgs();
        }
        
        private void ReturnWindowMovedArgsToPool(WindowMovedEventArgs args)
        {
            // Reset the object before returning it to the pool
            args.WindowHandle = IntPtr.Zero;
            args.WindowTitle = null;
            
            // Limit pool size to prevent excessive memory usage
            if (_windowMovedArgsPool.Count < MAX_POOL_SIZE)
            {
                _windowMovedArgsPool.Enqueue(args);
            }
        }
        
        // Object pooling methods for WindowRepositionedEventArgs
        private WindowRepositionedEventArgs GetWindowRepositionedArgsFromPool()
        {
            if (_windowRepositionedArgsPool.TryDequeue(out var args))
            {
                return args;
            }
            return new WindowRepositionedEventArgs();
        }
        
        private void ReturnWindowRepositionedArgsToPool(WindowRepositionedEventArgs args)
        {
            // Reset the object before returning it to the pool
            args.WindowHandle = IntPtr.Zero;
            args.WindowTitle = null;
            args.OldPosition = new System.Windows.Point();
            args.NewPosition = new System.Windows.Point();
            args.MonitorIndex = 0;
            
            // Limit pool size to prevent excessive memory usage
            if (_windowRepositionedArgsPool.Count < MAX_POOL_SIZE)
            {
                _windowRepositionedArgsPool.Enqueue(args);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
                
            _isDisposed = true;
            StopMonitoring();
            
            // Clear event handlers
            _windowMovedEventManager = null;
            _windowRepositionedEventManager = null;
            
            if (_monitorTimer != null)
            {
                _monitorTimer.Elapsed -= OnMonitorTimerElapsed;
                _monitorTimer.Dispose();
                _monitorTimer = null;
            }
            
            lock (_targetApplications)
            {
                _targetApplications.Clear();
            }
            
            _lastWindowPositions.Clear();
            
            _cachedMonitorRects = null;
            
            // Clear all object pools
            WindowsAPI.RECT rect;
            while (_rectPool.TryDequeue(out rect)) { }
            
            WindowMovedEventArgs movedArgs;
            while (_windowMovedArgsPool.TryDequeue(out movedArgs)) { }
            
            WindowRepositionedEventArgs repositionedArgs;
            while (_windowRepositionedArgsPool.TryDequeue(out repositionedArgs)) { }
            
            // Clear reusable collections
            _windowHandles.Clear();
            _windowTitleBuilder.Clear();
            
            GC.SuppressFinalize(this);
        }
    }
    
    
    // Simple weak event manager implementation
    public class WeakEventManager<TEventArgs> where TEventArgs : EventArgs
    {
        private readonly List<WeakReference<EventHandler<TEventArgs>>> _handlers = 
            new List<WeakReference<EventHandler<TEventArgs>>>();
        
        public void AddHandler(EventHandler<TEventArgs> handler)
        {
            if (handler == null)
                return;
                
            lock (_handlers)
            {
                _handlers.Add(new WeakReference<EventHandler<TEventArgs>>(handler));
            }
        }
        
        public void RemoveHandler(EventHandler<TEventArgs> handler)
        {
            if (handler == null)
                return;
                
            lock (_handlers)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    WeakReference<EventHandler<TEventArgs>> reference = _handlers[i];
                    
                    if (reference.TryGetTarget(out EventHandler<TEventArgs> existingHandler))
                    {
                        if (existingHandler == handler)
                        {
                            _handlers.RemoveAt(i);
                            break;
                        }
                    }
                    else
                    {
                        // Remove dead handlers
                        _handlers.RemoveAt(i);
                    }
                }
            }
        }
        
        public void RaiseEvent(object sender, TEventArgs e)
        {
            List<EventHandler<TEventArgs>> handlersToInvoke = new List<EventHandler<TEventArgs>>();
            
            lock (_handlers)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    WeakReference<EventHandler<TEventArgs>> reference = _handlers[i];
                    
                    if (reference.TryGetTarget(out EventHandler<TEventArgs> handler))
                    {
                        handlersToInvoke.Add(handler);
                    }
                    else
                    {
                        // Remove dead handler
                        _handlers.RemoveAt(i);
                    }
                }
            }
            
            // Invoke handlers outside the lock to prevent deadlocks
            foreach (var handler in handlersToInvoke)
            {
                try
                {
                    handler(sender, e);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error invoking event handler: {ex.Message}");
                }
            }
        }
    }
    
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

    // Add the LimitedSizeDictionary implementation
    /// <summary>
    /// Thread-safe dictionary with a maximum size limit that automatically removes
    /// the least recently used items when the size limit is reached
    /// </summary>
    public class LimitedSizeDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();
        private readonly Queue<TKey> _accessOrder = new Queue<TKey>();
        private readonly int _maxSize;
        private readonly object _lock = new object();
        
        public LimitedSizeDictionary(int maxSize)
        {
            _maxSize = maxSize > 0 ? maxSize : 100;
        }
        
        // Implement GetAllKeys method to provide access to dictionary keys
        public IEnumerable<TKey> GetAllKeys()
        {
            lock (_lock)
            {
                return _dictionary.Keys.ToList();
            }
        }
        
        // Implement Remove method
        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                return _dictionary.Remove(key);
                // Note: We're not removing from _accessOrder here since it will be
                // reordered naturally by subsequent operations
            }
        }
        
        public TValue this[TKey key]
        {
            get
            {
                lock (_lock)
                {
                    // Update access order
                    UpdateAccessOrder(key);
                    return _dictionary[key];
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_dictionary.ContainsKey(key))
                    {
                        // Update existing value
                        _dictionary[key] = value;
                        UpdateAccessOrder(key);
                    }
                    else
                    {
                        // Check if we need to remove an item
                        if (_dictionary.Count >= _maxSize && _accessOrder.Count > 0)
                        {
                            // Remove the least recently used item
                            TKey oldestKey = _accessOrder.Dequeue();
                            _dictionary.Remove(oldestKey);
                        }
                        
                        // Add the new item
                        _dictionary[key] = value;
                        _accessOrder.Enqueue(key);
                    }
                }
            }
        }
        
        private void UpdateAccessOrder(TKey key)
        {
            // Remove the key from the access order queue
            var tempQueue = new Queue<TKey>();
            while (_accessOrder.Count > 0)
            {
                TKey currentKey = _accessOrder.Dequeue();
                if (!currentKey.Equals(key))
                {
                    tempQueue.Enqueue(currentKey);
                }
            }
            
            // Rebuild the queue with the accessed key at the end
            while (tempQueue.Count > 0)
            {
                _accessOrder.Enqueue(tempQueue.Dequeue());
            }
            
            // Add the accessed key to the end
            _accessOrder.Enqueue(key);
        }
        
        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_lock)
            {
                bool result = _dictionary.TryGetValue(key, out value);
                if (result)
                {
                    UpdateAccessOrder(key);
                }
                return result;
            }
        }
        
        public void Clear()
        {
            lock (_lock)
            {
                _dictionary.Clear();
                _accessOrder.Clear();
            }
        }
        
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _dictionary.Count;
                }
            }
        }
    }
} 