using System;

namespace EconomicGame.Services
{
    /// <summary>
    /// Per-circuit (Scoped) navigation state. Tracks the active game view
    /// so the main sidebar and the page layout can synchronize view transitions.
    /// </summary>
    public class NavigationStateService
    {
        private string _currentView = "Market";

        public string CurrentView
        {
            get => _currentView;
            set
            {
                if (_currentView == value) return;
                _currentView = value;
                OnViewChanged?.Invoke(value);
            }
        }

        public event Action<string>? OnViewChanged;
    }
}
