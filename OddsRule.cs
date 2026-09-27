using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SimpleRollCall
{
    /// <summary>
    /// One row of the odds (爆率) table: which person is drawn with which weight.
    ///
    /// The weight drives the draw immediately (the rule is read from
    /// <see cref="AppConfig.OddsRules"/> on every draw):
    ///   0   - the person is never drawn,
    ///   50  - normal weight (the default),
    ///   100 - the person is always drawn,
    ///   1-99 - drawn proportionally to the value.
    /// </summary>
    public class OddsRule : INotifyPropertyChanged
    {
        /// <summary>The weight that means "draw normally".</summary>
        public const int NormalWeight = 50;

        private string _name = "";
        private int _weight = NormalWeight;
        private bool _enabled = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Person the rule applies to; unknown names are simply ignored by the draw.</summary>
        public string Name
        {
            get => _name;
            set
            {
                var name = value ?? "";
                if (_name == name)
                    return;
                _name = name;
                OnPropertyChanged();
            }
        }

        /// <summary>Weight of the rule, always clamped to the 0..100 range.</summary>
        public int Weight
        {
            get => _weight;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (_weight == clamped)
                    return;
                _weight = clamped;
                OnPropertyChanged();
                RepaintWeightText();
            }
        }

        /// <summary>
        /// Weight as the user types it. An empty (or unparsable) cell falls back to 50
        /// and an out of range value is clamped, then the normalised text is pushed
        /// back to the cell so the grid always shows the value that is really in effect.
        /// </summary>
        public string WeightText
        {
            get => _weight.ToString(CultureInfo.InvariantCulture);
            set
            {
                var text = value?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    Weight = NormalWeight;
                }
                else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    Weight = parsed; // clamped to 0..100
                }
                else
                {
                    Weight = NormalWeight;
                }

                RepaintWeightText(); // repaint the normalised value
            }
        }

        /// <summary>Whether the rule takes part in the draw at all.</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Notifies the weight cell about the value that is really in effect.
        ///
        /// The number box rewrites its own inner text box while it pushes a value
        /// through this binding (its internal guard swallows an answer arriving in
        /// that moment), so the notification is scheduled right after the current
        /// update instead of being raised inline. The model itself - and therefore
        /// the draw and the saved config - is already final at this point; only the
        /// repaint of the cell is one dispatcher tick late.
        /// </summary>
        private void RepaintWeightText()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                OnPropertyChanged(nameof(WeightText));
                return;
            }

            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => OnPropertyChanged(nameof(WeightText))));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
