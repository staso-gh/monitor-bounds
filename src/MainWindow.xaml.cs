using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ScreenRegionProtector.Services;
using ScreenRegionProtector.ViewModels;
using ScreenRegionProtector.Converters;
using System.Windows.Threading;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using ScreenRegionProtector.Models;

namespace ScreenRegionProtector;


// Interaction logic for MainWindow.xaml

public partial class MainWindow : Window, IDisposable
{
    private readonly WindowMonitorService _windowMonitorService;
    private readonly ConfigurationService _configurationService;
    private MainViewModel _viewModel;
    private ThemeManager _themeManager;
    private bool _isDarkTheme;
    private bool _isDisposed;
    
    // Store event handlers as fields so they can be unsubscribed
    private EventHandler<bool> _themeChangedHandler;
    private MouseButtonEventHandler _mouseLeftButtonDownHandler;
    private RoutedEventHandler _loadedHandler;
    private RoutedEventHandler _gridLoadedHandler;
    private MouseButtonEventHandler _dataGridPreviewMouseDownHandler;
    private System.Windows.Input.KeyEventHandler _dataGridPreviewKeyDownHandler;
    private ContextMenuEventHandler _dataGridContextMenuOpeningHandler;

    // Light theme colors
    private readonly Color _lightBackgroundColor = Color.FromRgb(248, 248, 248);
    private readonly Color _lightForegroundColor = Color.FromRgb(30, 30, 30);
    private readonly Color _lightDataGridBackgroundColor = Colors.White;
    
    // Dark theme colors
    private readonly Color _darkBackgroundColor = Color.FromRgb(45, 45, 48);
    // Using pure white (255,255,255) for dark mode text to ensure maximum contrast
    private readonly Color _darkForegroundColor = Colors.White;
    private readonly Color _darkDataGridBackgroundColor = Color.FromRgb(60, 60, 62);

    // Add private fields to store original styles
    private Style _originalLightRowStyle;
    private Style _originalDarkRowStyle;

    public MainWindow()
    {
        // Register converters in resources first
        Resources.Add("BoolToStringConverter", new BoolToStringConverter());
        Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        Resources.Add("BoolToColorConverter", new BoolToColorConverter());
        Resources.Add("ToggleButtonCommandConverter", new ToggleButtonCommandConverter());

        // Initialize component next
        InitializeComponent();

        // Set the window icon based on current theme (app-level variable)
        var app = System.Windows.Application.Current as App;
        if (app != null)
        {
            // The app class will handle icon updates, but we need to make sure 
            // the window has the right one when first created
            _themeManager = app.ThemeManager;
            _isDarkTheme = _themeManager?.IsDarkTheme ?? false;
        }

        // Initialize services before creating ViewModel
        _configurationService = new ConfigurationService();
        _windowMonitorService = new WindowMonitorService();
        
        // Create and initialize ViewModel
        _viewModel = new MainViewModel(_windowMonitorService, _configurationService);
        
        // Track monitoring state changes
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Set the DataContext to the ViewModel
        DataContext = _viewModel;

        // Load configuration
        LoadConfiguration();

        if (System.Windows.Application.Current is App appInstance)
        {
            // Get access to the theme manager
            _themeManager = appInstance.ThemeManager;
            if (_themeManager != null)
            {
                // Get initial theme state
                _isDarkTheme = _themeManager.IsDarkTheme;
            
                // Force all theme-related resources to load first
                EnsureThemeResourcesAreLoaded();

                // Apply the theme immediately
                ApplyTheme(_isDarkTheme);
                
                // Schedule another theme application after layout is complete to ensure it's applied correctly
                _loadedHandler = (s, e) => {
                    ApplyTheme(_themeManager.IsDarkTheme);
                    
                    // Also ensure monitoring state is correctly applied after load
                    if (_viewModel.IsMonitoring)
                    {
                        // Delay to ensure UI is fully loaded first
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                            UpdateMonitoringToggleButton();
                        }));
                    }
                };
                this.Loaded += _loadedHandler;
                
