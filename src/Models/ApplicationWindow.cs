using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ScreenRegionProtector.Models
{
    
    // Represents an application window that is tracked by the system
    
    public class ApplicationWindow : INotifyPropertyChanged
    {
        private string _titlePattern = "*";
        private bool _isActive = true;
        private IntPtr _handle = IntPtr.Zero;
        private int? _restrictToMonitor = null;

        public event PropertyChangedEventHandler PropertyChanged;


        // The pattern to match window titles against (supports * wildcard)

        public string TitlePattern
        {
            get => _titlePattern;
            set
            {
                if (_titlePattern != value)
                {
                    _titlePattern = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(IsSpecificWindow));
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
                    NotifyPropertyChanged();
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
                    NotifyPropertyChanged();
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
                    NotifyPropertyChanged();
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

            // If we're targeting a specific window, check the handle
            if (IsSpecificWindow)
            {
                return Handle == windowHandle;
            }

            // Otherwise, match by title pattern (case-insensitive)
            bool matches = MatchesPattern(windowTitle, TitlePattern);
            
            // Log matches for debugging
            if (matches)
            {
                System.Diagnostics.Debug.WriteLine($"Window '{windowTitle}' matched pattern '{TitlePattern}'");
            }
            
            return matches;
        }


        // Checks if a window title matches the given pattern (supporting * wildcard)

        private bool MatchesPattern(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            if (pattern == "*")
                return true;

            // Convert to lowercase for case-insensitive comparison
            text = text.ToLowerInvariant();
            pattern = pattern.ToLowerInvariant();

            // Very simple wildcard matching - could be improved for more complex patterns
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                string inner = pattern.Substring(1, pattern.Length - 2);
                return !string.IsNullOrEmpty(inner) && text.Contains(inner);
            }
            else if (pattern.StartsWith("*"))
            {
                string end = pattern.Substring(1);
                return text.EndsWith(end);
            }
            else if (pattern.EndsWith("*"))
            {
                string start = pattern.Substring(0, pattern.Length - 1);
                return text.StartsWith(start);
            }
            else
            {
                // Exact match check
                return text.Equals(pattern);
            }
        }


        // Notify that a property value has changed

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 