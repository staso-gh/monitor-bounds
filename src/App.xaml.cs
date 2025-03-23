#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Forms;
using MonitorBounds.Services;

namespace MonitorBounds
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private ThemeManager? _themeManager;

        // Expose the theme manager publicly
        public ThemeManager? ThemeManager => _themeManager;
        public bool IsDarkTheme => _themeManager?.IsDarkTheme ?? false;

        private string iconName;
        private string iconPath;

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
            _mainWindow.StateChanged += Window_StateChanged;

            // Initialize tray icon
            InitializeTrayIcon();

            // Update the theme of the taskbar icon
            UpdateTaskbarIcon();

            // Show the main window
            _mainWindow.Show();
        }
        #endregion

        #region Tray Icon and Taskbar Icon
        private void InitializeTrayIcon()
        {
            iconName = _themeManager?.IsDarkTheme == true ? "dark.ico" : "light.ico";
            iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
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
                Text = "Monitor Bounds"
            };

            // Create context menu for tray icon
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(new ToolStripMenuItem("Show/Hide", null, (s, e) => ToggleWindowVisibility()));
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication()));
            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.MouseDoubleClick += (s, e) => ToggleWindowVisibility();

        }

        private void UpdateTrayIcon()
        {
            if (_notifyIcon == null || _themeManager == null)
                return;

            iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
            iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
            // Load and resize the icon for system tray
            using (Icon loadedIcon = new Icon(iconPath))
            {
                Icon newTrayIcon = new Icon(loadedIcon, SystemInformation.SmallIconSize);
                _notifyIcon.Icon = newTrayIcon;
            }
        }

        private void UpdateTaskbarIcon()
        {
            if (_themeManager == null)
                return;

            iconName = _themeManager.IsDarkTheme ? "dark.ico" : "light.ico";
            iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
            var iconUri = new Uri(iconPath, UriKind.Absolute);
            var bitmapImage = new System.Windows.Media.Imaging.BitmapImage(iconUri);
            foreach (Window window in Windows)
            {
                window.Icon = bitmapImage;
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

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (_mainWindow?.WindowState == WindowState.Minimized)
            {
                ToggleWindowVisibility();
                _mainWindow.WindowState = WindowState.Normal;
            }
        }
        #endregion

        #region Window Visibility and Application Exit
        private void ToggleWindowVisibility()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
            }

            if (_mainWindow.IsVisible)
                _mainWindow.Hide();
            else
                _mainWindow.Show();
        }

        private void ExitApplication()
        {
            Shutdown();
        }
        #endregion
    }
}