                // Subscribe to theme changes
                _themeChangedHandler = ThemeManager_ThemeChanged;
                _themeManager.ThemeChanged += _themeChangedHandler;
            }
        }
        
        // Add mouse event handlers for window dragging
        _mouseLeftButtonDownHandler = (s, e) => 
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        };
        this.MouseLeftButtonDown += _mouseLeftButtonDownHandler;

        // Set up DataGrid event handlers for checkbox toggle and double-click edit
        SetupDataGridEvents();
    }
    
    private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.IsMonitoring))
        {
            // Handle monitoring state changes if needed
        }
    }
    
    private void ThemeManager_ThemeChanged(object sender, bool isDarkTheme)
    {
        // Apply the theme
        ApplyTheme(isDarkTheme);
    }
    
    private void SetupDataGridEvents()
    {
        // Find the DataGrid once instead of repeatedly
        var dataGrid = FindVisualChild<System.Windows.Controls.DataGrid>(this);
        if (dataGrid != null)
        {
            // Use fields for event handlers so they can be unsubscribed during disposal
            _dataGridPreviewMouseDownHandler = (s, e) => DataGrid_PreviewMouseDown(dataGrid, e);
            _dataGridPreviewKeyDownHandler = (s, e) => DataGrid_PreviewKeyDown(dataGrid, e);
            _dataGridContextMenuOpeningHandler = (s, e) => DataGrid_ContextMenuOpening(dataGrid, s, e);

            dataGrid.PreviewMouseDown += _dataGridPreviewMouseDownHandler;
            dataGrid.PreviewKeyDown += _dataGridPreviewKeyDownHandler;
            dataGrid.ContextMenuOpening += _dataGridContextMenuOpeningHandler;

            // Grid loaded event to handle initial selections
            _gridLoadedHandler = (s, e) => {
                if (dataGrid.Items.Count > 0)
                {
                    dataGrid.SelectedIndex = 0;
                }
            };
            
            dataGrid.Loaded += _gridLoadedHandler;
        }
    }

    // Helper method to find a child element in the visual tree
    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }
            
            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }
    
    // Method to apply the theme to the window
    public void ApplyTheme(bool isDarkTheme)
    {
        _isDarkTheme = isDarkTheme;

        // Make sure we have initialized our styles
        EnsureThemeResourcesAreLoaded();


        // Main window border background
        if (MainWindowBorder != null)
        {
            var bgColor = _isDarkTheme ? _darkBackgroundColor : _lightBackgroundColor;
            SolidColorBrush brush = new SolidColorBrush(bgColor);
            brush.Opacity = 0.95; // Set opacity for transparency effect
            MainWindowBorder.Background = brush;
        }
        
        // Update all text blocks EXCEPT those inside buttons to maintain button text color consistency
        var textBlocks = new System.Collections.Generic.List<TextBlock>();
        FindVisualChildren<TextBlock>(this, textBlocks);
        foreach (var textBlock in textBlocks)
        {
            // Skip the monitoring status text as it has its own color binding
            // Also skip any TextBlock that is a child of a Button
            bool isInsideButton = IsChildOfType<System.Windows.Controls.Button>(textBlock);
            
            if (textBlock.Name != "MonitoringStatusText" && !isInsideButton)
            {
                var textColor = _isDarkTheme ? _darkForegroundColor : _lightForegroundColor;
                textBlock.Foreground = new SolidColorBrush(textColor);
            }
        }
        
        // Update DataGrid and its border
        if (ApplicationsDataGrid != null)
        {
            // Set the DataGrid row style based on theme
            ApplicationsDataGrid.RowStyle = Resources[_isDarkTheme ? "DataGridRowStyleDark" : "DataGridRowStyleLight"] as Style;
            ApplicationsDataGrid.AlternatingRowBackground = new SolidColorBrush(_isDarkTheme ? 
                System.Windows.Media.Color.FromRgb(50, 50, 52) : System.Windows.Media.Color.FromRgb(249, 249, 249));
            
            // Update the DataGrid container background
            if (DataGridBorder != null)
            {
                DataGridBorder.Background = new SolidColorBrush(_isDarkTheme ? _darkDataGridBackgroundColor : _lightDataGridBackgroundColor);
                DataGridBorder.BorderBrush = new SolidColorBrush(_isDarkTheme ? 
                    System.Windows.Media.Color.FromRgb(70, 70, 72) : System.Windows.Media.Color.FromRgb(224, 224, 224));
            }
        }
        
        // Update window control buttons
        UpdateWindowControlButtons();
        
        // Update standard buttons - preserve text color but update other properties
        var buttons = new System.Collections.Generic.List<System.Windows.Controls.Button>();
        FindVisualChildren<System.Windows.Controls.Button>(this, buttons);
        
        // Reset button styles
        foreach (var button in buttons)
        {
            UpdateButtonStyle(button);
        }
        
        // Update toggle button for monitoring
        UpdateMonitoringToggleButton();
        
        // Update the theme toggle button icon
        UpdateThemeToggleIcon(isDarkTheme);
    }

    // Update the theme toggle icon based on current theme
    private void UpdateThemeToggleIcon(bool isDarkTheme)
    {
        // Find the theme icon path
        if (ThemeIconPath != null)
        {
            // In dark theme mode, use a light icon, in light theme mode, use a dark icon
            ThemeIconPath.Fill = new SolidColorBrush(isDarkTheme ? Colors.White : Colors.Black);
        }
    }
    
    // Method to update the monitoring toggle button
    private void UpdateMonitoringToggleButton()
    {
        var toggleButton = FindVisualChild<System.Windows.Controls.Primitives.ToggleButton>(this);
        if (toggleButton != null && toggleButton.Name == "MonitoringToggleButton")
        {
            // No need to adjust colors as they're set in the style
            // Just ensure the tooltip is set
            if (string.IsNullOrEmpty(toggleButton.ToolTip?.ToString()))
            {
                toggleButton.ToolTip = "Toggle Monitoring";
            }
        }
    }
    
    // Helper method to check if an element is a child of a specific type
    private bool IsChildOfType<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T)
                return true;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }
    
    private void UpdateButtonStyle(System.Windows.Controls.Button button)
    {
        // Skip window control buttons that have their own styles
        if (button == MinimizeButton || button == MaximizeButton || button == CloseButton)
        {
            return;
        }
        
        // Store the original content
        var originalContent = button.Content;
        
        // Check if this is a special button by name
        bool isAddButton = button == AddButton;
        bool isEditButton = button == EditButton;
        bool isRemoveButton = button == RemoveButton;
        
        // If we couldn't find by reference, try to identify by name
        if (!isAddButton && !isEditButton && !isRemoveButton)
        {
            // Try to identify by button name if available
            string buttonName = button.Name;
            isAddButton = buttonName == "AddButton";
            isEditButton = buttonName == "EditButton"; 
            isRemoveButton = buttonName == "RemoveButton";
            
            // If we still can't identify by name, use content as a last resort
            if (!isAddButton && !isEditButton && !isRemoveButton)
            {
                string contentText = originalContent?.ToString();
                isAddButton = contentText == "Add";
                isEditButton = contentText == "Edit";
                isRemoveButton = contentText == "Remove";
            }
        }
        
        // Special handling for each type of button
        if (isAddButton)
        {
            // Add button keeps its special appearance
            return;
        }
        
        if (isEditButton || isRemoveButton)
        {
            // Edit and Remove buttons already have their own styles
            return;
        }
        
        // Apply consistent styling for regular buttons across both themes
        ApplyConsistentButtonStyle(button);
        
        // CRITICAL: Ensure the original content is preserved
        if (button.Content != originalContent)
        {
            button.Content = originalContent;
        }
    }
    
    // Apply consistent style to regular buttons
    private void ApplyConsistentButtonStyle(System.Windows.Controls.Button button)
    {
        // Save original content before any changes
        var originalContent = button.Content;

        // Define colors based on theme
        System.Windows.Media.Color baseColor = _isDarkTheme ? 
            System.Windows.Media.Color.FromRgb(76, 76, 82) : // Dark theme button
            System.Windows.Media.Color.FromRgb(240, 240, 240); // Light theme button

        System.Windows.Media.Color hoverColor = _isDarkTheme ? 
            System.Windows.Media.Color.FromRgb(94, 94, 102) : // Dark theme hover
            System.Windows.Media.Color.FromRgb(224, 224, 224); // Light theme hover

        System.Windows.Media.Color pressedColor = _isDarkTheme ? 
            System.Windows.Media.Color.FromRgb(60, 60, 66) : // Dark theme pressed
            System.Windows.Media.Color.FromRgb(208, 208, 208); // Light theme pressed

        System.Windows.Media.Color textColor = _isDarkTheme ? 
            System.Windows.Media.Colors.White : // Dark theme text
            System.Windows.Media.Color.FromRgb(32, 32, 32); // Light theme text

        // Apply base colors
        button.Background = new SolidColorBrush(baseColor);
        button.Foreground = new SolidColorBrush(textColor);

        // Create a new template that incorporates proper animations for hover effect
        var factory = new System.Windows.FrameworkElementFactory(typeof(Border));
        factory.Name = "ButtonBorder";
        factory.SetValue(Border.BackgroundProperty, new SolidColorBrush(baseColor));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        factory.SetValue(Border.PaddingProperty, button.Padding);
        factory.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        factory.SetValue(Border.BorderBrushProperty, button.BorderBrush);

        var contentPresenterFactory = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenterFactory.Name = "ButtonContent";
        contentPresenterFactory.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        contentPresenterFactory.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        contentPresenterFactory.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(textColor));

        factory.AppendChild(contentPresenterFactory);

        var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
        template.VisualTree = factory;

        // Create hover animation (when mouse enters)
        var mouseOverTrigger = new Trigger();
        mouseOverTrigger.Property = UIElement.IsMouseOverProperty;
        mouseOverTrigger.Value = true;
        mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "ButtonBorder"));

        // Animation for hover enter
        var enterStoryboard = new Storyboard();
        var enterAnimation = new ColorAnimation();
        Storyboard.SetTargetName(enterAnimation, "ButtonBorder");
        Storyboard.SetTargetProperty(enterAnimation, new PropertyPath("Background.Color"));
        enterAnimation.To = hoverColor;
        enterAnimation.Duration = new Duration(TimeSpan.FromSeconds(0.2));
        enterStoryboard.Children.Add(enterAnimation);

        var enterAction = new BeginStoryboard();
        enterAction.Storyboard = enterStoryboard;
        mouseOverTrigger.EnterActions.Add(enterAction);

        // Animation for hover exit
        var exitStoryboard = new Storyboard();
        var exitAnimation = new ColorAnimation();
        Storyboard.SetTargetName(exitAnimation, "ButtonBorder");
        Storyboard.SetTargetProperty(exitAnimation, new PropertyPath("Background.Color"));
        exitAnimation.To = baseColor;
        exitAnimation.Duration = new Duration(TimeSpan.FromSeconds(0.2));
        exitStoryboard.Children.Add(exitAnimation);

        var exitAction = new BeginStoryboard();
        exitAction.Storyboard = exitStoryboard;
        mouseOverTrigger.ExitActions.Add(exitAction);

        // Add the trigger
        template.Triggers.Add(mouseOverTrigger);

        // Pressed state
        var pressedTrigger = new Trigger();
        pressedTrigger.Property = System.Windows.Controls.Button.IsPressedProperty;
        pressedTrigger.Value = true;
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(pressedColor), "ButtonBorder"));

        // Animation for pressed state
        var pressedStoryboard = new Storyboard();
        var pressedAnimation = new ColorAnimation();
        Storyboard.SetTargetName(pressedAnimation, "ButtonBorder");
        Storyboard.SetTargetProperty(pressedAnimation, new PropertyPath("Background.Color"));
        pressedAnimation.To = pressedColor;
        pressedAnimation.Duration = new Duration(TimeSpan.FromSeconds(0.1));
        pressedStoryboard.Children.Add(pressedAnimation);

        var pressedAction = new BeginStoryboard();
        pressedAction.Storyboard = pressedStoryboard;
        pressedTrigger.EnterActions.Add(pressedAction);

        // Add the trigger
        template.Triggers.Add(pressedTrigger);

        // Disabled state
        var disabledTrigger = new Trigger();
        disabledTrigger.Property = UIElement.IsEnabledProperty;
        disabledTrigger.Value = false;
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
        template.Triggers.Add(disabledTrigger);

        // Apply the template
        button.Template = template;

        // CRITICAL: Restore original content
        button.Content = originalContent;
    }
    
    private void UpdateWindowControlButtons()
    {
        if (MinimizeButton != null && MaximizeButton != null && CloseButton != null)
        {
            var controlButtonColor = _isDarkTheme ? _darkForegroundColor : _lightForegroundColor;

            // Update paths inside window control buttons
            foreach (var button in new[] { MinimizeButton, MaximizeButton, CloseButton })
            {
                if (button.Content is System.Windows.Shapes.Path path)
                {
                    path.Stroke = new SolidColorBrush(controlButtonColor);
                }
            }
        }
    }

    // Helper method to find all visual children of a specific type
    private static void FindVisualChildren<T>(DependencyObject parent, System.Collections.Generic.List<T> results) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                results.Add(typedChild);
            }

            FindVisualChildren<T>(child, results);
        }
    }
    
    // Window control button handlers
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }
    
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
    
    private void MonitoringToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // Get the toggle button
        var toggleButton = sender as System.Windows.Controls.Primitives.ToggleButton;
        if (toggleButton == null) return;
        
        // Get the current pressed state
        bool isChecked = toggleButton.IsChecked ?? false;
        
        System.Diagnostics.Debug.WriteLine($"==== TOGGLE BUTTON CLICKED - User wants monitoring: {isChecked} ====");
        
        try
        {
            // Determine what operation to perform based on current monitoring state
            if (!_viewModel.IsMonitoring && isChecked)
            {
                // User wants to START monitoring
                System.Diagnostics.Debug.WriteLine("Starting monitoring...");
                
                // Check if we have valid applications to monitor
                bool hasValidTarget = _viewModel.TargetApplications.Any(app => app.RestrictToMonitor.HasValue);
                if (!hasValidTarget)
                {
                    MessageBox.Show(
                        "There are no valid applications configured for monitoring.\nPlease add at least one application and assign it to a monitor.",
                        "No Applications",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    
                    // Reset toggle button state since we're not starting monitoring
                    toggleButton.IsChecked = false;
                    return;
                }
                
                // Make sure all apps with monitor restrictions are active
                foreach (var app in _viewModel.TargetApplications)
                {
                    if (app.RestrictToMonitor.HasValue)
                    {
                        app.IsActive = true;
                    }
                }
                
                // Start monitoring
                _viewModel.StartMonitoringCommand.Execute(null);
            }
            else if (_viewModel.IsMonitoring && !isChecked)
            {
                // User wants to STOP monitoring
                System.Diagnostics.Debug.WriteLine("Stopping monitoring...");
                _viewModel.StopMonitoringCommand.Execute(null);
            }
            
            // Refresh DataGrid to show changes
            if (ApplicationsDataGrid != null)
            {
                ApplicationsDataGrid.Items.Refresh();
            }
            
            // Force configuration save
            SaveConfiguration();
            
            // Ensure button state matches the actual monitoring state
            toggleButton.IsChecked = _viewModel.IsMonitoring;
            System.Diagnostics.Debug.WriteLine($"Final state: Monitoring={_viewModel.IsMonitoring}, Button={toggleButton.IsChecked}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: {ex.Message}");
            MessageBox.Show(
                $"An error occurred: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
                
            // Reset toggle button state to match the actual monitoring state
            toggleButton.IsChecked = _viewModel.IsMonitoring;
        }
    }
    
    // Add a dedicated method to load configuration
    private async void LoadConfiguration()
    {
        try
        {
            Debug.WriteLine("MainWindow: Loading configuration");
            
            // Initialize and load configuration
            await _viewModel.LoadConfigurationAsync();
            
            // Once configuration is loaded, update the UI to match
            UpdateMonitoringToggleButton();
            
            Debug.WriteLine($"MainWindow: Configuration loaded. Monitoring state: {_viewModel.IsMonitoring}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load configuration: {ex.Message}");
            MessageBox.Show(
                $"Failed to load application settings: {ex.Message}",
                "Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // Improve the save configuration method
    private async void SaveConfiguration()
    {
        try
        {
            Debug.WriteLine("MainWindow: Saving configuration");
            
            await _viewModel.SaveConfigurationAsync();
            
            Debug.WriteLine("MainWindow: Configuration saved successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save configuration: {ex.Message}");
            MessageBox.Show(
                $"Failed to save settings: {ex.Message}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_themeManager != null)
        {
            // Toggle the theme
            _themeManager.ToggleTheme();
            
            // Theme application will happen automatically through the ThemeChanged event
        }
    }
    
    private void ApplicationsDataGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clear selection when clicking on empty space
        var hitTestResult = VisualTreeHelper.HitTest(ApplicationsDataGrid, e.GetPosition(ApplicationsDataGrid));
        
        if (hitTestResult != null)
        {
            DependencyObject obj = hitTestResult.VisualHit;
            // Navigate up the visual tree to find a DataGridRow or header
            bool foundElement = false;
            while (obj != null && !foundElement)
            {
                if (obj is System.Windows.Controls.DataGridRow || 
                    obj is System.Windows.Controls.DataGridCell || 
                    obj is System.Windows.Controls.Primitives.DataGridColumnHeader)
                {
                    foundElement = true;
                    break;
                }
                obj = VisualTreeHelper.GetParent(obj);
            }
            
            // If we didn't find a row or header, then we clicked on empty space
            if (!foundElement)
            {
                ApplicationsDataGrid.UnselectAll();
                e.Handled = true;
            }
        }
    }
    
    private void ApplicationsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Handle double-click to edit
        if (_viewModel.SelectedApplication != null && _viewModel.EditApplicationCommand.CanExecute(null))
        {
            // Check if we clicked on a row and not a header
            var hitTestResult = VisualTreeHelper.HitTest(ApplicationsDataGrid, e.GetPosition(ApplicationsDataGrid));
            
            if (hitTestResult != null)
            {
                DependencyObject obj = hitTestResult.VisualHit;
                // Navigate up the visual tree to find a DataGridRow
                while (obj != null && !(obj is System.Windows.Controls.DataGridRow))
                {
                    obj = VisualTreeHelper.GetParent(obj);
                }
                
                // If we found a row, edit the selected application
                if (obj is System.Windows.Controls.DataGridRow)
                {
                    _viewModel.EditApplicationCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
    
    public bool IsMonitoring => _viewModel.IsMonitoring;
    
    public void SetMonitoring(bool isActive)
    {
        if (isActive && !_viewModel.IsMonitoring)
        {
            _viewModel.StartMonitoringCommand.Execute(null);
        }
        else if (!isActive && _viewModel.IsMonitoring)
        {
            _viewModel.StopMonitoringCommand.Execute(null);
        }
    }
    
    private void OnAppActiveChanged(object sender, bool isActive)
    {
        // We don't need this anymore since we removed the active toggle from the tray
    }

    // Dispose method to clean up resources
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
            // Clean up resources
            
            // Unsubscribe from events
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                
                // Dispose the view model
                _viewModel.Dispose();
                _viewModel = null;
            }
            
            if (_themeManager != null && _themeChangedHandler != null)
            {
                _themeManager.ThemeChanged -= _themeChangedHandler;
            }
            
            // Unsubscribe window event handlers
            if (_mouseLeftButtonDownHandler != null)
            {
                this.MouseLeftButtonDown -= _mouseLeftButtonDownHandler;
            }
            
            if (_loadedHandler != null)
            {
                this.Loaded -= _loadedHandler;
            }
            
            if (_gridLoadedHandler != null)
            {
                this.Loaded -= _gridLoadedHandler;
            }
            
            // Find DataGrid and remove its event handlers
            var dataGrid = FindVisualChild<System.Windows.Controls.DataGrid>(this);
            if (dataGrid != null)
            {
                if (_dataGridPreviewMouseDownHandler != null)
                {
                    dataGrid.PreviewMouseDown -= _dataGridPreviewMouseDownHandler;
                }
                
                if (_dataGridPreviewKeyDownHandler != null)
                {
                    dataGrid.PreviewKeyDown -= _dataGridPreviewKeyDownHandler;
                }
                
                if (_dataGridContextMenuOpeningHandler != null)
                {
                    dataGrid.ContextMenuOpening -= _dataGridContextMenuOpeningHandler;
                }
            }
            
            // Dispose services
            if (_windowMonitorService != null)
            {
                _windowMonitorService.Dispose();
            }
            
            // Clear event handler references
            _themeChangedHandler = null;
            _mouseLeftButtonDownHandler = null;
            _loadedHandler = null;
            _gridLoadedHandler = null;
            _dataGridPreviewMouseDownHandler = null;
            _dataGridPreviewKeyDownHandler = null;
            _dataGridContextMenuOpeningHandler = null;
        }
        
        _isDisposed = true;
    }
    
    // Handle window closing to properly clean up resources
    protected override void OnClosed(EventArgs e)
    {
        // Call Dispose to ensure resources are properly cleaned up
        Dispose(true);
        base.OnClosed(e);
        
        // Force garbage collection to release memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    // Helper method to ensure theme resources are loaded
    private void EnsureThemeResourcesAreLoaded()
    {
        if (Resources.Contains("DataGridRowStyleLight"))
        {
            _originalLightRowStyle = Resources["DataGridRowStyleLight"] as Style;
        }
        if (Resources.Contains("DataGridRowStyleDark"))
        {
            _originalDarkRowStyle = Resources["DataGridRowStyleDark"] as Style;
        }
    }

    // DataGrid event handlers
    private bool FindIfClickedOnActiveCheckbox(DependencyObject obj)
    {
        // Traverse up to find if we clicked on a checkbox
        while (obj != null)
        {
            if (obj is System.Windows.Controls.CheckBox checkbox)
            {
                // Check if this checkbox is in the "IsActive" column
                var parent = VisualTreeHelper.GetParent(checkbox);
                while (parent != null && !(parent is System.Windows.Controls.DataGridCell))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }
                
                if (parent is System.Windows.Controls.DataGridCell cell)
                {
                    // Check if this is the IsActive column
                    return cell.Column.DisplayIndex == 0;
                }
                
                return true;
            }
            
            obj = VisualTreeHelper.GetParent(obj);
        }
        
        return false;
    }

    private void DataGrid_PreviewMouseDown(System.Windows.Controls.DataGrid dataGrid, MouseButtonEventArgs args)
    {
        if (_isDisposed) return;
        
        // Get hit test result to see where exactly the user clicked
        var hitTestResult = VisualTreeHelper.HitTest(dataGrid, args.GetPosition(dataGrid));
        if (hitTestResult != null)
        {
            // Find the clicked element
            DependencyObject depObj = hitTestResult.VisualHit;

            // Traverse up to find if we clicked on a row
            while (depObj != null && !(depObj is System.Windows.Controls.DataGridRow))
            {
                depObj = VisualTreeHelper.GetParent(depObj);
            }
            
            if (depObj is System.Windows.Controls.DataGridRow row)
            {
                // Access row data
                if (row.DataContext is ApplicationWindow app)
                {
                    // Find if clicked on checkbox
                    bool clickedOnCheckbox = FindIfClickedOnActiveCheckbox(hitTestResult.VisualHit);
                    
                    if (clickedOnCheckbox)
                    {
                        // Toggle active state directly
                        _viewModel.ToggleActiveState(app);
                        args.Handled = true;
                    }
                }
            }
        }
    }

    private void DataGrid_PreviewKeyDown(System.Windows.Controls.DataGrid dataGrid, System.Windows.Input.KeyEventArgs args)
    {
        if (_isDisposed) return;
        
        // Enter key for editing application
        if (args.Key == Key.Return || args.Key == Key.Enter)
        {
            if (_viewModel.SelectedApplication != null && 
                _viewModel.EditApplicationCommand.CanExecute(null))
            {
                _viewModel.EditApplicationCommand.Execute(null);
                args.Handled = true;
            }
        }
        // Delete key for removing application
        else if (args.Key == Key.Delete)
        {
            if (_viewModel.SelectedApplication != null && 
                _viewModel.RemoveApplicationCommand.CanExecute(null))
            {
                _viewModel.RemoveApplicationCommand.Execute(null);
                args.Handled = true;
            }
        }
        // Space key to toggle the active state
        else if (args.Key == Key.Space)
        {
            _viewModel.ToggleActiveState(_viewModel.SelectedApplication);
            args.Handled = true;
        }
        // Prevent arrow keys from toggling state by explicitly handling them
        else if (args.Key == Key.Right || args.Key == Key.Left)
        {
            // Complete consume right/left keys to prevent toggling
            args.Handled = true;
        }
    }

    private void DataGrid_ContextMenuOpening(System.Windows.Controls.DataGrid dataGrid, object sender, ContextMenuEventArgs args)
    {
        if (_isDisposed) return;
        
        try
        {
            // Get mouse position relative to the DataGrid
            System.Windows.Point mousePosition = Mouse.GetPosition(dataGrid);

            // Get the row under the mouse
            var hitTestResult = VisualTreeHelper.HitTest(dataGrid, mousePosition);
            if (hitTestResult != null)
            {
                // Find the DataGridRow that was clicked
                DependencyObject obj = hitTestResult.VisualHit;
                while (obj != null && !(obj is System.Windows.Controls.DataGridRow))
                {
                    obj = VisualTreeHelper.GetParent(obj);
                }

                if (obj is System.Windows.Controls.DataGridRow row)
                {
                    // Get the application from the row
                    if (row.DataContext is ApplicationWindow app)
                    {
                        // Select the row
                        dataGrid.SelectedItem = app;
                        _viewModel.SelectedApplication = app;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in context menu handling: {ex.Message}");
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        
        // Put the monitor service in dormant mode when minimized
        if (_viewModel?.WindowMonitorService != null)
        {
            bool isDormant = WindowState == WindowState.Minimized;
            _viewModel.WindowMonitorService.SetDormantMode(isDormant);
        }
    }
}
