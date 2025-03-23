#nullable enable
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;

namespace MonitorBounds.Converters
{
    // Helper for common converter operations
    internal static class ConverterHelper
    {
        public static bool ToBool(object? value)
        {
            return value switch
            {
                bool b => b,
                null => false,
                _ => bool.TryParse(value.ToString(), out bool result) && result
            };
        }
    }

    // Converts a boolean value to a Visibility value
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        private static readonly BoolToVisibilityConverter _instance = new();
        public static BoolToVisibilityConverter Instance => _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter is string paramStr && 
                          paramStr.Trim().Equals("invert", StringComparison.OrdinalIgnoreCase);
            bool boolValue = ConverterHelper.ToBool(value);

            if (invert)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter is string paramStr && 
                          paramStr.Trim().Equals("invert", StringComparison.OrdinalIgnoreCase);
            bool result = value is Visibility visibility && visibility == Visibility.Visible;

            if (invert)
                result = !result;

            return result;
        }
    }

    // Converts a boolean value to a string value
    [ValueConversion(typeof(bool), typeof(string))]
    public sealed class BoolToStringConverter : IValueConverter
    {
        private static readonly BoolToStringConverter _instance = new();
        public static BoolToStringConverter Instance => _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = ConverterHelper.ToBool(value);
            
            if (parameter is string paramStr)
            {
                string[] options = paramStr.Split(',');
                if (options.Length >= 2)
                    return boolValue ? options[0].Trim() : options[1].Trim();
            }

            return boolValue.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
                return false;

            if (parameter is string paramStr)
            {
                string[] options = paramStr.Split(',');
                if (options.Length >= 2)
                    return stringValue.Trim().Equals(options[0].Trim(), StringComparison.OrdinalIgnoreCase);
            }

            return bool.TryParse(stringValue, out bool result) && result;
        }
    }

    // Converts a boolean value to a Color value
    [ValueConversion(typeof(bool), typeof(Color))]
    public sealed class BoolToColorConverter : IValueConverter
    {
        private static readonly BoolToColorConverter _instance = new();
        public static BoolToColorConverter Instance => _instance;
        
        private static readonly BrushConverter BrushConverter = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = ConverterHelper.ToBool(value);
            
            if (parameter is string paramStr)
            {
                string[] options = paramStr.Split(',');
                if (options.Length >= 2)
                {
                    string colorName = boolValue ? options[0].Trim() : options[1].Trim();
                    try
                    {
                        if (BrushConverter.ConvertFromString(colorName) is SolidColorBrush solidBrush)
                            return solidBrush.Color;
                    }
                    catch
                    {
                        return Colors.Transparent;
                    }
                }
            }
            
            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converts a color brush to a darker shade based on a provided factor
    [ValueConversion(typeof(SolidColorBrush), typeof(Color))]
    public sealed class ColorShadeConverter : IValueConverter
    {
        private static readonly ColorShadeConverter _instance = new();
        public static ColorShadeConverter Instance => _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SolidColorBrush brush || parameter is not string shade || 
                !double.TryParse(shade, NumberStyles.Any, CultureInfo.InvariantCulture, out double shadeAmount))
                return value;
            
            Color originalColor = brush.Color;
            byte newR = (byte)Math.Max(0, originalColor.R - originalColor.R * shadeAmount);
            byte newG = (byte)Math.Max(0, originalColor.G - originalColor.G * shadeAmount);
            byte newB = (byte)Math.Max(0, originalColor.B - originalColor.B * shadeAmount);

            return Color.FromArgb(originalColor.A, newR, newG, newB);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converts toggle button state to either a start or stop command
    public sealed class ToggleButtonCommandConverter : IMultiValueConverter
    {
        private static readonly ToggleButtonCommandConverter _instance = new();
        public static ToggleButtonCommandConverter Instance => _instance;

        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expecting: StartCommand, StopCommand, IsChecked
            if (values.Length < 3)
                return null;

            ICommand? startCommand = values[0] as ICommand;
            ICommand? stopCommand = values[1] as ICommand;
            bool isChecked = ConverterHelper.ToBool(values[2]);

            return isChecked ? stopCommand : startCommand;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
