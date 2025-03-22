using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;

namespace ScreenRegionProtector.Converters
{
    // Helper for common converter operations
    internal static class ConverterHelper
    {
        public static bool ToBool(object value)
        {
            if (value is bool b)
                return b;
            return value != null && bool.TryParse(value.ToString(), out bool result) && result;
        }
    }

    // Converts a boolean value to a Visibility value
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = (parameter as string)?.Trim().Equals("invert", StringComparison.OrdinalIgnoreCase) == true;
            bool boolValue = ConverterHelper.ToBool(value);

            if (invert)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = (parameter as string)?.Trim().Equals("invert", StringComparison.OrdinalIgnoreCase) == true;
            bool result = (value is Visibility visibility) && (visibility == Visibility.Visible);

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
            bool boolValue = ConverterHelper.ToBool(value);
            string[] options = (parameter as string)?.Split(',');

            if (options?.Length == 2)
                return boolValue ? options[0].Trim() : options[1].Trim();

            return boolValue.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string stringValue = value as string;
            if (string.IsNullOrWhiteSpace(stringValue))
                return false;

            string[] options = (parameter as string)?.Split(',');
            if (options?.Length == 2)
                return stringValue.Trim().Equals(options[0].Trim(), StringComparison.OrdinalIgnoreCase);

            return bool.TryParse(stringValue, out bool result) && result;
        }
    }

    // Converts a boolean value to a Color value
    public class BoolToColorConverter : IValueConverter
    {
        private static readonly BrushConverter BrushConverter = new BrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = ConverterHelper.ToBool(value);
            string[] options = (parameter as string)?.Split(',');

            if (options?.Length == 2)
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
            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converts a color brush to a darker shade based on a provided factor (e.g., "0.2" for 20% darker)
    public class ColorShadeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush && parameter is string shade &&
                double.TryParse(shade, NumberStyles.Any, CultureInfo.InvariantCulture, out double shadeAmount))
            {
                Color originalColor = brush.Color;
                byte newR = (byte)Math.Max(0, originalColor.R - originalColor.R * shadeAmount);
                byte newG = (byte)Math.Max(0, originalColor.G - originalColor.G * shadeAmount);
                byte newB = (byte)Math.Max(0, originalColor.B - originalColor.B * shadeAmount);

                return Color.FromArgb(originalColor.A, newR, newG, newB);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Converts toggle button state to either a start or stop command
    public class ToggleButtonCommandConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // Expecting: StartCommand, StopCommand, IsChecked
            if (values.Length < 3)
                return null;

            ICommand startCommand = values[0] as ICommand;
            ICommand stopCommand = values[1] as ICommand;
            bool isChecked = ConverterHelper.ToBool(values[2]);

            return isChecked ? stopCommand : startCommand;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
