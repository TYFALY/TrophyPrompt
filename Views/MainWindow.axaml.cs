using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TrophyPrompt.ViewModels;

namespace TrophyPrompt.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel Vm
        {
            get { return (MainViewModel)DataContext; }
        }

        private bool _closingAfterPrompt;

        public MainWindow()
        {
            InitializeComponent();
            MainViewModel vm = new MainViewModel();
            vm.HostWindow = this;
            DataContext = vm;
            vm.EditTimeRequestedHandler = (row) => { _ = OpenEditTimeDialogAsync(row); };
            vm.ConfirmHandler = ConfirmYesNoAsync;
            vm.AlertHandler = AlertAsync;

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, OnDropFiles);
            Closing += OnWindowClosing;
        }

        private async void OnDropFiles(object sender, DragEventArgs e)
        {
            IEnumerable<IStorageItem> items = e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>();
            string first = items.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            if (!string.IsNullOrEmpty(first) && Directory.Exists(first))
            {
                await Vm.LoadTrophyDirectoryAsync(first);
            }
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private DateTime SyncFloor()
        {
            DateTime s = Vm.LastSyncTime;
            return s.Year < 2008 ? new DateTime(2008, 1, 1) : s;
        }

        private async void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            if (_closingAfterPrompt)
            {
                Vm.Shutdown();
                return;
            }
            if (Vm.HasUnsavedChanges)
            {
                e.Cancel = true;
                int choice = await ConfirmAsync("Unsaved changes", "Save changes before closing?", new string[] { "Save", "Discard", "Cancel" });
                if (choice == 2)
                {
                    return;
                }
                // A failed save must not close the window and lose the edits.
                if (choice == 0 && !await Vm.SaveFilesAsync())
                {
                    return;
                }
                _closingAfterPrompt = true;
                Close();
            }
            else
            {
                // Removes the decrypted temp copy of the trophy folder.
                Vm.Shutdown();
            }
        }

        private async void OnBatchTimeClick(object sender, RoutedEventArgs e)
        {
            TimestampDialogViewModel tvm = new TimestampDialogViewModel();
            tvm.Title = "Batch custom time";
            tvm.Value = Vm.BatchCustomTime;
            tvm.MinTime = SyncFloor();
            bool? ok = await ShowTimestampDialogAsync(tvm);
            if (ok == true)
            {
                Vm.BatchCustomTime = tvm.Value;
                if (Vm.BatchSetCustomTimeCommand.CanExecute(null))
                {
                    Vm.BatchSetCustomTimeCommand.Execute(null);
                }
            }
        }

        private async void OnEditTimeClick(object sender, RoutedEventArgs e)
        {
            if (Vm.SelectedTrophy != null)
            {
                await OpenEditTimeDialogAsync(Vm.SelectedTrophy);
            }
        }

        private async Task OpenEditTimeDialogAsync(TrophyItemViewModel row)
        {
            TimestampDialogViewModel tvm = new TimestampDialogViewModel();
            tvm.Title = "Edit time — " + row.Name;
            tvm.Value = row.IsUnlocked && row.Timestamp.Ticks > 0 ? row.Timestamp : DateTime.Now;
            tvm.MinTime = SyncFloor();
            bool? ok = await ShowTimestampDialogAsync(tvm);
            if (ok == true)
            {
                string error;
                if (!Vm.ApplyEditTime(row, tvm.Value, out error) && error != null)
                {
                    await AlertAsync("Cannot update trophy", error);
                }
            }
        }

        private async Task<bool?> ShowTimestampDialogAsync(TimestampDialogViewModel tvm)
        {
            TimestampDialog dlg = new TimestampDialog();
            dlg.DataContext = tvm;
            return await dlg.ShowDialog<bool?>(this);
        }

        private void OnGridDoubleTapped(object sender, TappedEventArgs e)
        {
            if (Vm.ToggleUnlockSelectedCommand.CanExecute(null))
            {
                Vm.ToggleUnlockSelectedCommand.Execute(null);
            }
        }

        private void OnLoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is TrophyItemViewModel row)
            {
                if (row.RowBrushKey == "Locked")
                {
                    e.Row.Background = new SolidColorBrush(Color.Parse("#2A1619"));
                    e.Row.Foreground = new SolidColorBrush(Color.Parse("#FF7B72"));
                }
                else if (row.RowBrushKey == "Synced")
                {
                    e.Row.Background = new SolidColorBrush(Color.Parse("#12261E"));
                    e.Row.Foreground = new SolidColorBrush(Color.Parse("#7EE787"));
                }
                else
                {
                    e.Row.Background = new SolidColorBrush(Color.Parse("#161B22"));
                    e.Row.Foreground = new SolidColorBrush(Color.Parse("#E6EDF3"));
                }
            }
        }

        private async Task<bool> ConfirmYesNoAsync(string title, string text)
        {
            int choice = await ConfirmAsync(title, text, new string[] { "Yes", "No" });
            return choice == 0;
        }

        private Task AlertAsync(string title, string text)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Window w = new Window();
            w.Title = title;
            w.Width = 420;
            w.Height = 180;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(16);
            panel.Spacing = 12;
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.TextWrapping = TextWrapping.Wrap;
            Button ok = new Button();
            ok.Content = "OK";
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            ok.Click += (s, ev) => { w.Close(); tcs.SetResult(true); };
            panel.Children.Add(tb);
            panel.Children.Add(ok);
            w.Content = panel;
            _ = w.ShowDialog(this).ContinueWith(_ => tcs.TrySetResult(true));
            return tcs.Task;
        }

        private Task<int> ConfirmAsync(string title, string text, string[] options)
        {
            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            Window w = new Window();
            w.Title = title;
            w.Width = 420;
            w.Height = 180;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(16);
            panel.Spacing = 12;
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.TextWrapping = TextWrapping.Wrap;
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Spacing = 8;
            row.HorizontalAlignment = HorizontalAlignment.Right;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                Button b = new Button();
                b.Content = options[i];
                b.Click += (s, ev) => { w.Close(); tcs.TrySetResult(idx); };
                row.Children.Add(b);
            }
            panel.Children.Add(tb);
            panel.Children.Add(row);
            w.Content = panel;
            w.Closed += (s, ev) => tcs.TrySetResult(options.Length - 1);
            _ = w.ShowDialog(this);
            return tcs.Task;
        }
    }
}
