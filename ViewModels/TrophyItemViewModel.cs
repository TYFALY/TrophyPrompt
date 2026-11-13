using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TROPHYParser;

namespace TrophyPrompt.ViewModels
{
    public partial class TrophyItemViewModel : ViewModelBase
    {
        [ObservableProperty]
        private int _id;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _detail = string.Empty;

        [ObservableProperty]
        private string _typeCode = string.Empty;

        [ObservableProperty]
        private TropType _trophyType;

        [ObservableProperty]
        private bool _isHidden;

        [ObservableProperty]
        private bool _isUnlocked;

        [ObservableProperty]
        private bool _isSync;

        [ObservableProperty]
        private DateTime _timestamp = DateTime.MinValue;

        [ObservableProperty]
        private string _timestampText = string.Empty;

        [ObservableProperty]
        private string _group = "BaseGame";

        [ObservableProperty]
        private Bitmap _trophyIcon;

        [ObservableProperty]
        private string _rowBrushKey = "Locked";

        public Action<TrophyItemViewModel> ToggleRequested { get; set; }

        public Action<TrophyItemViewModel> EditTimeRequested { get; set; }

        [RelayCommand]
        private void Toggle()
        {
            if (ToggleRequested != null)
            {
                ToggleRequested(this);
            }
        }

        [RelayCommand]
        private void EditTime()
        {
            if (EditTimeRequested != null)
            {
                EditTimeRequested(this);
            }
        }

        public string TypeBadgeText
        {
            get
            {
                switch (TrophyType)
                {
                    case TropType.Platinum: return "P";
                    case TropType.Gold: return "G";
                    case TropType.Silver: return "S";
                    default: return "B";
                }
            }
        }

        public IBrush TypeBadgeBrush
        {
            get
            {
                switch (TrophyType)
                {
                    case TropType.Platinum: return new SolidColorBrush(Color.Parse("#E5E4E2"));
                    case TropType.Gold: return new SolidColorBrush(Color.Parse("#FFD700"));
                    case TropType.Silver: return new SolidColorBrush(Color.Parse("#C0C0C0"));
                    default: return new SolidColorBrush(Color.Parse("#CD7F32"));
                }
            }
        }

        public string TypeName
        {
            get { return TrophyType.ToString(); }
        }

        public string TypeStr
        {
            get { return TypeName; }
        }

        public Bitmap IconImage
        {
            get { return TrophyIcon; }
        }

        public string TimeString
        {
            get { return TimestampText; }
        }

        // Avalonia bitmaps hold unmanaged memory; release it when the row is discarded.
        public void ReleaseIcon()
        {
            SetIcon(null);
        }

        // Takes ownership of the bitmap and disposes the one it replaces, so
        // replacing an icon in place never leaks the old bitmap's native memory.
        public void SetIcon(Bitmap icon)
        {
            Bitmap old = TrophyIcon;
            if (ReferenceEquals(old, icon))
            {
                return;
            }
            TrophyIcon = icon;
            OnPropertyChanged(nameof(IconImage));
            old?.Dispose();
        }

        public void SetIconFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }
            Bitmap icon = null;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    icon = new Bitmap(ms);
                }
            }
            catch
            {
                // Corrupt PNG: leave the row without an icon, grid shows fallback.
            }
            SetIcon(icon);
        }

        public void RefreshDerived()
        {
            OnPropertyChanged(nameof(TypeBadgeText));
            OnPropertyChanged(nameof(TypeBadgeBrush));
            OnPropertyChanged(nameof(TypeName));
            OnPropertyChanged(nameof(TypeStr));
            OnPropertyChanged(nameof(HiddenText));
            OnPropertyChanged(nameof(SyncText));
            TimestampText = IsUnlocked && Timestamp.Ticks > 0
                ? Timestamp.ToString("yyyy/M/dd  HH:mm:ss")
                : string.Empty;
            OnPropertyChanged(nameof(TimeString));
            OnPropertyChanged(nameof(IconImage));
            RowBrushKey = IsSync ? "Synced" : (IsUnlocked ? "Unlocked" : "Locked");
        }

        public string HiddenText
        {
            get { return IsHidden ? "yes" : "no"; }
        }

        public string SyncText
        {
            get { return IsSync ? "yes" : "no"; }
        }
    }
}
