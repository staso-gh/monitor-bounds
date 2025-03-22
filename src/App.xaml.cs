#nullable enable
using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using Microsoft.Win32;
using ScreenRegionProtector.Services;

namespace ScreenRegionProtector;


// Interaction logic for App.xaml

public partial class App : System.Windows.Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private bool _isActive = true;
    private ThemeManager? _themeManager;
    
    // Event for notifying the main window when active state changes
    public event EventHandler<bool>? ActiveChanged;
    
    // Public property to expose the theme manager
    public ThemeManager? ThemeManager => _themeManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Set up exception handlers
        DispatcherUnhandledException += Current_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        
        // Adjust process priority to be slightly below normal to reduce system impact
        try
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                process.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set process priority: {ex.Message}");
        }
        
        // Initialize theme manager
        _themeManager = new ThemeManager();
        _themeManager.ThemeChanged += ThemeManager_ThemeChanged;
        
        // Create main window and initialize tray icon
        _mainWindow = new MainWindow();
        _mainWindow.Closing += MainWindow_Closing;
        
        // Initialize tray icon
        InitializeTrayIcon();
        
        // Show main window
        _mainWindow.Show();
    }
    
    private void ThemeManager_ThemeChanged(object? sender, bool isDarkTheme)
    {
        // Update the tray icon
        UpdateTrayIcon();
        
        // Update the taskbar icon for any open windows
        UpdateTaskbarIcon();
        
        // Update the main window theme if it exists
        if (_mainWindow != null)
        {
            // Apply the theme, regardless of window visibility
            _mainWindow.ApplyTheme(isDarkTheme);
            
            // If window is visible, force a refresh of the UI
            if (_mainWindow.IsVisible)
            {
                // Force UI refresh - use BeginInvoke to avoid potential deadlocks
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => {
                    _mainWindow.UpdateLayout();
                }), DispatcherPriority.Background);
            }
        }
    }
    
    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Icon = _themeManager?.IsDarkTheme == true ? 
                Resources["DarkIcon"] as System.Drawing.Icon : 
                Resources["LightIcon"] as System.Drawing.Icon,
            Text = "Screen Region Protector"
        };
        
        // Create context menu
        var contextMenu = new ContextMenuStrip();
        
        // Show/Hide menu item
        var showHideItem = new ToolStripMenuItem("Show/Hide", null, (s, e) => ToggleWindowVisibility());
        contextMenu.Items.Add(showHideItem);
        
        // Active toggle menu item
        var toggleItem = new ToolStripMenuItem("Active", null, (s, e) => ToggleActive())
        {
            Checked = _isActive
        };
        contextMenu.Items.Add(toggleItem);
        
        // Separator
        contextMenu.Items.Add(new ToolStripSeparator());
        
        // Exit menu item
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
        contextMenu.Items.Add(exitItem);
        
        _notifyIcon.ContextMenuStrip = contextMenu;
        
        // Double-click action
        _notifyIcon.MouseDoubleClick += (s, e) => ToggleWindowVisibility();
    }
    
    private void UpdateTrayIcon()
    {
        if (_notifyIcon == null || _themeManager == null) return;
        
        // Use the appropriate icon based on the theme
        string iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
        
        // Find the icon file
        string? iconPath = FindIconPath(iconName);
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                // Load the icon from file and set it
                using (Icon icon = new Icon(iconPath))
                {
                    // Use a properly sized icon for the system tray
                    Icon trayIcon = new Icon(icon, SystemInformation.SmallIconSize);
                    
                    // To avoid resource leaks, dispose the old icon first if it exists
                    _notifyIcon.Icon?.Dispose();
                    
                    // Set the new icon
                    _notifyIcon.Icon = trayIcon;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
            }
        }
    }
    
    private void ShowMainWindow()
    {
        // Check if the window exists
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closing += MainWindow_Closing;
        }
        
        // Show and activate the window
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }
        
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        
        _mainWindow.Activate();
        _mainWindow.Focus();
    }
    
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Instead of closing, minimize to tray
        e.Cancel = true;
        
        // Use BeginInvoke to avoid potential issues
        Dispatcher.BeginInvoke(new Action(() => {
            if (_mainWindow != null)
            {
                _mainWindow.Hide();
            }
        }), DispatcherPriority.Background);
    }
    
    private void OnAppActiveChanged(bool isActive)
    {
        // Update the tray icon menu
        if (_notifyIcon?.ContextMenuStrip?.Items.Count > 1 && 
            _notifyIcon.ContextMenuStrip.Items[1] is ToolStripMenuItem toggleItem)
        {
            toggleItem.Checked = isActive;
        }
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        // Unsubscribe from events to prevent memory leaks
        if (_themeManager != null)
        {
            _themeManager.ThemeChanged -= ThemeManager_ThemeChanged;
            _themeManager.Dispose();
            _themeManager = null;
        }
        
        this.DispatcherUnhandledException -= Current_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        
        // Close the main window if it exists
        if (_mainWindow != null)
        {
            // Unsubscribe from events
            _mainWindow.Closing -= MainWindow_Closing;
            
            // Properly dispose the window's resources
            _mainWindow.Dispose();
            
            // Close the window
            _mainWindow.Close();
            _mainWindow = null;
        }
        
        // Dispose notification icon
        if (_notifyIcon != null)
        {
            // Before disposing, hide the icon
            _notifyIcon.Visible = false;
            
            // Dispose the context menu to prevent memory leaks
            if (_notifyIcon.ContextMenuStrip != null)
            {
                foreach (var item in _notifyIcon.ContextMenuStrip.Items)
                {
                    if (item is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                
                _notifyIcon.ContextMenuStrip.Dispose();
                _notifyIcon.ContextMenuStrip = null;
            }
            
            // Dispose the icon
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        
        // Force garbage collection before exiting
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        base.OnExit(e);
    }

    private void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace}",
                      "Error",
                      MessageBoxButton.OK,
                      MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            System.Windows.MessageBox.Show($"A fatal error occurred: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                          "Fatal Error",
                          MessageBoxButton.OK,
                          MessageBoxImage.Error);
        }
    }

    // Public property to expose current theme
    public bool IsDarkTheme => _themeManager?.IsDarkTheme ?? false;

    private void UpdateTaskbarIcon()
    {
        if (_themeManager == null) return;
        
        string iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
        
        // Load the icon and update all app windows
        string? iconPath = FindIconPath(iconName);
        if (!string.IsNullOrEmpty(iconPath))
        {
            // Create an icon source from the file
            var iconUri = new Uri(iconPath, UriKind.Absolute);
            System.Windows.Media.Imaging.BitmapImage bitmapImage = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            
            // Use dispatcher to avoid cross-thread issues
            Dispatcher.BeginInvoke(new Action(() => {
                // Set the icon for all windows 
                foreach (Window window in Windows)
                {
                    window.Icon = bitmapImage;
                }
            }), DispatcherPriority.Background);
        }
    }
    
    private string? FindIconPath(string iconName)
    {
        // First try the project root directory (one level up)
        string rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", iconName);
        if (File.Exists(rootPath))
        {
            return Path.GetFullPath(rootPath);
        }
        
        // Then try the current directory
        string currentDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
        if (File.Exists(currentDirPath))
        {
            return currentDirPath;
        }
        
        // Finally try the application directory
        string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconName);
        if (File.Exists(appPath))
        {
            return appPath;
        }
        
        return null;
    }

    // Add method to update tray menu active state
    public void UpdateTrayMenuActive(bool isActive)
    {
        _isActive = isActive;
        OnAppActiveChanged(isActive);
    }

    private void ToggleWindowVisibility()
    {
        // Use Dispatcher to ensure we're on the UI thread
        Dispatcher.BeginInvoke(new Action(() => {
            if (_mainWindow != null)
            {
                if (_mainWindow.IsVisible)
                {
                    _mainWindow.Hide();
                }
                else
                {
                    _mainWindow.Show();
                    _mainWindow.Activate();
                    _mainWindow.WindowState = WindowState.Normal;
                }
            }
        }), DispatcherPriority.Normal);
    }
    
    private void ToggleActive()
    {
        _isActive = !_isActive;
        
        // Update the checked state
        if (_notifyIcon?.ContextMenuStrip?.Items.Count > 1 && 
            _notifyIcon.ContextMenuStrip.Items[1] is ToolStripMenuItem toggleItem)
        {
            toggleItem.Checked = _isActive;
        }
        
        // Notify listeners
        ActiveChanged?.Invoke(this, _isActive);
    }
    
    private void ExitApplication()
    {
        // Use Dispatcher to ensure we're on the UI thread
        Dispatcher.BeginInvoke(new Action(() => {
            Shutdown();
        }), DispatcherPriority.Normal);
    }
}

