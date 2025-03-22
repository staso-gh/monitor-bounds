using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ScreenRegionProtector.Models
{
    
    // Represents a protected region on the screen where windows cannot be moved
    
    public class ScreenRegion : INotifyPropertyChanged
    {
        private string _name = "Protected Region";
        private int _left;
        private int _top;
        private int _width;
        private int _height;
        private System.Windows.Media.Color _highlightColor = System.Windows.Media.Colors.Red;
        private double _highlightOpacity = 0.2;
        private bool _isActive = true;
        
        public event PropertyChangedEventHandler PropertyChanged;


        // The name of the region for user identification

        public string Name 
        { 
            get => _name; 
            set 
            { 
                if (_name != value)
                {
                    _name = value;
                    NotifyPropertyChanged();
                }
            }
        }
        

        // The left edge of the protected region in screen coordinates

        public int Left 
        { 
            get => _left; 
            set 
            { 
                if (_left != value)
                {
                    _left = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(Right));
                }
            }
        }
        

        // The top edge of the protected region in screen coordinates

        public int Top 
        { 
            get => _top; 
            set 
            { 
                if (_top != value)
                {
                    _top = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(Bottom));
                }
            }
        }
        

        // The width of the protected region

        public int Width 
        { 
            get => _width; 
            set 
            { 
                if (_width != value)
                {
                    _width = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(Right));
                }
            }
        }
        

        // The height of the protected region

        public int Height 
        { 
            get => _height; 
            set 
            { 
                if (_height != value)
                {
                    _height = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(Bottom));
                }
            }
        }
        

        // Gets the right edge of the protected region

        public int Right => Left + Width;
        

        // Gets the bottom edge of the protected region

        public int Bottom => Top + Height;
        

        // The color used to highlight this region

        public System.Windows.Media.Color HighlightColor 
        { 
            get => _highlightColor; 
            set 
            { 
                if (_highlightColor != value)
                {
                    _highlightColor = value;
                    NotifyPropertyChanged();
                }
            }
        }
        

        // The transparency level of the highlight (0.0 to 1.0)

        public double HighlightOpacity 
        { 
            get => _highlightOpacity; 
            set 
            { 
                if (_highlightOpacity != value)
                {
                    _highlightOpacity = value;
                    NotifyPropertyChanged();
                }
            }
        }
        

        // Determines if the protected region is active

        public bool IsActive 
        { 
            get => _isActive; 
            set 
            { 
                if (_isActive != value)
                {
                    _isActive = value;
                    NotifyPropertyChanged();
                }
            }
        }
        

        // Determines if a point is inside this protected region

        public bool ContainsPoint(System.Windows.Point point)
        {
            return point.X >= Left && point.X <= Right &&
                   point.Y >= Top && point.Y <= Bottom;
        }
        

        // Determines if a rectangle intersects with this protected region

        public bool IntersectsWith(WindowsAPI.RECT rect)
        {
            // Check if any part of the rectangle is inside the region
            return !(rect.Right < Left || rect.Left > Right ||
                    rect.Bottom < Top || rect.Top > Bottom);
        }
        

        // Gets the safe position closest to the current position that doesn't intersect the protected region

        public System.Windows.Point GetSafePosition(WindowsAPI.RECT windowRect)
        {
            // If not intersecting, return the current position
            if (!IntersectsWith(windowRect))
            {
                return new System.Windows.Point(windowRect.Left, windowRect.Top);
            }
            
            // Determine the best direction to move the window
            // Calculate the overlap on each side
            int leftOverlap = Right - windowRect.Left;
            int rightOverlap = windowRect.Right - Left;
            int topOverlap = Bottom - windowRect.Top;
            int bottomOverlap = windowRect.Bottom - Top;
            
            // Find the smallest overlap
            int minOverlap = Math.Min(Math.Min(leftOverlap, rightOverlap), 
                                Math.Min(topOverlap, bottomOverlap));
            
            // Add a small margin to ensure the window is completely outside the protected region
            const int safetyMargin = 5;
            
            // Move the window in the direction of smallest overlap
            if (minOverlap == leftOverlap)
            {
                return new System.Windows.Point(Right + safetyMargin, windowRect.Top);
            }
            else if (minOverlap == rightOverlap)
            {
                return new System.Windows.Point(Left - windowRect.Width - safetyMargin, windowRect.Top);
            }
            else if (minOverlap == topOverlap)
            {
                return new System.Windows.Point(windowRect.Left, Bottom + safetyMargin);
            }
            else // bottomOverlap
            {
                return new System.Windows.Point(windowRect.Left, Top - windowRect.Height - safetyMargin);
            }
        }


        // Notify that a property value has changed

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 