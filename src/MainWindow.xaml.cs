#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MonitorBounds.Converters;
using MonitorBounds.Models;
using MonitorBounds.Services;
using MonitorBounds.ViewModels;

namespace MonitorBounds
{
    public partial class MainWindow : Window
    {
        // Services and view model
        private readonly WindowMonitorService _windowMonitorService;
        private readonly ConfigurationService _configurationService;
        private MainViewModel _viewModel;
        private ThemeManager? _themeManager;
        private bool _isDarkTheme;

        // Dictionary to store event handlers for easier management
        private readonly Dictionary<string, Delegate> _eventHandlers = new Dictionary<string, Delegate>();

        // Cached UI elements (using FindName when possible)
        private DataGrid? _applicationsDataGrid;
        private Border? _mainWindowBorder;
        private Border? _dataGridBorder;
        private Path? _themeIconPath;

        // Cached brushes for themes
        private SolidColorBrush? _lightBackgroundBrush;
        private SolidColorBrush? _lightForegroundBrush;
        private SolidColorBrush? _lightDataGridBackgroundBrush;
        private SolidColorBrush? _darkBackgroundBrush;
        private SolidColorBrush? _darkForegroundBrush;
        private SolidColorBrush? _darkDataGridBackgroundBrush;

        // Theme colors
        private readonly Color _lightBackgroundColor = Color.FromRgb(248, 248, 248);
        private readonly Color _lightForegroundColor = Color.FromRgb(30, 30, 30);
        private readonly Color _lightDataGridBackgroundColor = Colors.White;
        private readonly Color _darkBackgroundColor = Color.FromRgb(45, 45, 48);
        private readonly Color _darkForegroundColor = Colors.White;
        private readonly Color _darkDataGridBackgroundColor = Color.FromRgb(60, 60, 62);

        // Cached button templates (for reuse across buttons)
        private ControlTemplate? _lightButtonTemplate;
        private ControlTemplate? _darkButtonTemplate;

        // Lazy-loaded row styles
        private Lazy<Style>? _lightRowStyle;
        private Lazy<Style>? _darkRowStyle;
        private Style? _originalLightRowStyle;
        private Style? _originalDarkRowStyle;

        public MainWindow()
        {
            // Register converters in resources
            Resources.Add("BoolToStringConverter", new BoolToStringConverter());
            Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
            Resources.Add("BoolToColorConverter", new BoolToColorConverter());
            Resources.Add("ToggleButtonCommandConverter", new ToggleButtonCommandConverter());

            InitializeComponent();

            // Cache UI elements using their x:Name (defined in XAML)
            _mainWindowBorder = this.FindName("MainWindowBorder") as Border;
            _dataGridBorder = this.FindName("DataGridBorder") as Border;
            _themeIconPath = this.FindName("ThemeIconPath") as Path;
            _applicationsDataGrid = this.FindName("ApplicationsDataGrid") as DataGrid;

            // Retrieve theme manager from the App instance
            if (Application.Current is App app)
            {
                _themeManager = app.ThemeManager;
                _isDarkTheme = _themeManager?.IsDarkTheme ?? false;
            }

            // Initialize services and view model
            _configurationService = new ConfigurationService();
            _windowMonitorService = new WindowMonitorService();
            _viewModel = new MainViewModel(_windowMonitorService, _configurationService);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            DataContext = _viewModel;

            // Load configuration
            LoadConfiguration();

            // Initialize cached theme resources and button templates
            InitializeThemeBrushes();
            InitializeButtonTemplates();
            InitializeLazyResources();

            // Apply the initial theme
            ApplyTheme(_isDarkTheme);

            // Set up event handlers via a centralized dictionary
            SetupEventHandlers();

            // Add mouse event for window dragging
            _eventHandlers["MouseLeftButtonDown"] = new MouseButtonEventHandler((s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
                {
                    this.DragMove();
                }
            });
            this.MouseLeftButtonDown += (MouseButtonEventHandler)_eventHandlers["MouseLeftButtonDown"];

            // Set up DataGrid events
            SetupDataGridEvents();
        }

        #region Initialization Helpers

        private void InitializeThemeBrushes()
        {
            _lightBackgroundBrush = new SolidColorBrush(_lightBackgroundColor) { Opacity = 0.95 };
            _lightForegroundBrush = new SolidColorBrush(_lightForegroundColor);
            _lightDataGridBackgroundBrush = new SolidColorBrush(_lightDataGridBackgroundColor);
            _darkBackgroundBrush = new SolidColorBrush(_darkBackgroundColor) { Opacity = 0.95 };
            _darkForegroundBrush = new SolidColorBrush(_darkForegroundColor);
            _darkDataGridBackgroundBrush = new SolidColorBrush(_darkDataGridBackgroundColor);
        }

