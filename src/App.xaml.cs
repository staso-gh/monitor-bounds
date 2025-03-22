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
    private ThemeManager? _themeManager;
    
    // Public property to expose the theme manager
    public ThemeManager? ThemeManager => _themeManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Set up exception handlers
        DispatcherUnhandledException += Current_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        
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
            
            // If window is visible, force a refresh of the UI without optimization
            if (_mainWindow.IsVisible)
            {
                _mainWindow.UpdateLayout();
            }
        }
    }
    
    private void InitializeTrayIcon()
    {
        try
        {
            // Ensure we can load at least one icon before creating the tray icon
            string iconName = _themeManager?.IsDarkTheme == true ? "dark.ico" : "light.ico";
            string? iconPath = FindIconPath(iconName);
            
            if (string.IsNullOrEmpty(iconPath))
            {
                // If we can't find the expected icon, try the other one
                iconName = _themeManager?.IsDarkTheme == true ? "light.ico" : "dark.ico";
                iconPath = FindIconPath(iconName);
                
                if (string.IsNullOrEmpty(iconPath))
                {
                    // We couldn't find any icon, show error message
                    System.Windows.MessageBox.Show(
                        "Could not find tray icon resources. The application will run without a tray icon.",
                        "Resource Missing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            
            // Load icon from found path
            Icon trayIcon = new Icon(iconPath);
            
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Icon = trayIcon,
                Text = "Screen Region Protector"
            };
            
            // Create context menu
            var contextMenu = new ContextMenuStrip();
            
            // Show/Hide menu item
            var showHideItem = new ToolStripMenuItem("Show/Hide", null, (s, e) => ToggleWindowVisibility());
            contextMenu.Items.Add(showHideItem);
            
            // Separator
            contextMenu.Items.Add(new ToolStripSeparator());
            
            // Exit menu item
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
            contextMenu.Items.Add(exitItem);
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            
            // Double-click action
            _notifyIcon.MouseDoubleClick += (s, e) => ToggleWindowVisibility();
            
            // Debug message to confirm tray icon creation
            Debug.WriteLine($"Tray icon created successfully from path: {iconPath}");
        }
        catch (Exception ex)
        {
            // Show error details
            Debug.WriteLine($"Failed to create tray icon: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Failed to initialize tray icon: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    
    private void UpdateTrayIcon()
    {
        if (_notifyIcon == null || _themeManager == null) return;
        
        try
        {
            // Use the appropriate icon based on the theme
            string iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
            
            // Find the icon file
            string? iconPath = FindIconPath(iconName);
            if (string.IsNullOrEmpty(iconPath))
            {
                // Try alternate icon if primary not found
                iconName = !_themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
                iconPath = FindIconPath(iconName);
                
                if (string.IsNullOrEmpty(iconPath))
                {
                    Debug.WriteLine("Failed to find any icon file for tray icon update");
                    return;
                }
            }
            
            // Load the icon from file and set it
            using (Icon icon = new Icon(iconPath))
            {
                // Use a properly sized icon for the system tray
                Icon trayIcon = new Icon(icon, SystemInformation.SmallIconSize);
                
                // To avoid resource leaks, dispose the old icon first if it exists
                _notifyIcon.Icon?.Dispose();
                
                // Set the new icon
                _notifyIcon.Icon = trayIcon;
                
                Debug.WriteLine($"Tray icon updated successfully from path: {iconPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update tray icon: {ex.Message}");
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
        
        // Hide the window directly without using dispatcher optimizations
        if (_mainWindow != null)
        {
            _mainWindow.Hide();
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
        
        // Force garbage collection before exiting to clean up resources
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
            
            // Directly update windows without using dispatcher optimizations
            foreach (Window window in Windows)
            {
                window.Icon = bitmapImage;
            }
        }
    }
    
    private string? FindIconPath(string iconName)
    {
        try
        {
            // Check existence of each path before returning
            string[] possiblePaths = {
                // Current directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName),
                
                // One level up (project root)
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", iconName)),
                
                // Resources directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconName),
                
                // Executable directory
                Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory, 
                    iconName),
                
                // Fixed paths for debugging
                @"C:\Users\staso\Documents\git\screen-region-protector\" + iconName,
                Path.Combine(Environment.CurrentDirectory, iconName)
            };
            
            foreach (string path in possiblePaths)
            {
                // Skip null paths
                if (string.IsNullOrEmpty(path))
                    continue;
                    
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Found icon file at: {path}");
                    return path;
                }
            }
            
            // Log all checked paths if icon not found
            Debug.WriteLine($"Icon '{iconName}' not found in any of the following paths:");
            foreach (string path in possiblePaths)
            {
                Debug.WriteLine($"  - {path}");
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error searching for icon file: {ex.Message}");
            return null;
        }
    }

    // Add method to update tray menu active state
    public void UpdateTrayMenuActive(bool isActive)
    {
        // Do nothing, Active toggle removed
    }

    private void ToggleWindowVisibility()
    {
        // If the window is visible, hide it
        if (_mainWindow != null && _mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            // Otherwise show it
            ShowMainWindow();
        }
    }
    
    private void ExitApplication()
    {
        // Directly shutdown without using dispatcher optimizations
        Shutdown();
    }
}

