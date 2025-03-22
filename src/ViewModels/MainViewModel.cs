#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ScreenRegionProtector.Models;
using ScreenRegionProtector.Services;
using System.Linq;
using ScreenRegionProtector.Views;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;

namespace ScreenRegionProtector.ViewModels
{
    // ViewModel for the main application window
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly WindowMonitorService _windowMonitorService;
        private readonly ConfigurationService _configurationService;
        private bool _isMonitoring;
        private ApplicationWindow? _selectedApplication;
        private bool _isDisposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        // Add public property to expose WindowMonitorService
        public WindowMonitorService WindowMonitorService => _windowMonitorService;

        // Observable collections for binding
        public ObservableCollection<ApplicationWindow> TargetApplications { get; } = new();

        // Commands
        public ICommand StartMonitoringCommand { get; }
        public ICommand StopMonitoringCommand { get; }
        public ICommand AddApplicationCommand { get; }
        public ICommand EditApplicationCommand { get; }
        public ICommand RemoveApplicationCommand { get; }
        public ICommand ToggleActiveStateCommand { get; }

        // Gets or sets whether monitoring is active
        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                if (_isMonitoring != value)
                {
                    _isMonitoring = value;
                    OnPropertyChanged();
                }
            }
        }

        // Gets or sets the selected application in the UI
        public ApplicationWindow? SelectedApplication
        {
            get => _selectedApplication;
            set
            {
                if (_selectedApplication != value)
                {
                    _selectedApplication = value;
                    OnPropertyChanged();

                    // Update command availability
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        // Constructor initializes services and commands
        public MainViewModel(WindowMonitorService windowMonitorService, ConfigurationService configurationService)
        {
            _windowMonitorService = windowMonitorService ?? throw new ArgumentNullException(nameof(windowMonitorService));
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));

            // Set up commands
            StartMonitoringCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
            StopMonitoringCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);
            AddApplicationCommand = new AsyncRelayCommand(AddApplicationAsync);
            EditApplicationCommand = new AsyncRelayCommand(EditApplicationAsync, CanEditApplication);
            RemoveApplicationCommand = new AsyncRelayCommand(RemoveApplicationAsync, CanRemoveApplication);
            ToggleActiveStateCommand = new RelayCommand<ApplicationWindow>(ToggleActiveState);

            // Subscribe to window monitoring events
            _windowMonitorService.WindowMoved += OnWindowMoved;
            _windowMonitorService.WindowRepositioned += OnWindowRepositioned;

            // Listen for configuration changes
            _configurationService.ConfigurationChanged += OnConfigurationChanged;

            // Removed auto initialization to prevent double loading the configuration
            // _ = LoadConfigurationAsync();
        }

        // Loads the configuration from the service
        public async Task LoadConfigurationAsync()
        {
            System.Diagnostics.Debug.WriteLine("===== LoadConfigurationAsync - STARTING =====");
            
            await _configurationService.LoadConfigurationAsync();
            System.Diagnostics.Debug.WriteLine($"Loaded configuration with {_configurationService.TargetApplications.Count} applications");
            
            // Update observable collections
            TargetApplications.Clear();
            System.Diagnostics.Debug.WriteLine("Cleared TargetApplications collection");
            
            // Copy each application from the config service
            foreach (var app in _configurationService.TargetApplications)
            {
                // Create a fresh copy to avoid reference issues
                var appCopy = new ApplicationWindow
                {
                    TitlePattern = app.TitlePattern,
                    IsActive = app.IsActive,
                    RestrictToMonitor = app.RestrictToMonitor
                };
                
                TargetApplications.Add(appCopy);
                
                // Add to monitoring service if active
                if (appCopy.IsActive)
                {
                    _windowMonitorService.AddTargetApplication(appCopy);
                }
                
                System.Diagnostics.Debug.WriteLine($"Added application: '{appCopy.TitlePattern}', Active={appCopy.IsActive}, Monitor={appCopy.RestrictToMonitor}");
            }
            
            // Start monitoring if configured to do so
            if (_configurationService.StartMonitoringOnStartup)
            {
                System.Diagnostics.Debug.WriteLine("Starting monitoring based on configuration");
                StartMonitoring();
            }
            
            System.Diagnostics.Debug.WriteLine("===== LoadConfigurationAsync - COMPLETE =====");
        }

        private void OnConfigurationChanged(object? sender, EventArgs e)
        {
            // Configuration has changed, update UI if needed
        }

        private void OnWindowMoved(object? sender, WindowMovedEventArgs e)
        {
            // A window has moved
        }

        private void OnWindowRepositioned(object? sender, WindowRepositionedEventArgs e)
        {
            // A window was repositioned because it entered a protected region
        }

        // Start monitoring window movements
        private void StartMonitoring()
        {
            if (_isDisposed) 
            {
                System.Diagnostics.Debug.WriteLine("Cannot start monitoring - ViewModel is disposed");
                return;
            }

            System.Diagnostics.Debug.WriteLine("StartMonitoring called");
            
            try
            {
                // Explicitly ensure all target applications are properly registered
                foreach (var app in TargetApplications)
                {
                    if (app.IsActive && app.RestrictToMonitor.HasValue)
                    {
                        // Always re-add to ensure it's registered correctly
                        _windowMonitorService.RemoveTargetApplication(app);
                        _windowMonitorService.AddTargetApplication(app);
                        System.Diagnostics.Debug.WriteLine($"Registered app for monitoring: {app.TitlePattern} on monitor {app.RestrictToMonitor}");
                    }
                }
                
                // Start the monitoring service
                _windowMonitorService.StartMonitoring();
                
                // Update IsMonitoring property
                IsMonitoring = true;
                
                // Update configuration to start on startup
                _configurationService.StartMonitoringOnStartup = true;
                
                System.Diagnostics.Debug.WriteLine($"Monitoring started successfully. IsMonitoring = {IsMonitoring}");
                
                // Save configuration
                Task.Run(async () => await SaveConfigurationAsync()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR starting monitoring: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                
                // Reset state
                IsMonitoring = false;
                
                // Rethrow to allow UI to show error
                throw;
            }
        }

        private bool CanStartMonitoring()
        {
            bool result = !_isDisposed && !IsMonitoring;
            System.Diagnostics.Debug.WriteLine($"CanStartMonitoring() = {result} (_isDisposed={_isDisposed}, IsMonitoring={IsMonitoring})");
            return result;
        }

        // Stop monitoring window movements
        private void StopMonitoring()
        {
            if (_isDisposed)
            {
                System.Diagnostics.Debug.WriteLine("Cannot stop monitoring - ViewModel is disposed");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("StopMonitoring called");
            
            try
            {
                // Stop the monitoring service
                _windowMonitorService.StopMonitoring();
                
                // Update IsMonitoring property
                IsMonitoring = false;
                
                // Update configuration
                _configurationService.StartMonitoringOnStartup = false;
                
                System.Diagnostics.Debug.WriteLine($"Monitoring stopped successfully. IsMonitoring = {IsMonitoring}");
                
                // Save configuration
                Task.Run(async () => await SaveConfigurationAsync()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR stopping monitoring: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                
                // Rethrow to allow UI to show error
                throw;
            }
        }

        private bool CanStopMonitoring()
        {
            bool result = !_isDisposed && IsMonitoring;
            System.Diagnostics.Debug.WriteLine($"CanStopMonitoring() = {result} (_isDisposed={_isDisposed}, IsMonitoring={IsMonitoring})");
            return result;
        }

        // Add a new target application
        private async Task AddApplicationAsync()
        {
            if (_isDisposed) return;
            
            try
            {
                System.Diagnostics.Debug.WriteLine("===== AddApplicationAsync - STARTING =====");
                
                // Create a new application
                var newApp = new ApplicationWindow
                {
                    TitlePattern = "*",
                    IsActive = true,
                    // Default to first monitor if available
                    RestrictToMonitor = 0
                };

                // Show the editor
                var editor = new ApplicationEditorWindow();
                bool? result = editor.ShowDialog();

                if (result == true && editor.Application != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Dialog confirmed. New application: Title='{editor.Application.TitlePattern}', Active={editor.Application.IsActive}, Monitor={editor.Application.RestrictToMonitor}");
                    
                    // Create fresh copy to avoid reference issues
                    var freshApp = new ApplicationWindow
                    {
                        TitlePattern = editor.Application.TitlePattern,
                        IsActive = editor.Application.IsActive,
                        RestrictToMonitor = editor.Application.RestrictToMonitor,
                        Handle = editor.Application.Handle
                    };
                    
                    // Add to collections
                    TargetApplications.Add(freshApp);
                    System.Diagnostics.Debug.WriteLine($"Added to TargetApplications collection. New count: {TargetApplications.Count}");
                    
                    // Add to monitoring service if needed
                    if (IsMonitoring && freshApp.IsActive)
                    {
                        _windowMonitorService.AddTargetApplication(freshApp);
                        System.Diagnostics.Debug.WriteLine($"Added to WindowMonitorService: '{freshApp.TitlePattern}'");
                    }
                    
                    // Select the new item
                    SelectedApplication = freshApp;
                    
                    // Save the configuration
                    System.Diagnostics.Debug.WriteLine("Saving configuration...");
                    await SaveConfigurationAsync();
                    
                    // Force UI refresh
                    OnPropertyChanged(nameof(TargetApplications));
                    CommandManager.InvalidateRequerySuggested();
                    
                    System.Diagnostics.Debug.WriteLine("===== AddApplicationAsync - COMPLETE (added) =====");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("===== AddApplicationAsync - COMPLETE (canceled) =====");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in AddApplicationAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                
                System.Windows.MessageBox.Show(
                    $"Error adding application: {ex.Message}",
                    "Add Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        // Edit the selected application
        private async Task EditApplicationAsync()
        {
            if (_isDisposed || SelectedApplication == null) 
                return;
                
            System.Diagnostics.Debug.WriteLine($"EditApplicationAsync called for selected application: {SelectedApplication.TitlePattern}");
            await EditApplicationAsync(SelectedApplication);
        }

        private async Task<bool> EditApplicationAsync(ApplicationWindow appToEdit)
        {
            try
            {
                if (_isDisposed) return false;
                
                System.Diagnostics.Debug.WriteLine($"===== EditApplicationAsync - STARTING =====");
                System.Diagnostics.Debug.WriteLine($"Original app: Title='{appToEdit.TitlePattern}', Active={appToEdit.IsActive}, Monitor={appToEdit.RestrictToMonitor}, HashCode={appToEdit.GetHashCode()}");
                System.Diagnostics.Debug.WriteLine($"Target applications count before edit: {TargetApplications.Count}");
                
                // Save selected application title pattern for comparison later
                string originalTitlePattern = appToEdit.TitlePattern;
                bool wasSelected = object.ReferenceEquals(SelectedApplication, appToEdit);
                System.Diagnostics.Debug.WriteLine($"Was selected: {wasSelected}");
                
                foreach (var app in TargetApplications)
                {
                    System.Diagnostics.Debug.WriteLine($"Collection item: Title='{app.TitlePattern}', Active={app.IsActive}, Monitor={app.RestrictToMonitor}, HashCode={app.GetHashCode()}, Equals={object.ReferenceEquals(app, appToEdit)}");
                }
                
                // Create a deep copy for editing to avoid reference issues
                var applicationCopy = new ApplicationWindow
                {
                    TitlePattern = appToEdit.TitlePattern,
                    IsActive = appToEdit.IsActive,
                    RestrictToMonitor = appToEdit.RestrictToMonitor,
                    Handle = appToEdit.Handle
                };
                
                System.Diagnostics.Debug.WriteLine($"Created copy: Title='{applicationCopy.TitlePattern}', HashCode={applicationCopy.GetHashCode()}");
                
                // Show the editor dialog with the copy
                var dialog = new ApplicationEditorWindow(applicationCopy);
                bool? dialogResult = dialog.ShowDialog();
                
                // If user confirmed changes
                if (dialogResult == true && dialog.Application != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Dialog confirmed. New values: Title='{dialog.Application.TitlePattern}', Active={dialog.Application.IsActive}, Monitor={dialog.Application.RestrictToMonitor}, HashCode={dialog.Application.GetHashCode()}");
                    
                    // Find the application in the collection - use index for direct replacement
                    int indexToReplace = -1;
                    
                    // First try to find by reference (most accurate)
                    for (int i = 0; i < TargetApplications.Count; i++)
                    {
                        if (object.ReferenceEquals(TargetApplications[i], appToEdit))
                        {
                            indexToReplace = i;
                            break;
                        }
                    }
                    
                    // If reference search failed, try title pattern as fallback
                    if (indexToReplace < 0)
                    {
                        for (int i = 0; i < TargetApplications.Count; i++)
                        {
                            if (TargetApplications[i].TitlePattern == originalTitlePattern)
                            {
                                indexToReplace = i;
                                break;
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Found index to replace: {indexToReplace}");
                    
                    if (indexToReplace < 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: Could not find original app in collection");
                        return false;
                    }
                    
                    // Create new instance with updated values
                    var updatedApp = new ApplicationWindow
                    {
                        TitlePattern = dialog.Application.TitlePattern,
                        IsActive = dialog.Application.IsActive,
                        RestrictToMonitor = dialog.Application.RestrictToMonitor,
                        Handle = dialog.Application.Handle
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"Created updated app: Title='{updatedApp.TitlePattern}', HashCode={updatedApp.GetHashCode()}");
                    
                    // Replace at the found index
                    TargetApplications.RemoveAt(indexToReplace);
                    TargetApplications.Insert(indexToReplace, updatedApp);
                    System.Diagnostics.Debug.WriteLine($"Replaced application at index {indexToReplace}");
                    
                    // Update SelectedApplication if it was the one being edited
                    if (wasSelected)
                    {
                        System.Diagnostics.Debug.WriteLine("Updating SelectedApplication reference to the new instance");
                        SelectedApplication = updatedApp;
                    }
                    
                    // Update the monitoring service if needed
                    if (IsMonitoring)
                    {
                        // Remove old entry first
                        try
                        {
                            _windowMonitorService.RemoveTargetApplication(appToEdit);
                            System.Diagnostics.Debug.WriteLine($"Unregistered old window: '{appToEdit.TitlePattern}'");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to unregister window: {ex.Message}");
                        }
                        
                        // Register with new settings if it should be active
                        if (updatedApp.IsActive)
                        {
                            _windowMonitorService.AddTargetApplication(updatedApp);
                            System.Diagnostics.Debug.WriteLine($"Registered new window: '{updatedApp.TitlePattern}', Monitor={updatedApp.RestrictToMonitor}");
                        }
                    }
                    
                    // Save the updated configuration
                    System.Diagnostics.Debug.WriteLine("About to save configuration after edit");
                    await SaveConfigurationAsync();
                    
                    // Force UI refresh
                    OnPropertyChanged(nameof(TargetApplications));
                    CommandManager.InvalidateRequerySuggested();
                    
                    System.Diagnostics.Debug.WriteLine("===== EditApplicationAsync - COMPLETE (saved) =====");
                    
                    System.Diagnostics.Debug.WriteLine($"Target applications count after edit: {TargetApplications.Count}");
                    foreach (var app in TargetApplications)
                    {
                        System.Diagnostics.Debug.WriteLine($"Final collection item: Title='{app.TitlePattern}', Active={app.IsActive}, Monitor={app.RestrictToMonitor}, HashCode={app.GetHashCode()}");
                    }
                    
                    return true;
                }
                
                System.Diagnostics.Debug.WriteLine("===== EditApplicationAsync - COMPLETE (canceled) =====");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in EditApplicationAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                
                // Show error to user
                System.Windows.MessageBox.Show(
                    $"Error editing application: {ex.Message}",
                    "Edit Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                
                return false;
            }
        }

        private bool CanEditApplication()
        {
            return !_isDisposed && SelectedApplication != null;
        }

        // Remove the selected application
        private async Task RemoveApplicationAsync()
        {
            if (_isDisposed || SelectedApplication == null) 
                return;
                
            try
            {
                // Confirm deletion
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove '{SelectedApplication.TitlePattern}'?", 
                    "Confirm Removal", 
                    System.Windows.MessageBoxButton.YesNo, 
                    System.Windows.MessageBoxImage.Question);
                    
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // Remove from collections
                    _windowMonitorService.RemoveTargetApplication(SelectedApplication);
                    TargetApplications.Remove(SelectedApplication);
                    
                    // Clear selection
                    SelectedApplication = null;
                    
                    // Save the configuration
                    await SaveConfigurationAsync();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to remove application: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private bool CanRemoveApplication()
        {
            return !_isDisposed && SelectedApplication != null;
        }

        // Toggle the active state of an application
        public void ToggleActiveState(ApplicationWindow app)
        {
            if (app == null || _isDisposed)
                return;

            System.Diagnostics.Debug.WriteLine($"Toggling active state for '{app.TitlePattern}' from {app.IsActive} to {!app.IsActive}");
            
            try
            {
                // Toggle application active state
                app.IsActive = !app.IsActive;
                
                // If active, add to monitor service; if inactive, remove from monitor service
                if (app.IsActive)
                {
                    _windowMonitorService.AddTargetApplication(app);
                    System.Diagnostics.Debug.WriteLine($"Added app to window monitor: '{app.TitlePattern}'");
                }
                else
                {
                    _windowMonitorService.RemoveTargetApplication(app);
                    System.Diagnostics.Debug.WriteLine($"Removed app from window monitor: '{app.TitlePattern}'");
                }
                
                // Save the configuration change immediately
                _ = Task.Run(async () => 
                {
                    await SaveConfigurationAsync();
                    System.Diagnostics.Debug.WriteLine($"Saved configuration after toggling active state");
                });
                
                // Update command availability
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling active state: {ex.Message}");
                MessageBox.Show(
                    $"Failed to update application state: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Saves the current configuration to the service
        public async Task SaveConfigurationAsync()
        {
            if (_isDisposed)
            {
                System.Diagnostics.Debug.WriteLine("Cannot save configuration - ViewModel is disposed");
                return;
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine("SaveConfigurationAsync - STARTING");
                System.Diagnostics.Debug.WriteLine($"Saving {TargetApplications.Count} applications");
                
                foreach (var app in TargetApplications)
                {
                    System.Diagnostics.Debug.WriteLine($"App to save: '{app.TitlePattern}', Active={app.IsActive}, Monitor={app.RestrictToMonitor}");
                }
                
                // Use the direct save method to ensure all applications are saved correctly
                await _configurationService.SaveConfigurationDirectAsync(
                    TargetApplications, 
                    _isMonitoring // Save current monitoring state as StartMonitoringOnStartup
                );
                
                System.Diagnostics.Debug.WriteLine($"Configuration saved successfully with StartMonitoringOnStartup={_isMonitoring}");
                
                // Verify that settings were saved correctly
                string configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    long fileSize = new FileInfo(configPath).Length;
                    System.Diagnostics.Debug.WriteLine($"Verified config file exists: {configPath}, Size: {fileSize} bytes");
                    
                    if (fileSize <= 10)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: Config file exists but appears to be empty or very small");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: Config file does not exist after save: {configPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR saving configuration: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                
                // Show error to user
                MessageBox.Show(
                    $"Failed to save configuration: {ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Clean up resources and unsubscribe from events
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;
                
            if (disposing)
            {
                // Unsubscribe from events to prevent memory leaks
                _windowMonitorService.WindowMoved -= OnWindowMoved;
                _windowMonitorService.WindowRepositioned -= OnWindowRepositioned;
                _configurationService.ConfigurationChanged -= OnConfigurationChanged;
                
                // Stop monitoring
                if (IsMonitoring)
                {
                    StopMonitoring();
                }
                
                // We should NOT clear the TargetApplications collection here
                // as it will cause settings to be lost when the window is hidden to tray
                // TargetApplications.Clear();
            }
            
            _isDisposed = true;
        }

        // Helper method to raise PropertyChanged events
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Helper method to show a dialog window asynchronously
        private async Task<bool?> ShowDialogAsync(Func<Window> windowCreator)
        {
            return await Task.Run(() =>
            {
                Window window = null;
                
                // Create and show the window on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    window = windowCreator();
                    if (window != null)
                    {
                        window.Owner = System.Windows.Application.Current.MainWindow;
                    }
                });
                
                // Show the dialog on the UI thread and return the result
                bool? result = null;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (window != null)
                    {
                        result = window.ShowDialog();
                    }
                });
                
                return result;
            });
        }

        // Helper to get config file path
        private string GetConfigFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "WindowBoundaryGuard");
            return Path.Combine(appFolderPath, "config.json");
        }
    }

    // Implementation of ICommand for simple delegate commands
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }
    }

    // Generic RelayCommand that takes a parameter
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T>? _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return parameter is T t ? _canExecute?.Invoke(t) ?? true : true;
        }

        public void Execute(object? parameter)
        {
            if (parameter is T t)
            {
                _execute(t);
            }
        }
    }

    // AsyncRelayCommand for async Task methods
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (_isExecuting)
                return;

            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
} 