#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MonitorBounds.Models;
using MonitorBounds.Services;
using MonitorBounds.Views;
using System.IO;

namespace MonitorBounds.ViewModels
{
    // ViewModel for the main application window
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly WindowMonitorService _windowMonitorService;
        private readonly ConfigurationService _configurationService;
        private readonly StartupManager _startupManager;
        private bool _isMonitoring;
        private bool _runAtWindowsStartup;
        private ApplicationWindow? _selectedApplication;

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
        public ICommand ToggleStartupCommand { get; }

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
        
        // Gets or sets whether the application should run when Windows starts
        public bool RunAtWindowsStartup
        {
            get => _runAtWindowsStartup;
            set
            {
                if (_runAtWindowsStartup != value)
                {
                    _runAtWindowsStartup = value;
                    
                    // Update the startup registry based on the value
                    if (_runAtWindowsStartup)
                    {
                        _startupManager.EnableStartup();
                    }
                    else
                    {
                        _startupManager.DisableStartup();
                    }
                    
                    // Save the setting to configuration
                    _configurationService.RunAtWindowsStartup = value;
                    Task.Run(async () => await SaveConfigurationAsync()).ConfigureAwait(false);
                    
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
        public MainViewModel(WindowMonitorService windowMonitorService, ConfigurationService configurationService, StartupManager startupManager)
        {
            _windowMonitorService = windowMonitorService ?? throw new ArgumentNullException(nameof(windowMonitorService));
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _startupManager = startupManager ?? throw new ArgumentNullException(nameof(startupManager));

            // Set up commands
            StartMonitoringCommand = new RelayCommand(StartMonitoring, CanStartMonitoring);
            StopMonitoringCommand = new RelayCommand(StopMonitoring, CanStopMonitoring);
            AddApplicationCommand = new AsyncRelayCommand(AddApplicationAsync);
            EditApplicationCommand = new AsyncRelayCommand(EditApplicationAsync, CanEditApplication);
            RemoveApplicationCommand = new AsyncRelayCommand(RemoveApplicationAsync, CanRemoveApplication);
            ToggleActiveStateCommand = new RelayCommand<ApplicationWindow>(ToggleActiveState);
            ToggleStartupCommand = new RelayCommand(ToggleStartup);

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
            
            await _configurationService.LoadConfigurationAsync();
            
            // Update observable collections
            TargetApplications.Clear();
            
            // Copy each application from the config service
            foreach (var app in _configurationService.TargetApplications)
            {
                // Create a fresh copy to avoid reference issues
                var appCopy = new ApplicationWindow
                {
                    RuleName = app.RuleName,
                    TitlePattern = app.TitlePattern,
                    ProcessNamePattern = app.ProcessNamePattern,
                    UseProcessNameMatching = app.UseProcessNameMatching,
                    IsActive = app.IsActive,
                    RestrictToMonitor = app.RestrictToMonitor
                };
                
                TargetApplications.Add(appCopy);
                
                // Add to monitoring service if active
                if (appCopy.IsActive)
                {
                    _windowMonitorService.AddTargetApplication(appCopy);
                }
                
            }
            
            // Set the startup setting from configuration
            _runAtWindowsStartup = _configurationService.RunAtWindowsStartup;
            
            // Ensure the actual startup registry entry matches the configuration
            if (_runAtWindowsStartup != _startupManager.IsStartupEnabled())
            {
                if (_runAtWindowsStartup)
                {
                    _startupManager.EnableStartup();
                }
                else
                {
                    _startupManager.DisableStartup();
                }
            }
            
            // Start monitoring if configured to do so
            if (_configurationService.StartMonitoringOnStartup)
            {
                StartMonitoring();
            }
            
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
                    }
                }
                
                // Start the monitoring service
                _windowMonitorService.StartMonitoring();
                
                // Update IsMonitoring property
                IsMonitoring = true;
                
                // Update configuration to start on startup
                _configurationService.StartMonitoringOnStartup = true;
                
                
                // Save configuration
                Task.Run(async () => await SaveConfigurationAsync()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                
                // Reset state
                IsMonitoring = false;
                
                // Rethrow to allow UI to show error
                throw;
            }
        }

        private bool CanStartMonitoring()
        {
            return !IsMonitoring;
        }

        // Stop monitoring window movements
        private void StopMonitoring()
        {
            try
            {
                // Stop the monitoring service
                _windowMonitorService.StopMonitoring();
                
                // Update IsMonitoring property
                IsMonitoring = false;
                
                // Update configuration
                _configurationService.StartMonitoringOnStartup = false;
                
                
                // Save configuration
                Task.Run(async () => await SaveConfigurationAsync()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                
                // Rethrow to allow UI to show error
                throw;
            }
        }

        private bool CanStopMonitoring()
        {
            return IsMonitoring;
        }

        // Add a new target application
        private async Task AddApplicationAsync()
        {
            try
            {
                
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
                    
                    // Create fresh copy to avoid reference issues
                    var freshApp = new ApplicationWindow
                    {
                        RuleName = editor.Application.RuleName,
                        TitlePattern = editor.Application.TitlePattern,
                        ProcessNamePattern = editor.Application.ProcessNamePattern,
                        UseProcessNameMatching = editor.Application.UseProcessNameMatching,
                        IsActive = editor.Application.IsActive,
                        RestrictToMonitor = editor.Application.RestrictToMonitor,
                        Handle = editor.Application.Handle
                    };
                    
                    // Add to collections
                    TargetApplications.Add(freshApp);
                    
                    // Add to monitoring service if needed
                    if (IsMonitoring && freshApp.IsActive)
                    {
                        _windowMonitorService.AddTargetApplication(freshApp);
                    }
                    
                    // Select the new item
                    SelectedApplication = freshApp;
                    
                    // Save the configuration
                    await SaveConfigurationAsync();
                    
                    // Force UI refresh
                    OnPropertyChanged(nameof(TargetApplications));
                    CommandManager.InvalidateRequerySuggested();
                    
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                
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
            if (SelectedApplication == null) 
                return;
                
            await EditApplicationAsync(SelectedApplication);
        }

        private async Task<bool> EditApplicationAsync(ApplicationWindow appToEdit)
        {
            try
            {
                // Save selected application title pattern for comparison later
                string originalTitlePattern = appToEdit.TitlePattern;
                bool wasSelected = object.ReferenceEquals(SelectedApplication, appToEdit);
                
                foreach (var app in TargetApplications)
                {
                }
                
                // Create a deep copy for editing to avoid reference issues
                var applicationCopy = new ApplicationWindow
                {
                    RuleName = appToEdit.RuleName,
                    TitlePattern = appToEdit.TitlePattern,
                    ProcessNamePattern = appToEdit.ProcessNamePattern,
                    UseProcessNameMatching = appToEdit.UseProcessNameMatching,
                    IsActive = appToEdit.IsActive,
                    RestrictToMonitor = appToEdit.RestrictToMonitor,
                    Handle = appToEdit.Handle
                };
                
                
                // Show the editor dialog with the copy
                var dialog = new ApplicationEditorWindow(applicationCopy);
                bool? dialogResult = dialog.ShowDialog();
                
                // If user confirmed changes
                if (dialogResult == true && dialog.Application != null)
                {
                    
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
                    
                    
                    if (indexToReplace < 0)
                    {
                        return false;
                    }
                    
                    // Create new instance with updated values
                    var updatedApp = new ApplicationWindow
                    {
                        RuleName = dialog.Application.RuleName,
                        TitlePattern = dialog.Application.TitlePattern,
                        ProcessNamePattern = dialog.Application.ProcessNamePattern,
                        UseProcessNameMatching = dialog.Application.UseProcessNameMatching,
                        IsActive = dialog.Application.IsActive,
                        RestrictToMonitor = dialog.Application.RestrictToMonitor,
                        Handle = dialog.Application.Handle
                    };
                    
                    
                    // Replace at the found index
                    TargetApplications.RemoveAt(indexToReplace);
                    TargetApplications.Insert(indexToReplace, updatedApp);
                    
                    // Update SelectedApplication if it was the one being edited
                    if (wasSelected)
                    {
                        SelectedApplication = updatedApp;
                    }
                    
                    // Update the monitoring service if needed
                    if (IsMonitoring)
                    {
                        // Remove old entry first
                        try
                        {
                            _windowMonitorService.RemoveTargetApplication(appToEdit);
                        }
                        catch (Exception)
                        {
                        }
                        
                        // Register with new settings if it should be active
                        if (updatedApp.IsActive)
                        {
                            _windowMonitorService.AddTargetApplication(updatedApp);
                        }
                    }
                    
                    // Save the updated configuration
                    await SaveConfigurationAsync();
                    
                    // Force UI refresh
                    OnPropertyChanged(nameof(TargetApplications));
                    CommandManager.InvalidateRequerySuggested();
                    
                    
                    foreach (var app in TargetApplications)
                    {
                    }
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                
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
            return SelectedApplication != null;
        }

        // Remove the selected application
        private async Task RemoveApplicationAsync()
        {
            if (SelectedApplication == null) 
                return;
                
            try
            {
                // Confirm deletion
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove '{SelectedApplication.RuleName}'?", 
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
                    $"Failed to remove rule: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private bool CanRemoveApplication()
        {
            return SelectedApplication != null;
        }

        // Toggle the active state of an application
        public void ToggleActiveState(ApplicationWindow app)
        {
            if (app == null)
                return;

            try
            {
                // Toggle application active state
                app.IsActive = !app.IsActive;
                
                // If active, add to monitor service; if inactive, remove from monitor service
                if (app.IsActive)
                {
                    _windowMonitorService.AddTargetApplication(app);
                }
                else
                {
                    _windowMonitorService.RemoveTargetApplication(app);
                }
                
                // Save the configuration change immediately
                _ = Task.Run(async () => 
                {
                    await SaveConfigurationAsync();
                });
                
                // Update command availability
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update application state: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Toggle the startup state of the application
        private void ToggleStartup()
        {
            RunAtWindowsStartup = !RunAtWindowsStartup;
        }

        // Saves the current configuration to the service
        public async Task SaveConfigurationAsync()
        {
            try
            {
                
                foreach (var app in TargetApplications)
                {
                }
                
                // Use the direct save method to ensure all applications are saved correctly
                await _configurationService.SaveConfigurationDirectAsync(
                    TargetApplications, 
                    _isMonitoring, // Save current monitoring state as StartMonitoringOnStartup
                    _runAtWindowsStartup // Save current startup setting
                );
                
                
                // Verify that settings were saved correctly
                string configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    long fileSize = new FileInfo(configPath).Length;
                    
                    if (fileSize <= 10)
                    {
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                
                // Show error to user
                MessageBox.Show(
                    $"Failed to save configuration: {ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
                Window? window = null;
                
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
