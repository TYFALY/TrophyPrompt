using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrophyPrompt.ViewModels
{
    public partial class TimestampDialogViewModel : ViewModelBase
    {
        [ObservableProperty]
        private DateTime _value = DateTime.Now;

        [ObservableProperty]
        private DateTime _minTime = new DateTime(2008, 1, 1);

        [ObservableProperty]
        private string _title = "Pick time";

        [ObservableProperty]
        private string _error = string.Empty;

        [ObservableProperty]
        private DateTimeOffset? _datePart = DateTime.Now;

        [ObservableProperty]
        private TimeSpan? _timePart = DateTime.Now.TimeOfDay;

        partial void OnValueChanged(DateTime value)
        {
            DatePart = new DateTimeOffset(value);
            TimePart = value.TimeOfDay;
        }

        partial void OnDatePartChanged(DateTimeOffset? value)
        {
            if (value.HasValue)
            {
                DateTime d = value.Value.DateTime;
                TimeSpan t = TimePart ?? Value.TimeOfDay;
                Value = new DateTime(d.Year, d.Month, d.Day, t.Hours, t.Minutes, t.Seconds);
            }
        }

        partial void OnTimePartChanged(TimeSpan? value)
        {
            if (value.HasValue)
            {
                DateTime d = Value;
                TimeSpan t = value.Value;
                Value = new DateTime(d.Year, d.Month, d.Day, t.Hours, t.Minutes, t.Seconds);
            }
        }

        public bool Accepted { get; set; }

        public bool TryAccept()
        {
            if (DateTime.Compare(MinTime, Value) > 0)
            {
                Error = "The last trophy synchronized with PSN has the following date: " + MinTime.ToString("yyyy/M/dd  HH:mm:ss") + ". Select a date greater than this.";
                return false;
            }
            Error = string.Empty;
            Accepted = true;
            return true;
        }
    }
}
