using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;

namespace ScreenRegionProtector.Converters
{
    
    // Converts a boolean value to a Visibility value
    
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter as string == "invert";
            bool boolValue = false;
            
            // Safely handle null values
            if (value != null)
            {
                if (value is bool b)
                {
                    boolValue = b;
                }
                else if (bool.TryParse(value.ToString(), out bool result))
                {
                    boolValue = result;
                }
            }
            
            if (invert)
                boolValue = !boolValue;
                
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter as string == "invert";
            bool result = false;
            
            if (value != null && value is Visibility visibility)
            {
                result = visibility == Visibility.Visible;
            }
            
            if (invert)
                result = !result;
                
            return result;
        }
    }

    
    // Converts a boolean value to a string value
    
    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;
            
            // Safely handle null values
            if (value != null)
            {
                if (value is bool b)
                {
                    boolValue = b;
                }
                else if (bool.TryParse(value.ToString(), out bool result))
                {
                    boolValue = result;
                }
            }
            
            string[] options = (parameter as string)?.Split(',');
            
            if (options != null && options.Length == 2)
            {
                return boolValue ? options[0] : options[1];
            }
            
            return boolValue.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string stringValue = value as string;
            
            if (string.IsNullOrEmpty(stringValue))
            {
                return false;
            }
            
            string[] options = (parameter as string)?.Split(',');
            
            if (options != null && options.Length == 2)
            {
                return stringValue == options[0];
            }
            
            return bool.TryParse(stringValue, out bool result) && result;
        }
    }

    
    // Converts a boolean value to a Color value
    
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;
            
            // Safely handle null values
            if (value != null)
            {
                if (value is bool b)
                {
                    boolValue = b;
                }
                else if (bool.TryParse(value.ToString(), out bool result))
                {
                    boolValue = result;
                }
            }
            
            string[] options = (parameter as string)?.Split(',');
            
            if (options != null && options.Length == 2)
            {
                string colorName = boolValue ? options[0] : options[1];
                
                // Try to convert the color name to a Color
                try
                {
                    var colorConverter = new System.Windows.Media.BrushConverter();
                    var brush = (System.Windows.Media.Brush)colorConverter.ConvertFromString(colorName);
                    
                    if (brush is System.Windows.Media.SolidColorBrush solidBrush)
                    {
                        return solidBrush.Color;
                    }
                }
                catch
                {
                    // Return transparent if conversion fails
                    return Colors.Transparent;
                }
            }
            
            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Not implemented
            return false;
        }
    }

    public class ColorShadeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush && parameter is string shade)
            {
                // Parse the shade parameter (expected format: 0.2 for 20% darker)
                if (double.TryParse(shade, out double shadeAmount))
                {
                    // Get the original color
                    System.Windows.Media.Color originalColor = brush.Color;
                    
                    // Create a darker version of the color
                    System.Windows.Media.Color darkerColor = System.Windows.Media.Color.FromArgb(
                        originalColor.A,
                        (byte)Math.Max(0, originalColor.R - originalColor.R * shadeAmount),
                        (byte)Math.Max(0, originalColor.G - originalColor.G * shadeAmount),
                        (byte)Math.Max(0, originalColor.B - originalColor.B * shadeAmount)
                    );
                    
                    return darkerColor;
                }
            }
            
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    
    // Converts toggle button state to either start or stop command
    
    public class ToggleButtonCommandConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expecting: StartCommand, StopCommand, IsChecked
            if (values.Length < 3)
                return null;
            
            var startCommand = values[0] as ICommand;
            var stopCommand = values[1] as ICommand;
            bool isMonitoring = false;
            
            if (values[2] is bool monitoringState)
            {
                isMonitoring = monitoringState;
            }
            
            // Return the appropriate command based on current state
            return isMonitoring ? stopCommand : startCommand;
        }
        
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 