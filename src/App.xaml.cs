#nullable enable
using System;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using ScreenRegionProtector.Services;

namespace ScreenRegionProtector
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private ThemeManager? _themeManager;

        // Expose the theme manager publicly
        public ThemeManager? ThemeManager => _themeManager;
        public bool IsDarkTheme => _themeManager?.IsDarkTheme ?? false;

        #region Application Startup

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handlers
            DispatcherUnhandledException += Current_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Initialize theme manager and subscribe to its event
            _themeManager = new ThemeManager();
            _themeManager.ThemeChanged += ThemeManager_ThemeChanged;

            // Initialize main window and subscribe to its closing event
            _mainWindow = new MainWindow();
            _mainWindow.Closing += MainWindow_Closing;

            // Initialize tray icon
            InitializeTrayIcon();

            // Show the main window
            _mainWindow.Show();
        }

        #endregion

        #region Tray Icon and Taskbar Icon

        private void InitializeTrayIcon()
        {
            try
            {
                // Attempt to get a valid icon path using current theme
                string iconName = _themeManager?.IsDarkTheme == true ? "dark.ico" : "light.ico";
                string? iconPath = ResolveIconPath(iconName);
                if (string.IsNullOrEmpty(iconPath))
                {
                    System.Windows.MessageBox.Show(
                        "Could not find tray icon resources. The application will run without a tray icon.",
                        "Resource Missing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Load the icon and create the NotifyIcon
                Icon trayIcon = new Icon(iconPath);
                _notifyIcon = new NotifyIcon
                {
                    Visible = true,
                    Icon = trayIcon,
                    Text = "Screen Region Protector"
                };

                // Create context menu for tray icon
                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add(new ToolStripMenuItem("Show/Hide", null, (s, e) => ToggleWindowVisibility()));
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication()));
                _notifyIcon.ContextMenuStrip = contextMenu;

                _notifyIcon.MouseDoubleClick += (s, e) => ToggleWindowVisibility();
                Debug.WriteLine($"Tray icon created successfully from path: {iconPath}");
            }
            catch (Exception ex)
            {
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
            if (_notifyIcon == null || _themeManager == null)
                return;

            try
            {
                string iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
                string? iconPath = ResolveIconPath(iconName);
                if (string.IsNullOrEmpty(iconPath))
                {
                    Debug.WriteLine("Failed to find any valid icon file for tray icon update");
                    return;
                }

                // Load and resize the icon for system tray
                using (Icon loadedIcon = new Icon(iconPath))
                {
                    Icon newTrayIcon = new Icon(loadedIcon, SystemInformation.SmallIconSize);
                    _notifyIcon.Icon?.Dispose();
                    _notifyIcon.Icon = newTrayIcon;
                    Debug.WriteLine($"Tray icon updated successfully from path: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update tray icon: {ex.Message}");
            }
        }

        private void UpdateTaskbarIcon()
        {
            if (_themeManager == null)
                return;

            string iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
            string? iconPath = ResolveIconPath(iconName);
            if (!string.IsNullOrEmpty(iconPath))
            {
                var iconUri = new Uri(iconPath, UriKind.Absolute);
                var bitmapImage = new System.Windows.Media.Imaging.BitmapImage(iconUri);
                foreach (Window window in Windows)
                {
                    window.Icon = bitmapImage;
                }
            }
        }

        #endregion

        #region Event Handlers

        private void ThemeManager_ThemeChanged(object? sender, bool isDarkTheme)
        {
            UpdateTrayIcon();
            UpdateTaskbarIcon();

            if (_mainWindow != null)
            {
                _mainWindow.ApplyTheme(isDarkTheme);
                if (_mainWindow.IsVisible)
                {
                    _mainWindow.UpdateLayout();
                }
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel closing to allow minimizing to tray
            e.Cancel = true;
            _mainWindow?.Hide();
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

        #endregion

        #region Window Visibility and Application Exit

        private void ToggleWindowVisibility()
        {
            if (_mainWindow != null && _mainWindow.IsVisible)
                _mainWindow.Hide();
            else
                ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closing += MainWindow_Closing;
            }

            if (!_mainWindow.IsVisible)
                _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
            _mainWindow.Focus();
        }

        private void ExitApplication()
        {
            Shutdown();
        }

        #endregion

        #region Application Exit Cleanup

        protected override void OnExit(ExitEventArgs e)
        {
            // Unsubscribe and dispose of theme manager
            if (_themeManager != null)
            {
                _themeManager.ThemeChanged -= ThemeManager_ThemeChanged;
                _themeManager.Dispose();
                _themeManager = null;
            }

            DispatcherUnhandledException -= Current_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;

            // Clean up main window
            if (_mainWindow != null)
            {
                _mainWindow.Closing -= MainWindow_Closing;
                _mainWindow.Dispose();
                _mainWindow.Close();
                _mainWindow = null;
            }

            // Dispose tray icon and its context menu
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                if (_notifyIcon.ContextMenuStrip != null)
                {
                    foreach (var item in _notifyIcon.ContextMenuStrip.Items.OfType<IDisposable>())
                    {
                        item.Dispose();
                    }
                    _notifyIcon.ContextMenuStrip.Dispose();
                    _notifyIcon.ContextMenuStrip = null;
                }
                _notifyIcon.Icon?.Dispose();
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            // Optionally force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();

            base.OnExit(e);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Tries to find the specified icon file; if not found, attempts the alternate icon.
        /// </summary>
        /// <param name="iconName">Primary icon file name</param>
        /// <returns>Full path to a valid icon file, or null if none found</returns>
        private string? ResolveIconPath(string iconName)
        {
            // List of possible directories to search
            string[] possiblePaths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", iconName)),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconName),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory, iconName),
                // Additional fixed or debugging paths (adjust as needed)
                Path.Combine(@"C:\Users\staso\Documents\git\screen-region-protector\", iconName),
                Path.Combine(Environment.CurrentDirectory, iconName)
            };

            foreach (string path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Debug.WriteLine($"Found icon file at: {path}");
                    return path;
                }
            }

            // Try alternate icon (if primary fails)
            string alternateIcon = iconName.Equals("dark.ico", StringComparison.OrdinalIgnoreCase) ? "light.ico" : "dark.ico";
            foreach (string path in possiblePaths.Select(p => p.Replace(iconName, alternateIcon)))
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Debug.WriteLine($"Found alternate icon file at: {path}");
                    return path;
                }
            }

            Debug.WriteLine($"Icon '{iconName}' not found in any of the following paths:");
            foreach (string path in possiblePaths)
            {
                Debug.WriteLine($"  - {path}");
            }

            return null;
        }

        // Dummy method kept for compatibility; active toggle functionality removed.
        public void UpdateTrayMenuActive(bool isActive) { }

        #endregion
    }
}
