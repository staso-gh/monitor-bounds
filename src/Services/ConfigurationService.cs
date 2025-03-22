using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ScreenRegionProtector.Models;

namespace ScreenRegionProtector.Services
{
    // Manages configuration settings for the application.
    public class ConfigurationService
    {
        private readonly string _configFilePath;

        // The list of application windows being monitored.
        public List<ApplicationWindow> TargetApplications { get; private set; } = new();

        // Determines if the application should start monitoring on startup.
        public bool StartMonitoringOnStartup { get; set; } = true;

        // Event raised when configuration changes.
        public event EventHandler ConfigurationChanged;

        public ConfigurationService()
        {
            // Get the local application data directory.
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "ScreenRegionProtector");

            // Ensure the directory exists. If not, attempt creation; if that fails, fallback to temp.
            if (!Directory.Exists(appFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(appFolderPath);
                }
                catch
                {
                    appFolderPath = Path.Combine(Path.GetTempPath(), "ScreenRegionProtector");
                    Directory.CreateDirectory(appFolderPath);
                }
            }

            _configFilePath = Path.Combine(appFolderPath, "settings.json");
        }

        // Loads configuration from the config file.
        public async Task LoadConfigurationAsync()
        {
            bool useDefaultConfig = false;

            if (File.Exists(_configFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_configFilePath).ConfigureAwait(false);

                    // Check if file content is valid.
                    if (string.IsNullOrWhiteSpace(json) || json.Length < 10)
                    {
                        useDefaultConfig = true;
                    }
                    else
                    {
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNameCaseInsensitive = true
                        };

                        var config = JsonSerializer.Deserialize<ConfigurationData>(json, options);
                        if (config != null)
                        {
                            // Clear existing items.
                            TargetApplications.Clear();

                            // Populate a fresh copy of applications to avoid reference issues.
                            if (config.TargetApplications != null && config.TargetApplications.Count > 0)
                            {
                                foreach (var app in config.TargetApplications)
                                {
                                    if (app != null && !string.IsNullOrWhiteSpace(app.TitlePattern))
                                    {
                                        var freshCopy = new ApplicationWindow
                                        {
                                            TitlePattern = app.TitlePattern,
                                            IsActive = app.IsActive,
                                            RestrictToMonitor = app.RestrictToMonitor
                                        };
                                        TargetApplications.Add(freshCopy);
                                    }
                                }
                            }
                            else
                            {
                                useDefaultConfig = true;
                            }

                            StartMonitoringOnStartup = config.StartMonitoringOnStartup;
                        }
                        else
                        {
                            useDefaultConfig = true;
                        }
                    }
                }
                catch
                {
                    useDefaultConfig = true;
                    // If the config file is corrupt, attempt to back it up.
                    try
                    {
                        string backupPath = _configFilePath + ".backup." + DateTime.Now.ToString("yyyyMMddHHmmss");
                        File.Move(_configFilePath, backupPath);
                    }
                    catch { }
                }
            }
            else
            {
                useDefaultConfig = true;
            }

            // Apply default configuration if needed.
            if (useDefaultConfig)
            {
                TargetApplications = new List<ApplicationWindow>();
                StartMonitoringOnStartup = true;
                await SaveConfigurationAsync().ConfigureAwait(false);
            }
        }

        // Saves configuration to the config file.
        public async Task SaveConfigurationAsync()
        {
            try
            {
                // Create clean copies of applications (excluding unserializable data).
                var cleanApplications = TargetApplications.Select(app => new ApplicationWindow
                {
                    TitlePattern = app.TitlePattern,
                    IsActive = app.IsActive,
                    RestrictToMonitor = app.RestrictToMonitor
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

                // Ensure directory exists.
                string configDir = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                // Use a temporary file for a safe write.
                string tempFilePath = Path.Combine(configDir, $"config_temp_{Guid.NewGuid()}.json");

                try
                {
                    await File.WriteAllTextAsync(tempFilePath, json).ConfigureAwait(false);

                    if (File.Exists(_configFilePath))
                    {
                        File.Delete(_configFilePath);
                    }

                    File.Move(tempFilePath, _configFilePath);
                }
                finally
                {
                    // Clean up the temporary file if it still exists.
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }

                // Notify subscribers that configuration has changed.
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                throw;
            }
        }

        // Adds a target application to the configuration.
        public async Task AddTargetApplicationAsync(ApplicationWindow application)
        {
            TargetApplications.Add(application);
            await SaveConfigurationAsync().ConfigureAwait(false);
        }

        // Removes a target application from the configuration.
        public async Task RemoveTargetApplicationAsync(ApplicationWindow application)
        {
            TargetApplications.Remove(application);
            await SaveConfigurationAsync().ConfigureAwait(false);
        }

        // Directly saves configuration with the provided applications.
        public async Task SaveConfigurationDirectAsync(IEnumerable<ApplicationWindow> applications, bool startMonitoringOnStartup)
        {
            try
            {
                var cleanApplications = applications.Select(app => new ApplicationWindow
                {
                    TitlePattern = app.TitlePattern,
                    IsActive = app.IsActive,
                    RestrictToMonitor = app.RestrictToMonitor
                }).ToList();

                var config = new ConfigurationData
                {
                    TargetApplications = cleanApplications,
                    StartMonitoringOnStartup = startMonitoringOnStartup
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                string json = JsonSerializer.Serialize(config, options);

                // Ensure the directory exists.
                string configDir = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                string tempFilePath = Path.Combine(configDir, $"config_temp_{Guid.NewGuid()}.json");

                try
                {
                    await File.WriteAllTextAsync(tempFilePath, json).ConfigureAwait(false);

                    if (File.Exists(_configFilePath))
                    {
                        File.Delete(_configFilePath);
                    }

                    File.Move(tempFilePath, _configFilePath);
                }
                catch
                {
                    // Fallback: try writing directly if temp file write fails.
                    await File.WriteAllTextAsync(_configFilePath, json).ConfigureAwait(false);
                }
                finally
                {
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                        }
                        catch { }
                    }
                }

                // Update internal state.
                TargetApplications.Clear();
                foreach (var app in cleanApplications)
                {
                    TargetApplications.Add(app);
                }

                StartMonitoringOnStartup = startMonitoringOnStartup;
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    // Data structure for storing configuration in JSON.
    public class ConfigurationData
    {
        public List<ApplicationWindow> TargetApplications { get; set; } = new();
        public bool StartMonitoringOnStartup { get; set; } = true;
    }
}
