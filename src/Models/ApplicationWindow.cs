using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.IO.Enumeration;

namespace MonitorBounds.Models
{
    // Represents an application window that is tracked by the system
    public class ApplicationWindow : INotifyPropertyChanged, IEquatable<ApplicationWindow>
    {
        private string _titlePattern = "*";
        private bool _isActive = true;
        private IntPtr _handle = IntPtr.Zero;
        private int? _restrictToMonitor = null;

        // Cached PropertyChangedEventArgs instances to reduce memory allocations
        private static readonly PropertyChangedEventArgs TitlePatternChangedEventArgs = new PropertyChangedEventArgs(nameof(TitlePattern));
        private static readonly PropertyChangedEventArgs IsActiveChangedEventArgs = new PropertyChangedEventArgs(nameof(IsActive));
        private static readonly PropertyChangedEventArgs HandleChangedEventArgs = new PropertyChangedEventArgs(nameof(Handle));
        private static readonly PropertyChangedEventArgs RestrictToMonitorChangedEventArgs = new PropertyChangedEventArgs(nameof(RestrictToMonitor));
        private static readonly PropertyChangedEventArgs IsSpecificWindowChangedEventArgs = new PropertyChangedEventArgs(nameof(IsSpecificWindow));

        public event PropertyChangedEventHandler PropertyChanged;

        // The pattern to match window titles against (supports * and ? wildcards)
        public string TitlePattern
        {
            get => _titlePattern;
            set
            {
                if (_titlePattern != value)
                {
                    _titlePattern = value;
                    OnPropertyChanged(TitlePatternChangedEventArgs);
                    OnPropertyChanged(IsSpecificWindowChangedEventArgs);
                }
            }
        }

        // Whether this application rule is enabled
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(IsActiveChangedEventArgs);
                }
            }
        }

        // The handle to a specific window instance (if applicable)
        [JsonIgnore]
        public IntPtr Handle
        {
            get => _handle;
            set
            {
                if (_handle != value)
                {
                    _handle = value;
                    OnPropertyChanged(HandleChangedEventArgs);
                    OnPropertyChanged(IsSpecificWindowChangedEventArgs);
                }
            }
        }

        // The monitor index that this application should be restricted to (null for no restriction)
        public int? RestrictToMonitor
        {
            get => _restrictToMonitor;
            set
            {
                if (_restrictToMonitor != value)
                {
                    _restrictToMonitor = value;
                    OnPropertyChanged(RestrictToMonitorChangedEventArgs);
                }
            }
        }

        // Gets whether this refers to a specific window instance rather than a pattern
        public bool IsSpecificWindow => Handle != IntPtr.Zero;

        // Checks if a window matches this application's criteria
        public bool Matches(IntPtr windowHandle, string windowTitle)
        {
            // If no title is provided, can't match
            if (string.IsNullOrWhiteSpace(windowTitle))
                return false;

            // If targeting a specific window, check the handle
            if (IsSpecificWindow)
                return Handle == windowHandle;

            // Otherwise, match by title pattern (case-insensitive)
            return MatchesPattern(windowTitle, TitlePattern);
        }

        // Checks if a window title matches the given pattern (supports * and ? wildcards)
        private bool MatchesPattern(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            // Use FileSystemName.MatchesSimpleExpression for wildcard matching
            return FileSystemName.MatchesSimpleExpression(pattern, text, ignoreCase: true);
        }

        // Notify that a property value has changed
        protected void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }

        // Implement IEquatable<ApplicationWindow>
        public bool Equals(ApplicationWindow other)
        {
            if (other == null)
                return false;

            return TitlePattern == other.TitlePattern &&
                   IsActive == other.IsActive &&
                   RestrictToMonitor == other.RestrictToMonitor;
        }

        // Override Equals for proper collection functionality
        public override bool Equals(object obj)
        {
            return Equals(obj as ApplicationWindow);
        }

        // Override GetHashCode to be consistent with Equals
        public override int GetHashCode()
        {
            int hash = TitlePattern?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ IsActive.GetHashCode();
            hash = (hash * 397) ^ (RestrictToMonitor?.GetHashCode() ?? 0);
            return hash;
        }

        // Override == and != operators
        public static bool operator ==(ApplicationWindow left, ApplicationWindow right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left is null || right is null)
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(ApplicationWindow left, ApplicationWindow right)
        {
            return !(left == right);
        }

        // For better debugging
        public override string ToString()
        {
            return $"Application[Title='{TitlePattern}', Active={IsActive}, Monitor={RestrictToMonitor}]";
        }
    }
}