        private void InitializeButtonTemplates()
        {
            // Create and cache button templates for light and dark themes
            _lightButtonTemplate = CreateButtonTemplate(isDark: false);
            _darkButtonTemplate = CreateButtonTemplate(isDark: true);
        }

        private ControlTemplate CreateButtonTemplate(bool isDark)
        {
            // Create a basic ControlTemplate with a Border and ContentPresenter
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "ButtonBorder";
            var baseColor = isDark ? Color.FromRgb(76, 76, 82) : Color.FromRgb(240, 240, 240);
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(baseColor));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.Name = "ButtonContent";
            contentFactory.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            var template = new ControlTemplate(typeof(Button))
            {
                VisualTree = borderFactory
            };

            // (Optional) Insert triggers here for hover, pressed, and disabled states.
            return template;
        }

        private void InitializeLazyResources()
        {
            // Lazy load DataGrid row styles from resources
            _lightRowStyle = new Lazy<Style>(() => Resources["DataGridRowStyleLight"] as Style ?? new Style());
            _darkRowStyle = new Lazy<Style>(() => Resources["DataGridRowStyleDark"] as Style ?? new Style());
        }

        private void SetupEventHandlers()
        {
            if (_themeManager != null)
            {
                // Theme change event
                _eventHandlers["ThemeChanged"] = new EventHandler<bool>(ThemeManager_ThemeChanged);
                _themeManager.ThemeChanged += (EventHandler<bool>)_eventHandlers["ThemeChanged"];

                // Loaded event to ensure theme is applied after layout
                _eventHandlers["Loaded"] = new RoutedEventHandler((s, e) =>
                {
                    ApplyTheme(_themeManager.IsDarkTheme);
                    if (_viewModel.IsMonitoring)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateMonitoringToggleButton));
                    }
                });
                this.Loaded += (RoutedEventHandler)_eventHandlers["Loaded"];
            }
        }

        private void SetupDataGridEvents()
        {
            // Ensure the DataGrid is cached
            if (_applicationsDataGrid == null)
            {
                _applicationsDataGrid = FindVisualChild<DataGrid>(this);
            }
            if (_applicationsDataGrid != null)
            {
                _eventHandlers["DataGridPreviewMouseDown"] = new MouseButtonEventHandler((s, e) =>
                    DataGrid_PreviewMouseDown(_applicationsDataGrid, e));
                _applicationsDataGrid.PreviewMouseDown += (MouseButtonEventHandler)_eventHandlers["DataGridPreviewMouseDown"];

                _eventHandlers["DataGridPreviewKeyDown"] = new KeyEventHandler((s, e) =>
                    DataGrid_PreviewKeyDown(_applicationsDataGrid, e));
                _applicationsDataGrid.PreviewKeyDown += (KeyEventHandler)_eventHandlers["DataGridPreviewKeyDown"];

                _eventHandlers["DataGridContextMenuOpening"] = new ContextMenuEventHandler((s, e) =>
                    DataGrid_ContextMenuOpening(_applicationsDataGrid, s, e));
                _applicationsDataGrid.ContextMenuOpening += (ContextMenuEventHandler)_eventHandlers["DataGridContextMenuOpening"];

                // Handle grid loaded event for initial row selection
                _eventHandlers["GridLoaded"] = new RoutedEventHandler((s, e) =>
                {
                    if (_applicationsDataGrid.Items.Count > 0)
                        _applicationsDataGrid.SelectedIndex = 0;
                });
                _applicationsDataGrid.Loaded += (RoutedEventHandler)_eventHandlers["GridLoaded"];
            }
        }

        #endregion

        #region Visual Tree Helpers

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static void FindVisualChildren<T>(DependencyObject parent, List<T> results) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    results.Add(typedChild);
                FindVisualChildren<T>(child, results);
            }
        }

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

        #endregion

        #region Theme and UI Updates

        public void ApplyTheme(bool isDarkTheme)
        {
            _isDarkTheme = isDarkTheme;
            EnsureThemeResourcesAreLoaded();

            // Update main window border background using cached brushes
            if (_mainWindowBorder != null)
            {
                _mainWindowBorder.Background = _isDarkTheme ? _darkBackgroundBrush : _lightBackgroundBrush;
            }

            // Update text block colors (excluding those inside buttons)
            var textBlocks = new List<TextBlock>();
            FindVisualChildren<TextBlock>(this, textBlocks);
            foreach (var textBlock in textBlocks)
            {
                if (textBlock.Name != "MonitoringStatusText" && !IsChildOfType<Button>(textBlock))
                {
                    textBlock.Foreground = _isDarkTheme
                        ? new SolidColorBrush(_darkForegroundColor)
                        : new SolidColorBrush(_lightForegroundColor);
                }
            }

            // Update DataGrid styling
            if (_applicationsDataGrid != null)
            {
                if (_darkRowStyle != null && _lightRowStyle != null)
                {
                    _applicationsDataGrid.RowStyle = _isDarkTheme ? _darkRowStyle.Value : _lightRowStyle.Value;
                }
                _applicationsDataGrid.AlternatingRowBackground = new SolidColorBrush(_isDarkTheme
                    ? Color.FromRgb(50, 50, 52)
                    : Color.FromRgb(249, 249, 249));

                if (_dataGridBorder != null && _darkDataGridBackgroundBrush != null && _lightDataGridBackgroundBrush != null)
                {
                    _dataGridBorder.Background = _isDarkTheme ? _darkDataGridBackgroundBrush : _lightDataGridBackgroundBrush;
                    _dataGridBorder.BorderBrush = new SolidColorBrush(_isDarkTheme
                        ? Color.FromRgb(70, 70, 72)
                        : Color.FromRgb(224, 224, 224));
                }
            }

            UpdateWindowControlButtons();

            // Update all standard buttons using cached button templates
            var buttons = new List<Button>();
            FindVisualChildren<Button>(this, buttons);
            foreach (var button in buttons)
            {
                // Skip window control buttons (assumed to be named accordingly)
                if (button == this.FindName("MinimizeButton") as Button ||
                    button == this.FindName("MaximizeButton") as Button ||
                    button == this.FindName("CloseButton") as Button)
                {
                    continue;
                }
                UpdateButtonStyle(button);
            }

            UpdateMonitoringToggleButton();
            UpdateThemeToggleIcon(_isDarkTheme);
        }

        private void UpdateThemeToggleIcon(bool isDarkTheme)
        {
            if (_themeIconPath != null)
            {
                _themeIconPath.Fill = new SolidColorBrush(isDarkTheme ? Colors.White : Colors.Black);
            }
        }

        private void UpdateMonitoringToggleButton()
        {
            var toggleButton = FindVisualChild<ToggleButton>(this);
            if (toggleButton != null && toggleButton.Name == "MonitoringToggleButton")
            {
                if (string.IsNullOrEmpty(toggleButton.ToolTip?.ToString()))
                {
                    toggleButton.ToolTip = "Toggle Monitoring";
                }
            }
        }

        private void UpdateButtonStyle(Button button)
        {
            // If the button is one of the special ones, leave its original style intact.
            if (button.Name == "AddButton" || button.Name == "EditButton" || button.Name == "RemoveButton")
            {
                return;
            }
            
            // Preserve the original content
            var originalContent = button.Content;
            // Apply the appropriate cached template based on current theme
            button.Template = _isDarkTheme ? _darkButtonTemplate : _lightButtonTemplate;
            button.Content = originalContent;
        }

        private void UpdateWindowControlButtons()
        {
            var minimizeButton = this.FindName("MinimizeButton") as Button;
            var maximizeButton = this.FindName("MaximizeButton") as Button;
            var closeButton = this.FindName("CloseButton") as Button;
            if (minimizeButton != null && maximizeButton != null && closeButton != null)
            {
                var controlButtonColor = _isDarkTheme ? _darkForegroundColor : _lightForegroundColor;
                foreach (var button in new[] { minimizeButton, maximizeButton, closeButton })
                {
                    if (button.Content is Path path)
                    {
                        path.Stroke = new SolidColorBrush(controlButtonColor);
                    }
                }
            }
        }

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

        #endregion

        #region DataGrid and Configuration Handlers

        private void LoadConfiguration()
        {
            // Fire-and-forget async configuration load; update UI once done
            _viewModel.LoadConfigurationAsync().ContinueWith(task =>
            {
                Dispatcher.Invoke(UpdateMonitoringToggleButton);
            });
        }

        private async void SaveConfiguration()
        {
            try
            {
                await _viewModel.SaveConfigurationAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplicationsDataGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_applicationsDataGrid == null)
                return;

            var hitTestResult = VisualTreeHelper.HitTest(_applicationsDataGrid, e.GetPosition(_applicationsDataGrid));
            if (hitTestResult != null)
            {
                DependencyObject obj = hitTestResult.VisualHit;
                bool foundElement = false;
                while (obj != null && !foundElement)
                {
                    if (obj is DataGridRow || obj is DataGridCell || obj is DataGridColumnHeader)
                    {
                        foundElement = true;
                        break;
                    }
                    obj = VisualTreeHelper.GetParent(obj);
                }
                if (!foundElement)
                {
                    _applicationsDataGrid.UnselectAll();
                    e.Handled = true;
                }
            }
        }

        private void ApplicationsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedApplication != null && _viewModel.EditApplicationCommand.CanExecute(null))
            {
                var hitTestResult = VisualTreeHelper.HitTest(_applicationsDataGrid, e.GetPosition(_applicationsDataGrid));
                if (hitTestResult != null)
                {
                    DependencyObject obj = hitTestResult.VisualHit;
                    while (obj != null && !(obj is DataGridRow))
                    {
                        obj = VisualTreeHelper.GetParent(obj);
                    }
                    if (obj is DataGridRow)
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

        private void DataGrid_PreviewMouseDown(DataGrid dataGrid, MouseButtonEventArgs args)
        {
            HitTestResult hitTestResult = VisualTreeHelper.HitTest(dataGrid, args.GetPosition(dataGrid));
            if (hitTestResult != null)
            {
                DependencyObject depObj = hitTestResult.VisualHit;
                while (depObj != null && !(depObj is DataGridRow))
                {
                    depObj = VisualTreeHelper.GetParent(depObj);
                }
                if (depObj is DataGridRow row && row.DataContext is ApplicationWindow app)
                {
                    bool clickedOnCheckbox = FindIfClickedOnActiveCheckbox(hitTestResult.VisualHit);
                    if (clickedOnCheckbox)
                    {
                        _viewModel.ToggleActiveState(app);
                        args.Handled = true;
                    }
                }
            }
        }

        private void DataGrid_PreviewKeyDown(DataGrid dataGrid, KeyEventArgs args)
        {
            if (args.Key == Key.Return || args.Key == Key.Enter)
            {
                if (_viewModel.SelectedApplication != null && _viewModel.EditApplicationCommand.CanExecute(null))
                {
                    _viewModel.EditApplicationCommand.Execute(null);
                    args.Handled = true;
                }
            }
            else if (args.Key == Key.Delete)
            {
                if (_viewModel.SelectedApplication != null && _viewModel.RemoveApplicationCommand.CanExecute(null))
                {
                    _viewModel.RemoveApplicationCommand.Execute(null);
                    args.Handled = true;
                }
            }
            else if (args.Key == Key.Space)
            {
                if (_viewModel.SelectedApplication != null)
                {
                    _viewModel.ToggleActiveState(_viewModel.SelectedApplication);
                }
                args.Handled = true;
            }
            else if (args.Key == Key.Right || args.Key == Key.Left)
            {
                args.Handled = true;
            }
        }

        private void DataGrid_ContextMenuOpening(DataGrid dataGrid, object sender, ContextMenuEventArgs args)
        {
            try
            {
                Point mousePosition = Mouse.GetPosition(dataGrid);
                var hitTestResult = VisualTreeHelper.HitTest(dataGrid, mousePosition);
                if (hitTestResult != null)
                {
                    DependencyObject obj = hitTestResult.VisualHit;
                    while (obj != null && !(obj is DataGridRow))
                    {
                        obj = VisualTreeHelper.GetParent(obj);
                    }
                    if (obj is DataGridRow row && row.DataContext is ApplicationWindow app)
                    {
                        dataGrid.SelectedItem = app;
                        _viewModel.SelectedApplication = app;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private bool FindIfClickedOnActiveCheckbox(DependencyObject obj)
        {
            // Traverse up to see if a CheckBox in the IsActive column was clicked
            while (obj != null)
            {
                if (obj is CheckBox)
                {
                    DependencyObject parent = VisualTreeHelper.GetParent(obj);
                    while (parent != null && !(parent is DataGridCell))
                    {
                        parent = VisualTreeHelper.GetParent(parent);
                    }
                    if (parent is DataGridCell cell)
                    {
                        return cell.Column.DisplayIndex == 0;
                    }
                    return true;
                }
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        #endregion

        #region Window State and Cleanup

        // Event handlers for window control buttons referenced in XAML
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MonitoringToggleButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle monitoring state based on current state
            bool newState = !_viewModel.IsMonitoring;
            SetMonitoring(newState);
        }

        // New: Added ThemeToggleButton_Click to resolve XAML reference.
        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _themeManager?.ToggleTheme();
        }
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        #endregion

        #region ThemeManager Event Handler

        private void ThemeManager_ThemeChanged(object? sender, bool isDarkTheme)
        {
            // Apply theme and update icon
            _isDarkTheme = isDarkTheme;
            ApplyTheme(isDarkTheme);
            UpdateThemeToggleIcon(isDarkTheme);
        }

        #endregion

        #region ViewModel Property Change Handler

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e == null)
                return;

            if (e.PropertyName == nameof(_viewModel.IsMonitoring))
            {
                // Update monitoring toggle button or other UI elements if needed
            }
        }

        #endregion
    }
}
