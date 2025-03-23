using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace MonitorBounds.Services
{
    /// <summary>
    /// Manages the application's startup settings, allowing it to launch when Windows starts.
    /// </summary>
    public class StartupManager
    {
        private const string RUN_REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private readonly string _applicationName;
        private readonly string _applicationPath;

        /// <summary>
        /// Initializes a new instance of the StartupManager class.
        /// </summary>
        public StartupManager()
        {
            // Get the executing assembly's name for the registry key
            _applicationName = "MonitorBounds";
            
            // Get the full path to the executable
            _applicationPath = GetExecutablePath();
        }

        /// <summary>
        /// Checks if the application is configured to start with Windows.
        /// </summary>
        /// <returns>True if the application starts with Windows, otherwise false.</returns>
        public bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RUN_REGISTRY_KEY, false))
            {
                if (key == null) return false;
                
                var value = key.GetValue(_applicationName) as string;
                return !string.IsNullOrEmpty(value) && value.Equals(_applicationPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Enables the application to start with Windows.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        public bool EnableStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RUN_REGISTRY_KEY, true))
                {
                    if (key == null) return false;
                    
                    key.SetValue(_applicationName, _applicationPath);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Disables the application from starting with Windows.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        public bool DisableStartup()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RUN_REGISTRY_KEY, true))
                {
                    if (key == null) return false;
                    
                    if (key.GetValue(_applicationName) != null)
                    {
                        key.DeleteValue(_applicationName, false);
                    }
                    
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the path to the application's executable.
        /// </summary>
        /// <returns>The full path to the executable.</returns>
        private string GetExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule?.FileName ?? 
                   Path.Combine(System.AppContext.BaseDirectory, Process.GetCurrentProcess().ProcessName + ".exe");
        }
    }
} 