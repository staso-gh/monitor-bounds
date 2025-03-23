using System;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorBounds.Views
{
    
    // Interaction logic for MonitorSelectorWindow.xaml
    
    public partial class MonitorSelectorWindow : Window
    {

        // Gets the selected monitor index

        public int SelectedMonitor { get; private set; } = 0;


        // Gets the dialog result (true if OK was clicked)

        public new bool? DialogResult { get; private set; }


        // Creates a new instance of the MonitorSelectorWindow

        public MonitorSelectorWindow(int monitorCount)
        {
            InitializeComponent();

            // Initialize the monitor combo box
            PopulateMonitorComboBox(monitorCount);
        }


        // Populates the monitor combo box with available monitors

        private void PopulateMonitorComboBox(int monitorCount)
        {
            MonitorComboBox.Items.Clear();

            // Get display devices using Windows API
            WindowsAPI.DISPLAY_DEVICE displayDevice = new WindowsAPI.DISPLAY_DEVICE();
            displayDevice.cb = Marshal.SizeOf(typeof(WindowsAPI.DISPLAY_DEVICE));

            for (uint i = 0; i < monitorCount; i++)
            {
                string displayName;
                
                if (WindowsAPI.EnumDisplayDevices(null, i, ref displayDevice, 0))
                {
                    displayName = string.IsNullOrEmpty(displayDevice.DeviceString)
                        ? $"Monitor {i}"
                        : $"Monitor {i} ({displayDevice.DeviceString})";
                }
                else
                {
                    displayName = $"Monitor {i}";
                }

                MonitorComboBox.Items.Add(displayName);
            }

            // Select the first monitor by default
            if (MonitorComboBox.Items.Count > 0)
            {
                MonitorComboBox.SelectedIndex = 0;
            }
        }


        // Handles the OK button click

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedMonitor = MonitorComboBox.SelectedIndex;
            
            if (SelectedMonitor < 0)
            {
                System.Windows.MessageBox.Show("Please select a monitor.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
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
    }
} 