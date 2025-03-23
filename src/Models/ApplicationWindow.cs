using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Media;

namespace MonitorBounds.Models
{
    // Represents an application window that is tracked by the system
    public class ApplicationWindow : INotifyPropertyChanged, IEquatable<ApplicationWindow>
    {
        private string _titlePattern = "*";
        private bool _isActive = true;
        private IntPtr _handle = IntPtr.Zero;
        private int? _restrictToMonitor = null;

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
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSpecificWindow));
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
                    OnPropertyChanged();
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
            bool matches = MatchesPattern(windowTitle, TitlePattern);

            // Log matches for debugging
            if (matches)
            {
            }

            return matches;
        }

        // Checks if a window title matches the given pattern (supports * and ? wildcards)
        private bool MatchesPattern(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            // Convert to lowercase for case-insensitive comparison
            string lowerText = text.ToLowerInvariant();
            string lowerPattern = pattern.ToLowerInvariant();

            // Use FileSystemName.MatchesSimpleExpression for wildcard matching
            return FileSystemName.MatchesSimpleExpression(lowerPattern, lowerText);
        }

        // Notify that a property value has changed
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
