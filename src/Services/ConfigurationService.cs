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
                System.Diagnostics.Debug.WriteLine($"ConfigurationService.LoadConfigurationAsync() - STARTING");
                System.Diagnostics.Debug.WriteLine($"Config file path: {_configFilePath}");
                
                // If the config file exists, load it
                if (File.Exists(_configFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("Config file exists, loading...");
                    try
                    {
                        string json = await File.ReadAllTextAsync(_configFilePath);
                        System.Diagnostics.Debug.WriteLine($"Read {json.Length} characters from config file");
                        
                        // Show a preview of the file for debugging
                        if (json.Length > 0)
                        {
                            int previewLength = Math.Min(100, json.Length);
                            System.Diagnostics.Debug.WriteLine($"JSON preview: {json.Substring(0, previewLength)}...");
                        }
                        
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            PropertyNameCaseInsensitive = true
                        };
                        
                        var config = JsonSerializer.Deserialize<ConfigurationData>(json, options);
                        
                        if (config != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Deserialized config successfully");
                            
                            // Clear existing applications
                            TargetApplications.Clear();
                            
                            // Create fresh copies of all applications to avoid reference issues
                            if (config.TargetApplications != null)
                            {
                                foreach (var app in config.TargetApplications)
                                {
                                    var freshCopy = new ApplicationWindow
                                    {
                                        TitlePattern = app.TitlePattern,
                                        IsActive = app.IsActive,
                                        RestrictToMonitor = app.RestrictToMonitor
                                    };
                                    
                                    TargetApplications.Add(freshCopy);
                                    System.Diagnostics.Debug.WriteLine($"Added app from config: '{freshCopy.TitlePattern}', Active={freshCopy.IsActive}, Monitor={freshCopy.RestrictToMonitor}");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("Config had null TargetApplications list, using empty list");
                                TargetApplications = new List<ApplicationWindow>();
                            }
                            
                            StartMonitoringOnStartup = config.StartMonitoringOnStartup;
                            System.Diagnostics.Debug.WriteLine($"Set StartMonitoringOnStartup = {StartMonitoringOnStartup}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Deserialized config was null, using default values");
                            TargetApplications = new List<ApplicationWindow>();
                            StartMonitoringOnStartup = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                        
                        // Create default configuration on error
                        System.Diagnostics.Debug.WriteLine("Using default configuration due to error");
                        TargetApplications = new List<ApplicationWindow>();
                        StartMonitoringOnStartup = false;
                    }
                }
                else
                {
                    // Create default configuration
                    System.Diagnostics.Debug.WriteLine("Config file doesn't exist, creating default configuration");
                    TargetApplications = new List<ApplicationWindow>();
                    StartMonitoringOnStartup = false;
                    
                    // Create default file
                    await SaveConfigurationAsync();
                }
                
                System.Diagnostics.Debug.WriteLine($"Final configuration loaded: {TargetApplications.Count} applications, StartMonitoring={StartMonitoringOnStartup}");
                System.Diagnostics.Debug.WriteLine($"ConfigurationService.LoadConfigurationAsync() - COMPLETE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in ConfigurationService.LoadConfigurationAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                
                // Create empty config as fallback
                TargetApplications = new List<ApplicationWindow>();
                StartMonitoringOnStartup = false;
            }
        }


        // Saves configuration to the config file

        public async Task SaveConfigurationAsync()
        {
            try 
            {
                System.Diagnostics.Debug.WriteLine($"ConfigurationService.SaveConfigurationAsync() - STARTING");
                System.Diagnostics.Debug.WriteLine($"TargetApplications contains {TargetApplications.Count} items before saving");
                
                foreach (var app in TargetApplications)
                {
                    System.Diagnostics.Debug.WriteLine($"  App in TargetApplications: '{app.TitlePattern}', Active={app.IsActive}, Monitor={app.RestrictToMonitor}, HashCode={app.GetHashCode()}");
                }
                
                // Create clean copies of applications without handles (which can't be serialized)
                var cleanApplications = TargetApplications.Select(app => new ApplicationWindow
                {
                    TitlePattern = app.TitlePattern,
                    IsActive = app.IsActive,
                    RestrictToMonitor = app.RestrictToMonitor
                    // Handle is intentionally left out
                }).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Created {cleanApplications.Count} clean application copies for serialization");
                
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
                System.Diagnostics.Debug.WriteLine($"Serialized configuration JSON length: {json.Length} characters");
                
                // For debugging, output a bit of the JSON
                if (json.Length > 0)
                {
                    int previewLength = Math.Min(100, json.Length);
                    System.Diagnostics.Debug.WriteLine($"JSON preview: {json.Substring(0, previewLength)}...");
                }
                
                // Make sure the directory exists
                string configDir = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(configDir))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating directory: {configDir}");
                    Directory.CreateDirectory(configDir);
                }
                
                System.Diagnostics.Debug.WriteLine($"Writing to file: {_configFilePath}");
                
                // First use a temporary file to avoid problems with partial writes
                string tempFilePath = Path.Combine(configDir, $"config_temp_{Guid.NewGuid()}.json");
                
                try
                {
                    // Write to temp file first
                    await File.WriteAllTextAsync(tempFilePath, json);
                    
                    // If successful, replace the actual config file
                    if (File.Exists(_configFilePath))
                    {
                        System.Diagnostics.Debug.WriteLine("Replacing existing config file");
                        File.Delete(_configFilePath);
                    }
                    
                    File.Move(tempFilePath, _configFilePath);
                    System.Diagnostics.Debug.WriteLine($"Successfully wrote configuration file");
                    
                    // Verify the file was written correctly
                    bool fileExists = File.Exists(_configFilePath);
                    System.Diagnostics.Debug.WriteLine($"Verified config file exists: {fileExists}");
                    
                    if (fileExists)
                    {
                        long fileSize = new FileInfo(_configFilePath).Length;
                        System.Diagnostics.Debug.WriteLine($"Config file size: {fileSize} bytes");
                    }
                }
                finally
                {
                    // Clean up temp file if it still exists
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                
                // Notify that configuration has changed
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine($"ConfigurationService.SaveConfigurationAsync() - COMPLETE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in ConfigurationService.SaveConfigurationAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                throw; // Rethrow to propagate
            }
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

        // Directly saves configuration with the provided applications, bypassing TargetApplications collection
        public async Task SaveConfigurationDirectAsync(IEnumerable<ApplicationWindow> applications, bool startMonitoringOnStartup)
        {
            try 
            {
                System.Diagnostics.Debug.WriteLine($"ConfigurationService.SaveConfigurationDirectAsync() - STARTING with {applications.Count()} applications");
                
                // Create clean copies of applications without handles (which can't be serialized)
                var cleanApplications = applications.Select(app => new ApplicationWindow
                {
                    TitlePattern = app.TitlePattern,
                    IsActive = app.IsActive,
                    RestrictToMonitor = app.RestrictToMonitor
                    // Handle is intentionally left out
                }).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Created {cleanApplications.Count} clean application copies for serialization");
                
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
                System.Diagnostics.Debug.WriteLine($"Serialized configuration JSON length: {json.Length} characters");
                
                // For debugging, output a bit of the JSON
                if (json.Length > 0)
                {
                    int previewLength = Math.Min(100, json.Length);
                    System.Diagnostics.Debug.WriteLine($"JSON preview: {json.Substring(0, previewLength)}...");
                }
                
                // Make sure the directory exists
                string configDir = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(configDir))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating directory: {configDir}");
                    Directory.CreateDirectory(configDir);
                }
                
                // Use a temporary file for safe writing
                string tempFilePath = Path.Combine(configDir, $"config_temp_{Guid.NewGuid()}.json");
                
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Writing to temp file: {tempFilePath}");
                    await File.WriteAllTextAsync(tempFilePath, json);
                    
                    // If writing to temp file succeeded, replace the original file
                    if (File.Exists(_configFilePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"Deleting existing config file: {_configFilePath}");
                        File.Delete(_configFilePath);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Moving temp file to final location: {_configFilePath}");
                    File.Move(tempFilePath, _configFilePath);
                    
                    // Verify file was successfully written
                    if (File.Exists(_configFilePath))
                    {
                        long fileSize = new FileInfo(_configFilePath).Length;
                        System.Diagnostics.Debug.WriteLine($"Config file successfully written: {fileSize} bytes");
                    }
                    else
                    {
                        throw new IOException($"Failed to verify config file existence after save: {_configFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during file write: {ex.Message}");
                    
                    // If temp file failed, try direct write as fallback
                    System.Diagnostics.Debug.WriteLine($"Attempting direct write to: {_configFilePath}");
                    await File.WriteAllTextAsync(_configFilePath, json);
                }
                finally
                {
                    // Clean up temp file if it still exists
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to delete temp file: {ex.Message}");
                        }
                    }
                }
                
                // Update our internal collections to match what was just saved
                TargetApplications.Clear();
                foreach (var app in cleanApplications) 
                {
                    TargetApplications.Add(app);
                }
                StartMonitoringOnStartup = startMonitoringOnStartup;
                
                // Notify that configuration has changed
                ConfigurationChanged?.Invoke(this, EventArgs.Empty);
                System.Diagnostics.Debug.WriteLine($"ConfigurationService.SaveConfigurationDirectAsync() - COMPLETE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in ConfigurationService.SaveConfigurationDirectAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                throw; // Rethrow to propagate
            }
        }
    }

    
    // Data structure for storing configuration in JSON
    
    public class ConfigurationData
    {
        public List<ApplicationWindow> TargetApplications { get; set; } = new();
        public bool StartMonitoringOnStartup { get; set; } = true;
    }
} 