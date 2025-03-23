using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms; // For ColorDialog
using MonitorBounds.Models;

namespace MonitorBounds.Views
{
    
    // Interaction logic for RegionEditorWindow.xaml
    
    public partial class RegionEditorWindow : Window
    {
        private readonly ScreenRegion _region;


        // Gets the edited region

        public ScreenRegion Region => _region;


        // Gets whether the dialog was confirmed

        public new bool DialogResult { get; private set; }


        // Creates a new instance of the RegionEditorWindow for editing an existing region

        public RegionEditorWindow(ScreenRegion region)
        {
            InitializeComponent();
            
            // Create a copy of the region to edit
            _region = new ScreenRegion
            {
                Name = region.Name,
                Left = region.Left,
                Top = region.Top,
                Width = region.Width,
                Height = region.Height,
                HighlightColor = region.HighlightColor,
                HighlightOpacity = region.HighlightOpacity,
                IsActive = region.IsActive
            };
            
            DataContext = _region;
        }


        // Creates a new instance of the RegionEditorWindow for creating a new region

        public RegionEditorWindow()
        {
            InitializeComponent();
            
            // Create a new region with default values
            _region = new ScreenRegion
            {
                Name = "New Protected Region",
                Left = 0,
                Top = 0,
                Width = 200,
                Height = 200,
                HighlightColor = Colors.Red,
                HighlightOpacity = 0.2,
                IsActive = true
            };
            
            DataContext = _region;
        }


        // Handles the OK button click

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(_region.Name))
            {
                System.Windows.MessageBox.Show("Please enter a name for the region.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_region.Width <= 0 || _region.Height <= 0)
            {
                System.Windows.MessageBox.Show("Width and height must be greater than zero.", "Validation Error", 
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

        // Handles the change color button click
        private void ChangeColorButton_Click(object sender, RoutedEventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                // Set the initial color to match the current highlight color
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    _region.HighlightColor.A,
                    _region.HighlightColor.R,
                    _region.HighlightColor.G,
                    _region.HighlightColor.B);

                // Show the dialog
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Convert the selected color to a WPF color
                    _region.HighlightColor = System.Windows.Media.Color.FromArgb(
                        colorDialog.Color.A,
                        colorDialog.Color.R,
                        colorDialog.Color.G,
                        colorDialog.Color.B);
                }
            }
        }
    }
} 