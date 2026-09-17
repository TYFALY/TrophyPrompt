using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TrophyPrompt.Core;
using TrophyPrompt.ViewModels;

namespace TrophyPrompt.Views
{
    public partial class TimestampDialog : Window
    {
        private readonly Random _rand = new Random();
        private TimestampDialogViewModel Vm
        {
            get { return (TimestampDialogViewModel)DataContext; }
        }

        public TimestampDialog()
        {
            InitializeComponent();
        }

        private void OnRandomClick(object sender, RoutedEventArgs e)
        {
            DateTime from = Vm.MinTime.Ticks > 0 ? Vm.MinTime : new DateTime(2008, 1, 1);
            Vm.Value = new DateTime(TrophyUtility.LongRandom(from.Ticks, DateTime.Now.Ticks, _rand));
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (Vm.TryAccept())
            {
                Close(true);
            }
        }
    }
}
