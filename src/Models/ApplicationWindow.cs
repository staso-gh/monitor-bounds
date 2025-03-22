using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ScreenRegionProtector.Models
{
    
    // Represents an application window that is tracked by the system
    
    public class ApplicationWindow : INotifyPropertyChanged, IEquatable<ApplicationWindow>
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

            // The asterisk by itself matches everything
            if (pattern == "*")
                return true;

            // Convert to lowercase for case-insensitive comparison
            string lowerText = text.ToLowerInvariant();
            string lowerPattern = pattern.ToLowerInvariant();
            
            // Special case for Discord - if pattern contains Discord and window contains Discord, consider it a match
            if (lowerPattern.Contains("discord") && lowerText.Contains("discord"))
            {
                System.Diagnostics.Debug.WriteLine("Special case: Discord match detected!");
                return true;
            }

            // Handle different wildcard patterns
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                // Pattern is *text* - check if window title contains the inner text
                string inner = lowerPattern.Substring(1, lowerPattern.Length - 2);
                return !string.IsNullOrEmpty(inner) && lowerText.Contains(inner);
            }
            else if (pattern.StartsWith("*"))
            {
                // Pattern is *text - check if window title ends with text
                string end = lowerPattern.Substring(1);
                return lowerText.EndsWith(end);
            }
            else if (pattern.EndsWith("*"))
            {
                // Pattern is text* - check if window title starts with text
                string start = lowerPattern.Substring(0, lowerPattern.Length - 1);
                return lowerText.StartsWith(start);
            }
            else if (pattern.Contains("*"))
            {
                // Pattern has wildcards in the middle - split and check parts
                string[] parts = lowerPattern.Split('*');
                int currentIndex = 0;
                
                // Each part must be found in sequence
                foreach (string part in parts)
                {
                    if (string.IsNullOrEmpty(part))
                        continue;
                        
                    int index = lowerText.IndexOf(part, currentIndex);
                    if (index == -1)
                        return false;
                        
                    currentIndex = index + part.Length;
                }
                
                return true;
            }
            else
            {
                // For general patterns without wildcards, use Contains instead of exact match
                // This is more permissive but better for real-world window titles
                return lowerText.Contains(lowerPattern);
            }
        }


        // Notify that a property value has changed

        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Override Equals for proper collection functionality
        public override bool Equals(object obj)
        {
            if (obj is ApplicationWindow other)
            {
                return Equals(other);
            }
            return false;
        }

        // Implement IEquatable<ApplicationWindow>
        public bool Equals(ApplicationWindow other)
        {
            if (other == null)
                return false;
                
            // For collections, primarily compare by TitlePattern which is our key identifier
            // Include other properties as necessary for complete equality
            return TitlePattern == other.TitlePattern &&
                   IsActive == other.IsActive &&
                   RestrictToMonitor == other.RestrictToMonitor;
                   
            // Note: We intentionally exclude Handle from equality comparison
            // as it's a runtime-only property that isn't persisted
        }

        // Always override GetHashCode when overriding Equals
        public override int GetHashCode()
        {
            // Combine hash codes of the properties used in Equals
            int hash = TitlePattern?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ IsActive.GetHashCode();
            hash = (hash * 397) ^ (RestrictToMonitor?.GetHashCode() ?? 0);
            
            return hash;
        }

        // For better debugging
        public override string ToString()
        {
            return $"Application[Title='{TitlePattern}', Active={IsActive}, Monitor={RestrictToMonitor}]";
        }
    }
} 