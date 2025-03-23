using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Drawing;

namespace MonitorBounds.Models
{
    public class ScreenRegion : INotifyPropertyChanged
    {
        private string _name = "Protected Region";
        private int _left, _top, _width, _height;
        private System.Windows.Media.Color _highlightColor = Colors.Red;
        private double _highlightOpacity = 0.2;
        private bool _isActive = true;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Left
        {
            get => _left;
            set => SetProperty(ref _left, value, nameof(Right));
        }

        public int Top
        {
            get => _top;
            set => SetProperty(ref _top, value, nameof(Bottom));
        }

        public int Width
        {
            get => _width;
            set => SetProperty(ref _width, value, nameof(Right));
        }

        public int Height
        {
            get => _height;
            set => SetProperty(ref _height, value, nameof(Bottom));
        }

        public int Right => _left + _width;
        public int Bottom => _top + _height;

        public System.Windows.Media.Color HighlightColor
        {
            get => _highlightColor;
            set => SetProperty(ref _highlightColor, value);
        }

        public double HighlightOpacity
        {
            get => _highlightOpacity;
            set => SetProperty(ref _highlightOpacity, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool ContainsPoint(System.Windows.Point point) =>
            point.X >= _left && point.X <= Right &&
            point.Y >= _top && point.Y <= Bottom;

        public bool IntersectsWith(WindowsAPI.RECT rect) =>
            rect.Right >= _left && rect.Left <= Right &&
            rect.Bottom >= _top && rect.Top <= Bottom;

        public System.Windows.Point GetSafePosition(WindowsAPI.RECT windowRect)
        {
            if (!IntersectsWith(windowRect))
                return new System.Windows.Point(windowRect.Left, windowRect.Top);

            int leftOverlap = Right - windowRect.Left;
            int rightOverlap = windowRect.Right - _left;
            int topOverlap = Bottom - windowRect.Top;
            int bottomOverlap = windowRect.Bottom - _top;

            int minOverlap = Math.Min(Math.Min(leftOverlap, rightOverlap), Math.Min(topOverlap, bottomOverlap));
            const int safetyMargin = 5;

            return minOverlap == leftOverlap ? new System.Windows.Point(Right + safetyMargin, windowRect.Top) :
                   minOverlap == rightOverlap ? new System.Windows.Point(_left - windowRect.Width - safetyMargin, windowRect.Top) :
                   minOverlap == topOverlap ? new System.Windows.Point(windowRect.Left, Bottom + safetyMargin) :
                   new System.Windows.Point(windowRect.Left, _top - windowRect.Height - safetyMargin);
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (!Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}