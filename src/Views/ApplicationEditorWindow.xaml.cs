using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Drawing;
using System.Runtime.InteropServices;
using MonitorBounds.Models;

namespace MonitorBounds.Views
{
    // Interaction logic for ApplicationEditorWindow.xaml
    public partial class ApplicationEditorWindow : Window
    {
        private readonly ApplicationWindow _application;
        private Form _pickerForm;


        // Gets the edited application
        public ApplicationWindow Application 
        {
            get
            {
                // Return a fresh copy to avoid any binding/reference issues
                if (_application == null) return null;
                
                return new ApplicationWindow
                {
                    TitlePattern = _application.TitlePattern,
                    IsActive = _application.IsActive,
                    RestrictToMonitor = _application.RestrictToMonitor,
                    Handle = _application.Handle
                };
            }
        }


        // Creates a new instance of the ApplicationEditorWindow for editing an existing application
        public ApplicationEditorWindow(ApplicationWindow application)
        {
            InitializeComponent();

            // Create a copy to avoid modifying the original directly
            _application = new ApplicationWindow
            {
                TitlePattern = application.TitlePattern,
                IsActive = application.IsActive,
                RestrictToMonitor = application.RestrictToMonitor,
                Handle = application.Handle
            };

            DataContext = _application;
            InitializeMonitorComboBox();
        }


        // Creates a new instance of the ApplicationEditorWindow for creating a new application
        public ApplicationEditorWindow()
        {
            InitializeComponent();
            
            _application = new ApplicationWindow
            {
                TitlePattern = "*",
                IsActive = true,
                RestrictToMonitor = null
            };
            
            DataContext = _application;
            InitializeMonitorComboBox();
        }


        // Initializes the monitor combo box with available monitors
        private void InitializeMonitorComboBox()
        {
            // Clear existing items
            MonitorComboBox.Items.Clear();
            
            // Get display devices using Windows API
            WindowsAPI.DISPLAY_DEVICE displayDevice = new WindowsAPI.DISPLAY_DEVICE();
            displayDevice.cb = Marshal.SizeOf(typeof(WindowsAPI.DISPLAY_DEVICE));
            
            int monitorCount = 0;
            for (uint i = 0; i < 10; i++) // Check up to 10 monitors
            {
                if (WindowsAPI.EnumDisplayDevices(null, i, ref displayDevice, 0))
                {
                    monitorCount++;
                    string displayName = string.IsNullOrEmpty(displayDevice.DeviceString) 
                        ? $"Monitor {i}" 
                        : $"Monitor {i}";
                    
                    MonitorComboBox.Items.Add(displayName);
                }
            }
            
            // Set the selected index based on the RestrictToMonitor value
            if (_application.RestrictToMonitor.HasValue && 
                _application.RestrictToMonitor.Value >= 0 && 
                _application.RestrictToMonitor.Value < MonitorComboBox.Items.Count)
            {
                MonitorComboBox.SelectedIndex = _application.RestrictToMonitor.Value;
            }
            else if (MonitorComboBox.Items.Count > 0)
            {
                // Default to first monitor
                MonitorComboBox.SelectedIndex = 0;
                _application.RestrictToMonitor = 0;
            }
            
            // Monitor combo box selection changed handler
            MonitorComboBox.SelectionChanged += (s, e) =>
            {
                if (MonitorComboBox.SelectedIndex >= 0)
                {
                    _application.RestrictToMonitor = MonitorComboBox.SelectedIndex;
                }
            };
        }


