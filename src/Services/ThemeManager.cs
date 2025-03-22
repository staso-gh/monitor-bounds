using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace ScreenRegionProtector.Services
{
    
    //Manages theme detection and switching for the application
    
    public class ThemeManager : IDisposable
    {
        private bool _isDarkTheme;
        private bool _isDisposed = false;


        //Event that fires when the theme changes

        public event EventHandler<bool> ThemeChanged;


        //Gets whether the current theme is dark

        public bool IsDarkTheme => _isDarkTheme;


        //Creates a new instance of ThemeManager and detects the current system theme

        public ThemeManager()
        {
            // Detect current theme
            DetectSystemTheme(true);

            // Subscribe to system theme changes
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }


        //Manually toggles between light and dark themes

        public void ToggleTheme()
        {
            if (_isDisposed) return;
            
            _isDarkTheme = !_isDarkTheme;

            // Notify subscribers
            ThemeChanged?.Invoke(this, _isDarkTheme);
        }


        //Force the theme to a specific value

        public void SetTheme(bool isDarkTheme)
        {
            if (_isDisposed) return;
            
            if (_isDarkTheme != isDarkTheme)
            {
                _isDarkTheme = isDarkTheme;

                // Notify subscribers
                ThemeChanged?.Invoke(this, _isDarkTheme);
            }
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_isDisposed) return;
            
            if (e.Category == UserPreferenceCategory.General)
            {
                DetectSystemTheme(false);
            }
        }

        private void DetectSystemTheme(bool isInitializing = false)
        {
            if (_isDisposed) return;
            
            // Check if Windows is using dark mode
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    bool previousTheme = _isDarkTheme;
                    bool newTheme = value != null && (int)value == 0;
                    
                    // When initializing or theme has changed, update and notify
                    if (isInitializing || previousTheme != newTheme)
                    {
                        _isDarkTheme = newTheme;

                        // Always notify on initialization to ensure proper theme application
                        if (isInitializing || previousTheme != _isDarkTheme)
                        {
                            // Immediately notify subscribers without any optimization delays
                            ThemeChanged?.Invoke(this, _isDarkTheme);
                        }
                    }
                }
            }
        }


        //Disposes the ThemeManager and unregisters event handlers

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Unsubscribe from system events
                    SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                }

                _isDisposed = true;
            }
        }

        ~ThemeManager()
        {
            Dispose(false);
        }
    }
} 