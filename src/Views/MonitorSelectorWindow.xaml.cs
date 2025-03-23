using System;
using System.Windows;
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

        public MonitorSelectorWindow()
        {
            InitializeComponent();

            // Initialize the monitor combo box
            PopulateMonitorComboBox();
        }


        // Populates the monitor combo box with available monitors

        private void PopulateMonitorComboBox()
        {
            MonitorComboBox.Items.Clear();

            // Get the actual monitor screens
            var screens = Screen.AllScreens;

            for (uint i = 0; i < screens.Length; i++)
            {
                // Get primary device for this screen
                WindowsAPI.DISPLAY_DEVICE displayDevice = new WindowsAPI.DISPLAY_DEVICE();
                displayDevice.cb = Marshal.SizeOf(typeof(WindowsAPI.DISPLAY_DEVICE));
                
                if (WindowsAPI.EnumDisplayDevices(null, i, ref displayDevice, 0))
                {
                    // Now try to get the actual monitor information for this adapter
                    WindowsAPI.DISPLAY_DEVICE monitorDevice = new WindowsAPI.DISPLAY_DEVICE();
                    monitorDevice.cb = Marshal.SizeOf(typeof(WindowsAPI.DISPLAY_DEVICE));
                    
                    string monitorName = string.Empty;
                    bool foundMonitor = false;
                    
                    // Loop through monitor devices connected to this adapter
                    for (uint j = 0; j < 10; j++) // Try up to 10 monitors per adapter
                    {
                        if (WindowsAPI.EnumDisplayDevices(displayDevice.DeviceName, j, ref monitorDevice, 0))
                        {
                            if (!string.IsNullOrEmpty(monitorDevice.DeviceString))
                            {
                                monitorName = monitorDevice.DeviceString;
                                foundMonitor = true;
                                break;
                            }
                        }
                    }
                    
                    string displayName;
                    if (foundMonitor)
                    {
                        displayName = $"Monitor {i} ({monitorName})";
                    }
                    else if (!string.IsNullOrEmpty(displayDevice.DeviceString))
                    {
                        // Fall back to adapter name if monitor name not found
                        displayName = $"Monitor {i} ({displayDevice.DeviceString})";
                    }
                    else
                    {
                        displayName = $"Monitor {i}";
                    }

                    MonitorComboBox.Items.Add(displayName);
                }
                else
                {
                    // If all else fails, just add a generic monitor name
                    MonitorComboBox.Items.Add($"Monitor {i}");
                }
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