        // Handles the OK button click
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {

            // Validate input
            if (string.IsNullOrWhiteSpace(_application.TitlePattern))
            {
                System.Windows.MessageBox.Show("Please enter a title pattern for the application.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Update monitor restriction based on combo box
            if (MonitorComboBox.SelectedIndex >= 0)
            {
                _application.RestrictToMonitor = MonitorComboBox.SelectedIndex;
            }
            else if (MonitorComboBox.Items.Count > 0)
            {
                _application.RestrictToMonitor = 0; // Default to first monitor
            }

            
            // Set standard WPF DialogResult
            this.DialogResult = true;
            Close();
        }


        // Handles the Cancel button click
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }
        

        // Allows the user to pick a window by clicking on it
        private void PickWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Hide(); // Hide this window while picking

            try
            {
                // Create a semi-transparent form with cursor crosshair
                using (_pickerForm = new Form
                {
                    WindowState = FormWindowState.Maximized,
                    FormBorderStyle = FormBorderStyle.None,
                    Opacity = 0.2,
                    ShowInTaskbar = false,
                    BackColor = System.Drawing.Color.CornflowerBlue,
                    Cursor = Cursors.Cross,
                    TopMost = true
                })
                {
                    // Add instructions label
                    var label = new Label
                    {
                        AutoSize = false,
                        TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                        Dock = DockStyle.Fill,
                        ForeColor = System.Drawing.Color.Black,
                        Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 24, System.Drawing.FontStyle.Bold),
                        Text = "Click on a window to select it, or press ESC to cancel",
                        BackColor = System.Drawing.Color.Transparent
                    };

                    _pickerForm.Controls.Add(label);

                    // Handle mouse click event
                    _pickerForm.MouseClick += (s, args) =>
                    {
                        _pickerForm.DialogResult = System.Windows.Forms.DialogResult.OK;
                        _pickerForm.Close();

                        // Get the window under the cursor
                        IntPtr hWnd = WindowFromPoint(new System.Drawing.Point(args.X, args.Y));
                        if (hWnd != IntPtr.Zero && hWnd != _pickerForm.Handle)
                        {
                            string title = GetWindowTitle(hWnd);
                            if (!string.IsNullOrEmpty(title))
                            {
                                // Update the application properties on the UI thread
                                this.Dispatcher.Invoke(() =>
                                {
                                    _application.Handle = hWnd;
                                    _application.TitlePattern = $"*{title}*";

                                    // Force data binding update
                                    DataContext = null;
                                    DataContext = _application;
                                });
                            }
                        }
                    };

                    // Handle escape key
                    _pickerForm.KeyDown += (s, args) =>
                    {
                        if (args.KeyCode == Keys.Escape)
                        {
                            _pickerForm.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                            _pickerForm.Close();
                        }
                    };

                    // Show the form as a dialog to block until it's closed
                    _pickerForm.ShowDialog();
                }
            }
            finally
            {
                this.Dispatcher.Invoke(() => this.Show()); // Make sure we show our window again
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point p);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        private string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, builder, builder.Capacity);
            return builder.ToString();
        }

        // Apply the current application theme when the window loads
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Get the app's theme manager
            var app = System.Windows.Application.Current as MonitorBounds.App;
            var themeManager = app?.ThemeManager;
            
            if (themeManager != null)
            {
                ApplyTheme(themeManager.IsDarkTheme);
            }
        }
        
        // Apply the theme to the editor window
        private void ApplyTheme(bool isDarkTheme)
        {
            // Update background and text colors
            if (isDarkTheme)
            {
                // Apply dark theme
                MainBorder.Background = (SolidColorBrush)Resources["BackgroundBrushDark"];
                
                // Update text colors for all labels
                TitleLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushDark"];
                EnableLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushDark"];
                MonitorLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushDark"];
                HelpLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushDark"];
                HelpText.Foreground = (SolidColorBrush)Resources["ForegroundBrushDark"];
                
                // Update control backgrounds as needed
                TitleTextBox.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48));
                TitleTextBox.Foreground = System.Windows.Media.Brushes.White;
                
                // Update button styles
                UpdateButtonsForDarkTheme();
            }
            else
            {
                // Apply light theme
                MainBorder.Background = (SolidColorBrush)Resources["BackgroundBrushLight"];
                
                // Update text colors for all labels
                TitleLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushLight"];
                EnableLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushLight"];
                MonitorLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushLight"];
                HelpLabel.Foreground = (SolidColorBrush)Resources["ForegroundBrushLight"];
                HelpText.Foreground = (SolidColorBrush)Resources["ForegroundBrushLight"];
                
                // Update control backgrounds as needed
                TitleTextBox.Background = System.Windows.Media.Brushes.White;
                TitleTextBox.Foreground = System.Windows.Media.Brushes.Black;
                
                // Update button styles
                UpdateButtonsForLightTheme();
            }
        }
        
        // Update button styles for dark theme
        private void UpdateButtonsForDarkTheme()
        {
            OkButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 75));
            OkButton.Foreground = System.Windows.Media.Brushes.White;
            
            CancelButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 75));
            CancelButton.Foreground = System.Windows.Media.Brushes.White;
        }
        
        // Update button styles for light theme
        private void UpdateButtonsForLightTheme()
        {
            OkButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            OkButton.Foreground = System.Windows.Media.Brushes.Black;
            
            CancelButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            CancelButton.Foreground = System.Windows.Media.Brushes.Black;
        }
    }
} 
