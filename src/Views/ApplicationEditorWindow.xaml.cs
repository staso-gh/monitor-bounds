using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using ScreenRegionProtector.Models;

namespace ScreenRegionProtector.Views
{
    // Interaction logic for ApplicationEditorWindow.xaml
    public partial class ApplicationEditorWindow : Window
    {
        private readonly ApplicationWindow _application;
        private Form _pickerForm;


        // Gets the edited application

        public ApplicationWindow Application => _application;


        // Gets whether the dialog was confirmed

        public new bool DialogResult { get; private set; }


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

            DialogResult = true;
            Close();
        }


        // Handles the Cancel button click
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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
    }
} 