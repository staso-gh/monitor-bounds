#nullable enable
using System;
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
using System.Collections.Generic;

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

            // Initialize data
            _ = LoadConfigurationAsync();
        }

        // Loads the configuration from the service
        public async Task LoadConfigurationAsync()
        {
            await _configurationService.LoadConfigurationAsync();
            
            // Update observable collections
            TargetApplications.Clear();
            foreach (var app in _configurationService.TargetApplications)
            {
                TargetApplications.Add(app);
                _windowMonitorService.AddTargetApplication(app);
            }
            
            // Start monitoring if configured to do so
            if (_configurationService.StartMonitoringOnStartup)
            {
                StartMonitoring();
            }

            // Fix ConfigurationService TargetApplications issue
            _configurationService.TargetApplications.Clear();
            foreach (var app in TargetApplications)
            {
                _configurationService.TargetApplications.Add(app);
            }
            _configurationService.StartMonitoringOnStartup = IsMonitoring;
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
            if (_isDisposed) return;
            _windowMonitorService.StartMonitoring();
            IsMonitoring = true;
        }

        private bool CanStartMonitoring()
        {
            return !_isDisposed && !IsMonitoring;
        }

        // Stop monitoring window movements
        private void StopMonitoring()
        {
            if (_isDisposed) return;
            _windowMonitorService.StopMonitoring();
            IsMonitoring = false;
        }

        private bool CanStopMonitoring()
        {
            return !_isDisposed && IsMonitoring;
        }

        // Add a new target application
        private async Task AddApplicationAsync()
        {
            if (_isDisposed) return;
            
            try
            {
                // Create a new application
                var newApp = new ApplicationWindow
                {
                    TitlePattern = "*",
                    IsActive = true
                };

                // Show the editor
                var editor = new ApplicationEditorWindow(newApp);
                editor.Owner = System.Windows.Application.Current.MainWindow;
                editor.ShowDialog();

                if (editor.DialogResult)
                {
                    // Add to collections
                    TargetApplications.Add(newApp);
                    _windowMonitorService.AddTargetApplication(newApp);
                    
                    // Select the new item
                    SelectedApplication = newApp;
                    
                    // Save the configuration
                    await SaveConfigurationAsync();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to add application: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        // Edit the selected application
        private async Task EditApplicationAsync()
        {
            if (_isDisposed || SelectedApplication == null) 
                return;
            
            try
            {
                // Instead of cloning, create a new instance with the same properties
                var appClone = new ApplicationWindow
                {
                    TitlePattern = SelectedApplication.TitlePattern,
                    IsActive = SelectedApplication.IsActive,
                    RestrictToMonitor = SelectedApplication.RestrictToMonitor
                };
                
                // Show the editor
                var editor = new ApplicationEditorWindow(appClone);
                editor.Owner = System.Windows.Application.Current.MainWindow;
                editor.ShowDialog();

                if (editor.DialogResult)
                {
                    // Instead of CopyFrom, manually update the properties
                    SelectedApplication.TitlePattern = appClone.TitlePattern;
                    SelectedApplication.IsActive = appClone.IsActive;
                    SelectedApplication.RestrictToMonitor = appClone.RestrictToMonitor;
                    
                    // Save the configuration
                    await SaveConfigurationAsync();
                    
                    // Force refresh the UI
                    OnPropertyChanged(nameof(TargetApplications));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to edit application: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
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
            if (_isDisposed || app == null) 
                return;
                
            try
            {
                // Toggle the active state
                app.IsActive = !app.IsActive;
                
                // Save the configuration
                _ = SaveConfigurationAsync();
                
                // Notify UI about the change
                OnPropertyChanged(nameof(TargetApplications));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to toggle active state: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        // Saves the current configuration
        private async Task SaveConfigurationAsync()
        {
            if (_isDisposed) return;
            
            try
            {
                _configurationService.TargetApplications.Clear();
                foreach (var app in TargetApplications)
                {
                    _configurationService.TargetApplications.Add(app);
                }
                _configurationService.StartMonitoringOnStartup = IsMonitoring;
                
                await _configurationService.SaveConfigurationAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to save configuration: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
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
                
                // Clear collections
                TargetApplications.Clear();
            }
            
            _isDisposed = true;
        }

        // Helper method to raise PropertyChanged events
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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