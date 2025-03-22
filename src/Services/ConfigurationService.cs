using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using ScreenRegionProtector.Models;

namespace ScreenRegionProtector.Services
{
    
    // Manages configuration settings for the application
    
    public class ConfigurationService
    {
        private readonly string _configFilePath;
        

        // The list of application windows being monitored

        public List<ApplicationWindow> TargetApplications { get; private set; } = new();
        

        // Determines if the application should start monitoring on startup

        public bool StartMonitoringOnStartup { get; set; } = true;
        

        // Event raised when configuration changes

        public event EventHandler ConfigurationChanged;

        public ConfigurationService()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "WindowBoundaryGuard");

            // Create directory if it doesn't exist
            if (!Directory.Exists(appFolderPath))
            {
                Directory.CreateDirectory(appFolderPath);
            }

            _configFilePath = Path.Combine(appFolderPath, "config.json");
        }


        // Loads configuration from the config file

        public async Task LoadConfigurationAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = await File.ReadAllTextAsync(_configFilePath);
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNameCaseInsensitive = true
                    };
                    
                    var config = JsonSerializer.Deserialize<ConfigurationData>(json, options);
                    
                    if (config != null)
                    {
                        TargetApplications = config.TargetApplications ?? new List<ApplicationWindow>();
                        StartMonitoringOnStartup = config.StartMonitoringOnStartup;
                    }
                }
                else
                {
                    // Create default configuration
                    TargetApplications = new List<ApplicationWindow>();
                    await SaveConfigurationAsync();
                }
            }
            catch (Exception)
            {
                // Create default configuration
                TargetApplications = new List<ApplicationWindow>();
            }
        }


        // Saves configuration to the config file

        public async Task SaveConfigurationAsync()
        {
            // Create clean copies of applications without handles (which can't be serialized)
            var cleanApplications = TargetApplications.Select(app => new ApplicationWindow
            {
                TitlePattern = app.TitlePattern,
                IsActive = app.IsActive,
                RestrictToMonitor = app.RestrictToMonitor
                // Handle is intentionally left out
            }).ToList();
            
            var config = new ConfigurationData
            {
                TargetApplications = cleanApplications,
                StartMonitoringOnStartup = StartMonitoringOnStartup
            };
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            string json = JsonSerializer.Serialize(config, options);
            
            await File.WriteAllTextAsync(_configFilePath, json);
            
            // Notify that configuration has changed
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }


        // Adds a target application to the configuration

        public async Task AddTargetApplicationAsync(ApplicationWindow application)
        {
            TargetApplications.Add(application);
            await SaveConfigurationAsync();
        }


        // Removes a target application from the configuration

        public async Task RemoveTargetApplicationAsync(ApplicationWindow application)
        {
            TargetApplications.Remove(application);
            await SaveConfigurationAsync();
        }
    }

    
    // Data structure for storing configuration in JSON
    
    public class ConfigurationData
    {
        public List<ApplicationWindow> TargetApplications { get; set; } = new();
        public bool StartMonitoringOnStartup { get; set; } = true;
    }
} 