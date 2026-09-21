using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrophyPrompt.ViewModels
{
    // Avalonia port of legacy CopyFrom form: scrape unlock timestamps from a
    // psntrophyleaders.com profile, optionally shifted (smart copy).
    public partial class CopyFromDialogViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _url = string.Empty;

        [ObservableProperty]
        private bool _useSmartCopy;

        [ObservableProperty]
        private double _years;

        [ObservableProperty]
        private double _months;

        [ObservableProperty]
        private double _days;

        [ObservableProperty]
        private double _minMinutes;

        [ObservableProperty]
        private double _maxMinutes;

        [ObservableProperty]
        private string _error = string.Empty;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        public bool TryValidate()
        {
            if (MinMinutes > MaxMinutes)
            {
                Error = "Min can't be greater than max.";
                return false;
            }
            if (!Regex.IsMatch(Url ?? string.Empty, "https://psntrophyleaders.com/user/view/" + "\\S+/\\S+"))
            {
                Error = "Can't find game. URL must look like https://psntrophyleaders.com/user/view/<user>/<game>.";
                return false;
            }
            Error = string.Empty;
            return true;
        }
    }
}
