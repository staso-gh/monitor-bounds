#nullable enable
using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MonitorBounds.Services
{
    /// <summary>
    /// Manages theme detection and switching for the application.
    /// </summary>
    public class ThemeManager : IDisposable
    {
        private bool _isDarkTheme;
        private bool _disposed;

        /// <summary>
        /// Occurs when the theme changes.
        /// </summary>
        public event EventHandler<bool>? ThemeChanged;

        /// <summary>
        /// Gets whether the current theme is dark.
        /// </summary>
        public bool IsDarkTheme => _isDarkTheme;

        #region Constructor and Theme Detection

        /// <summary>
        /// Creates a new instance of ThemeManager and detects the current system theme.
        /// </summary>
        public ThemeManager()
        {
            DetectSystemTheme(isInitializing: true);
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        /// <summary>
        /// Manually toggles between light and dark themes.
        /// </summary>
        public void ToggleTheme()
        {
            if (_disposed) return;
            
            _isDarkTheme = !_isDarkTheme;
            OnThemeChanged(_isDarkTheme);
        }

        /// <summary>
        /// Forces the theme to a specific value.
        /// </summary>
        /// <param name="isDarkTheme">True to set dark theme; otherwise, false.</param>
        public void SetTheme(bool isDarkTheme)
        {
            if (_disposed) return;
            
            if (_isDarkTheme != isDarkTheme)
            {
                _isDarkTheme = isDarkTheme;
                OnThemeChanged(_isDarkTheme);
            }
        }

        /// <summary>
        /// Handles system user preference changes to detect theme updates.
        /// </summary>
        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_disposed) return;
            
            if (e.Category == UserPreferenceCategory.General)
            {
                DetectSystemTheme(isInitializing: false);
            }
        }

        /// <summary>
        /// Detects the current system theme by reading the registry.
        /// </summary>
        /// <param name="isInitializing">Indicates if the call is during initialization.</param>
        private void DetectSystemTheme(bool isInitializing)
        {
            if (_disposed) return;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        bool previousTheme = _isDarkTheme;
                        // Windows returns 0 for dark theme when AppsUseLightTheme is false.
                        bool newTheme = value != null && (int)value == 0;

                        // Update and notify if initializing or if the theme changed.
                        if (isInitializing || previousTheme != newTheme)
                        {
                            _isDarkTheme = newTheme;
                            OnThemeChanged(_isDarkTheme);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Raises the ThemeChanged event.
        /// </summary>
        /// <param name="isDarkTheme">The new theme state.</param>
        protected virtual void OnThemeChanged(bool isDarkTheme)
        {
            ThemeChanged?.Invoke(this, isDarkTheme);
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Disposes the ThemeManager and unregisters event handlers.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the ThemeManager and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer for ThemeManager.
        /// </summary>
        ~ThemeManager()
        {
            Dispose(disposing: false);
        }

        #endregion
    }
}
