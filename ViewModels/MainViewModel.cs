using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using TrophyPrompt.Core;
using TrophyPrompt.Models;
using TROPHYParser;

namespace TrophyPrompt.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private const long MINIMUM_POSSIBLE_DATE_TICKS = 633347424000000000;

        private TROPCONF _tconf;
        private TROPTRNS _tpsn;
        private TROPUSR _tusr;
        private string _path = string.Empty;
        private string _pathTemp = string.Empty;
        private bool _haveBeenEdited;
        private DateTime _ps3Time = new DateTime(MINIMUM_POSSIBLE_DATE_TICKS);
        private DateTime _lastSyncTrophyTime = new DateTime(MINIMUM_POSSIBLE_DATE_TICKS);
        private DateTime _randomEndTime = DateTime.Now;
        private bool _isOpen;
        private int _baseGameCount;
        // Set while a batch operation runs so every single-trophy edit doesn't
        // rebuild the whole grid (and re-read every PNG icon) - was O(n^2).
        private bool _suspendRebuild;
        private bool _backupDone;
        // Folder whose icons the current rows hold; rebuilds of the same folder reuse them.
        private string _iconsPath = string.Empty;

        [ObservableProperty]
        private string _lastBackupPath = string.Empty;

        // ---- Header / PSN metadata (two-way bindable) ----
        [ObservableProperty]
        private string _gameTitle = string.Empty;

        [ObservableProperty]
        private string _titleId = string.Empty;

        [ObservableProperty]
        private string _accountId = string.Empty;

        [ObservableProperty]
        private string _trophySetVersion = string.Empty;

        [ObservableProperty]
        private string _syncState = string.Empty;

        [ObservableProperty]
        private string _pfdStatus = "No folder loaded";

        [ObservableProperty]
        private string _countText = "00/00";

        [ObservableProperty]
        private string _pointsText = "000/000";

        [ObservableProperty]
        private double _progressMax = 100;

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All";

        [ObservableProperty]
        private DateTime _batchCustomTime = DateTime.Now;

        [ObservableProperty]
        private DateTime _randomStartTime = new DateTime(2008, 1, 1);

        [ObservableProperty]
        private DateTime _randomEndTimeProp = DateTime.Now;

        [ObservableProperty]
        private TrophyItemViewModel _selectedTrophy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        public ObservableCollection<TrophyItemViewModel> Trophies { get; } = new ObservableCollection<TrophyItemViewModel>();
        public ObservableCollection<TrophyItemViewModel> FilteredTrophies { get; } = new ObservableCollection<TrophyItemViewModel>();

        public List<string> FilterOptions { get; } = new List<string>
        {
            "All", "Platinum", "Gold", "Silver", "Bronze", "Unlocked", "Locked", "Hidden"
        };

        public Func<string, string, Task<bool>> ConfirmHandler { get; set; }

        public Func<string, string, Task> AlertHandler { get; set; }

        private void RaiseAlert(string title, string text)
        {
            if (AlertHandler == null)
            {
                return;
            }
            try
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        await AlertHandler(title, text);
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        private async Task<bool> ConfirmDefaultAsync(string title, string text)
        {
            if (ConfirmHandler != null)
            {
                return await ConfirmHandler(title, text);
            }
            return true;
        }

        public ObservableCollection<string> Profiles { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedProfile = "Default Profile";

        [ObservableProperty]
        private bool _isRpcs3Format;

        [ObservableProperty]
        private string _currentFolder = string.Empty;

        public Window HostWindow { get; set; }

        public bool IsOpen
        {
            get { return _isOpen; }
        }

        public DateTime LastSyncTime
        {
            get { return _lastSyncTrophyTime; }
        }

        public bool HasUnsavedChanges
        {
            get { return _haveBeenEdited; }
        }

        public MainViewModel()
        {
            Profiles.Add("Default Profile");
            try
            {
                string profilesDir = TrophyUtility.ProfilesDirectory();
                Directory.CreateDirectory(profilesDir);
                string[] files = new DirectoryInfo(profilesDir).GetFiles("*.sfo").Select(p => p.Name).ToArray();
                foreach (string f in files)
                {
                    Profiles.Add(f);
                }
            }
            catch
            {
            }
            ApplyFilter();
        }

        public async Task LoadTrophyDirectoryAsync(string path)
        {
            await OpenFolderAtAsync(path);
        }

        // ---------- File debug log (debug.log next to the exe) ----------
        private static readonly object _logLock = new object();

        public static void LogDiag(string message)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(
                        Path.Combine(AppContext.BaseDirectory, "debug.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }

        // ---------- Folder open (also bound to File menu + Ctrl+O) ----------
        [RelayCommand]
        private async Task OpenFolderAsync()
        {
            LogDiag("OpenFolderCommand fired. HostWindow null = " + (HostWindow == null) + ", IsOpen = " + _isOpen + ".");
            if (HostWindow == null)
            {
                StatusMessage = "Window is not ready yet — try again.";
                LogDiag("OpenFolderCommand aborted: HostWindow null.");
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                StatusMessage = "Window is not ready yet — try again.";
                LogDiag("OpenFolderCommand aborted: TopLevel null.");
                return;
            }
            IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select PS3 Trophy Directory (NPWR...)",
                AllowMultiple = false
            });
            if (folders == null || folders.Count == 0)
            {
                StatusMessage = "Open cancelled.";
                LogDiag("OpenFolderCommand: picker cancelled/empty.");
                return;
            }
            string path = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                path = folders[0].Path.LocalPath;
            }
            LogDiag("OpenFolderCommand: picker chose '" + path + "'.");
            await LoadTrophyDirectoryAsync(path);
        }

        [RelayCommand]
        private async Task ToggleRpcs3FormatAsync()
        {
            IsRpcs3Format = !IsRpcs3Format;
            if (_isOpen && !string.IsNullOrEmpty(_path))
            {
                await OpenFolderAtAsync(_path);
                StatusMessage = "RPCS3 trophy format " + (IsRpcs3Format ? "ON" : "OFF") + " — folder reloaded.";
            }
            else
            {
                StatusMessage = "RPCS3 trophy format " + (IsRpcs3Format ? "ON" : "OFF") + ".";
            }
        }
        // ---------- Filtering ----------
        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedProfileChanged(string value)
        {
            if (!_isOpen)
            {
                return;
            }
            UpdateCompletion();
            ApplyFilter();
            OnPropertyChanged(nameof(FilteredTrophies));
            StatusMessage = "Profile: " + (value ?? "Default Profile");
        }

        partial void OnSelectedFilterChanged(string value)
        {
            ApplyFilter();
        }

        private bool PassesFilter(TrophyItemViewModel t)
        {
            string q = SearchText != null ? SearchText.Trim().ToLowerInvariant() : string.Empty;
            if (!string.IsNullOrEmpty(q))
            {
                string hay = ((t.Name ?? string.Empty) + " " + (t.Detail ?? string.Empty)).ToLowerInvariant();
                if (!hay.Contains(q))
                {
                    return false;
                }
            }
            string f = SelectedFilter ?? "All";
            switch (f)
            {
                case "Platinum": return t.TrophyType == TropType.Platinum;
                case "Gold": return t.TrophyType == TropType.Gold;
                case "Silver": return t.TrophyType == TropType.Silver;
                case "Bronze": return t.TrophyType == TropType.Bronze;
                case "Unlocked": return t.IsUnlocked;
                case "Locked": return !t.IsUnlocked;
                case "Hidden": return t.IsHidden;
                default: return true;
            }
        }

        public void ApplyFilter()
        {
            List<TrophyItemViewModel> snapshot = Trophies.Where(PassesFilter).ToList();
            void apply()
            {
                FilteredTrophies.Clear();
                foreach (TrophyItemViewModel t in snapshot)
                {
                    FilteredTrophies.Add(t);
                }
            }
            if (Dispatcher.UIThread.CheckAccess())
            {
                apply();
            }
            else
            {
                Dispatcher.UIThread.Post(apply);
            }
        }

        // ---------- File & PFD operations ----------
        public async Task OpenFolderAtAsync(string folder)
        {
            StatusMessage = "Loading " + folder + " ...";
            LogDiag("OpenFolderAtAsync: loading '" + folder + "'.");
            if (_isOpen)
            {
                // Await the close on the UI thread BEFORE touching temp:
                // Post() returned immediately, so delete-temp raced
                // decrypt/parse and TROPCONF.SFM read back garbage.
                await Dispatcher.UIThread.InvokeAsync(() => CloseFileInternal(false));
                LogDiag("OpenFolderAtAsync: previous folder closed before reload.");
            }
            await Task.Run(() =>
            {
                string temp = null;
                try
                {
                    temp = TrophyUtility.CopyTrophyDirToTemp(folder);
                    TrophyUtility.decryptTrophy(temp);
                    TROPCONF conf = new TROPCONF(temp, IsRpcs3Format);
                    TROPTRNS trns = new TROPTRNS(temp, IsRpcs3Format);
                    TROPUSR usr = new TROPUSR(temp, IsRpcs3Format);

                    _path = folder;
                    _pathTemp = temp;
                    _tconf = conf;
                    _tpsn = trns;
                    _tusr = usr;

                    _lastSyncTrophyTime = usr.LastSyncTime;
                    if (DateTime.Compare(trns.LastSyncTime, usr.LastSyncTime) > 0)
                    {
                        _lastSyncTrophyTime = trns.LastSyncTime;
                    }
                    _ps3Time = _lastSyncTrophyTime;
                    _randomEndTime = DateTime.Now;
                    _isOpen = true;
                    _haveBeenEdited = false;
                    _backupDone = false;

                    Dispatcher.UIThread.Post(() =>
                    {
                        _isOpen = true;
                        CurrentFolder = folder;
                        RandomStartTime = _ps3Time;
                        RandomEndTimeProp = _randomEndTime;
                        BatchCustomTime = DateTime.Now;
                        RebuildTrophyList();
                        RefreshHeader();
                        PfdStatus = "Decrypted + verified (TROPTRNS.DAT)";
                        StatusMessage = "Loaded " + conf.title_name;
                        LogDiag("OpenFolderAtAsync: OK '" + conf.title_name + "' (" + conf.npcommid + "), trophies=" + _tconf.Count + ".");
                    });
                }
                catch (FileNotFoundException ex)
                {
                    _tconf = null;
                    _tpsn = null;
                    _tusr = null;
                    LogDiag("OpenFolderAtAsync FileNotFound: " + ex.Message);
                    // Don't leave decrypted copies of a failed open lying in %TEMP%.
                    DeleteTempRoot(temp);
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusMessage = "\"" + System.IO.Path.GetFileName(ex.FileName) + "\" was not found. Please, select a valid trophy folder.";
                    });
                    RaiseAlert("Open folder failed", "\"" + System.IO.Path.GetFileName(ex.FileName) + "\" was not found. Please, select a valid trophy folder.");
                }
                catch (Exception ex)
                {
                    _tconf = null;
                    _tpsn = null;
                    _tusr = null;
                    string msg = ex.Message;
                    LogDiag("OpenFolderAtAsync FAILED: " + ex.GetType().Name + ": " + msg);
                    // Don't leave decrypted copies of a failed open lying in %TEMP%.
                    DeleteTempRoot(temp);
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusMessage = msg;
                    });
                    RaiseAlert("Open folder failed", msg);
                }
            });
        }

        // Clears the lists and frees the icon bitmaps afterwards (deferred so the
        // UI has detached the rows before their images are disposed).
        private void ClearTrophies()
        {
            ClearTrophies(null);
        }

        // keepIcons: icons moved to the rebuilt rows, keyed by trophy id. Old rows
        // whose icon was moved are not disposed, the new row owns it now.
        private void ClearTrophies(Dictionary<int, Avalonia.Media.Imaging.Bitmap> keepIcons)
        {
            List<TrophyItemViewModel> old = Trophies.ToList();
            Trophies.Clear();
            FilteredTrophies.Clear();
            if (old.Count > 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (TrophyItemViewModel t in old)
                    {
                        if (keepIcons != null && t.TrophyIcon != null
                            && keepIcons.TryGetValue(t.Id, out var kept) && ReferenceEquals(kept, t.TrophyIcon))
                        {
                            continue;
                        }
                        t.ReleaseIcon();
                    }
                }, DispatcherPriority.Background);
            }
        }

        private void RebuildTrophyList()
        {
            // Every single edit rebuilds the grid; re-reading and decoding every
            // PNG each time was the dominant cost. Rows of the same open folder
            // reuse the already decoded icons instead.
            Dictionary<int, Avalonia.Media.Imaging.Bitmap> reusable = null;
            if (_tconf != null && !string.IsNullOrEmpty(_path) && _iconsPath == _path && Trophies.Count > 0)
            {
                reusable = new Dictionary<int, Avalonia.Media.Imaging.Bitmap>(Trophies.Count);
                foreach (TrophyItemViewModel t in Trophies)
                {
                    if (t.TrophyIcon != null)
                    {
                        reusable[t.Id] = t.TrophyIcon;
                    }
                }
            }
            ClearTrophies(reusable);
            _iconsPath = _path;
            if (_tconf == null || _tpsn == null || _tusr == null)
            {
                ApplyFilter();
                return;
            }
            for (int i = 0; i < _tconf.Count; i++)
            {
                TrophyItemViewModel vm = new TrophyItemViewModel();
                vm.Id = i;
                vm.Name = _tconf[i].name;
                vm.Detail = _tconf[i].detail;
                vm.TypeCode = _tconf[i].ttype;
                vm.TrophyType = (TropType)_tusr.trophyTypeTable[i].Type;
                vm.IsHidden = _tconf[i].hidden == "yes";
                if (_tpsn[i].HasValue)
                {
                    vm.IsUnlocked = true;
                    vm.IsSync = _tpsn[i].Value.IsSync;
                    vm.Timestamp = _tpsn[i].Value.Time;
                }
                else
                {
                    vm.IsUnlocked = _tusr.trophyTimeInfoTable[i].IsGet;
                    vm.IsSync = _tusr.trophyTimeInfoTable[i].IsSync;
                    vm.Timestamp = _tusr.trophyTimeInfoTable[i].Time.Ticks > 0
                        ? _tusr.trophyTimeInfoTable[i].Time
                        : DateTime.MinValue;
                }
                if (_tconf[i].gid == 0)
                {
                    vm.Group = "BaseGame";
                    _baseGameCount = i;
                }
                else
                {
                    vm.Group = "DLC" + _tconf[i].gid;
                }
                if (reusable != null && reusable.TryGetValue(i, out var cachedIcon))
                {
                    vm.SetIcon(cachedIcon);
                }
                else
                {
                    byte[] iconBytes = TrophyUtility.GetTrophyIconBytes(_path, _tconf[i].id);
                    if (iconBytes == null)
                    {
                        iconBytes = TrophyUtility.GetTrophyIconBytes(_pathTemp, _tconf[i].id);
                    }
                    vm.SetIconFromBytes(iconBytes);
                }
                vm.RefreshDerived();
                WireRowActions(vm);
                Trophies.Add(vm);
            }
            UpdateCompletion();
            ApplyFilter();
        }

        private void RefreshHeader()
        {
            if (_tconf == null)
            {
                GameTitle = string.Empty;
                TitleId = string.Empty;
                AccountId = string.Empty;
                TrophySetVersion = string.Empty;
                SyncState = string.Empty;
                return;
            }
            GameTitle = _tconf.title_name;
            TitleId = _tconf.npcommid;
            TrophySetVersion = _tconf.trophyset_version;
            AccountId = _tpsn != null ? (_tpsn.account_id ?? string.Empty).Trim('\0') : (_tusr != null ? (_tusr.account_id ?? string.Empty) : string.Empty);
            SyncState = "Last sync: " + _lastSyncTrophyTime.ToString("yyyy/M/dd  HH:mm:ss");
            if (HostWindow != null)
            {
                HostWindow.Title = "TrophyPrompt-[" + _tconf.title_name + "]";
            }
            OnPropertyChanged(nameof(IsOpen));
        }

        private void UpdateCompletion()
        {
            if (_tconf == null || _tusr == null || _tpsn == null)
            {
                ProgressMax = 100;
                ProgressValue = 0;
                CountText = "00/00";
                PointsText = "000/000";
                return;
            }
            int totalGrade = 0;
            int getGrade = 0;
            int got = 0;
            for (int i = 0; i < _tconf.Count; i++)
            {
                switch ((TropType)_tusr.trophyTypeTable[i].Type)
                {
                    case TropType.Platinum:
                        totalGrade += (int)TropGrade.Platinum;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Platinum : 0;
                        break;
                    case TropType.Gold:
                        totalGrade += (int)TropGrade.Gold;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Gold : 0;
                        break;
                    case TropType.Silver:
                        totalGrade += (int)TropGrade.Silver;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Silver : 0;
                        break;
                    case TropType.Bronze:
                        totalGrade += (int)TropGrade.Bronze;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Bronze : 0;
                        break;
                }
                if (IsTrophySync(i))
                {
                    got++;
                }
            }
            ProgressMax = totalGrade;
            ProgressValue = getGrade;
            CountText = got + "/" + _tconf.Count;
            PointsText = getGrade + "/" + totalGrade;
        }

        private bool IsTrophySync(int id)
        {
            return (_tpsn[id].HasValue && _tpsn[id].Value.IsSync) || _tusr.trophyTimeInfoTable[id].IsSync;
        }

        private bool IsTrophyGot(int id)
        {
            return (!IsRpcs3Format && _tpsn[id].HasValue) || _tusr.trophyTimeInfoTable[id].IsGet;
        }

        private int GetCountBaseTrophiesGot()
        {
            int n = 0;
            for (int i = 0; i < _tconf.trophys.Count; i++)
            {
                if (_tconf[i].gid == 0 && IsTrophyGot(i))
                {
                    n++;
                }
            }
            return n;
        }

        private bool ValidateSelectedDate(DateTime d, out string error)
        {
            if (DateTime.Compare(_lastSyncTrophyTime, d) > 0)
            {
                error = "The last trophy synchronized with PSN has the following date: " + _lastSyncTrophyTime.ToString("yyyy/M/dd  HH:mm:ss") + ". Select a date greater than this.";
                return false;
            }
            error = null;
            return true;
        }

        // Full rebuild (not in-place patch) so row colors, icons and
        // derived text refresh exactly like legacy RefreshComponents().
        private void RebuildPreservingSelection(int id)
        {
            if (_suspendRebuild)
            {
                return;
            }
            RebuildTrophyList();
            SelectedTrophy = Trophies.FirstOrDefault(t => t.Id == id);
        }

        private void SyncRowToModel(TrophyItemViewModel vm)
        {
            int id = vm.Id;
            if (_tpsn[id].HasValue)
            {
                vm.IsUnlocked = true;
                vm.IsSync = _tpsn[id].Value.IsSync;
                vm.Timestamp = _tpsn[id].Value.Time;
            }
            else
            {
                vm.IsUnlocked = _tusr.trophyTimeInfoTable[id].IsGet;
                vm.IsSync = _tusr.trophyTimeInfoTable[id].IsSync;
                vm.Timestamp = _tusr.trophyTimeInfoTable[id].Time;
            }
            vm.RefreshDerived();
        }

        // ---------- Row editing ----------
        public bool UnlockTrophy(int trophyId, DateTime time, out string error)
        {
            error = null;
            if (trophyId == 0 && _tconf.HasPlatinium && GetCountBaseTrophiesGot() < _baseGameCount)
            {
                error = "Unlock every other base-game trophy before the platinum.";
                return false;
            }
            string verr;
            if (!ValidateSelectedDate(time, out verr))
            {
                error = verr;
                return false;
            }
            try
            {
                _tpsn.PutTrophy(trophyId, _tusr.trophyTypeTable[trophyId].Type, time);
                _tusr.UnlockTrophy(trophyId, time);
                _haveBeenEdited = true;
                RebuildPreservingSelection(trophyId);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool ChangeTrophyTime(int trophyId, DateTime time, out string error)
        {
            error = null;
            if (IsTrophySync(trophyId))
            {
                error = "Trophy already synchronized. Can't be modified.";
                return false;
            }
            string verr;
            if (!ValidateSelectedDate(time, out verr))
            {
                error = verr;
                return false;
            }
            try
            {
                _tpsn.ChangeTime(trophyId, time);
                TROPUSR.TrophyTimeInfo tti = _tusr.trophyTimeInfoTable[trophyId];
                tti.Time = time;
                _tusr.trophyTimeInfoTable[trophyId] = tti;
                _haveBeenEdited = true;
                RebuildPreservingSelection(trophyId);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool LockTrophy(int trophyId, out string error)
        {
            error = null;
            if (IsTrophySync(trophyId))
            {
                error = "Trophy already synchronized. Can't be modified.";
                return false;
            }
            if (trophyId != 0 && _tconf[trophyId].gid == 0 && IsTrophyGot(0))
            {
                error = "Lock the platinum first — it can't stay unlocked without the other base-game trophies.";
                return false;
            }
            try
            {
                _tpsn.DeleteTrophyByID(trophyId);
                _tusr.LockTrophy(trophyId);
                _haveBeenEdited = true;
                RebuildPreservingSelection(trophyId);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [RelayCommand]
        private void ToggleUnlockSelected()
        {
            if (!_isOpen || SelectedTrophy == null)
            {
                return;
            }
            _ = ToggleRowAsync(SelectedTrophy);
        }

        // ---------- Batch commands ----------
        [RelayCommand]
        private void LockAll()
        {
            if (!_isOpen)
            {
                return;
            }
            TROPTRNS.TrophyInfo? ti = _tpsn.PopTrophy();
            while (ti.HasValue)
            {
                _tusr.LockTrophy(ti.Value.TrophyID);
                ti = _tpsn.PopTrophy();
            }
            _haveBeenEdited = true;
            RebuildTrophyList();
            StatusMessage = "All editable trophies locked.";
        }

        [RelayCommand]
        private async Task UnlockAllAsync()
        {
            if (!_isOpen)
            {
                return;
            }
            bool ok = await ConfirmDefaultAsync("Danger", "Instant Platinum's time will be the same, is easily identify by the PSN, we recommend manually modify time, even so do you wish to continue?");
            if (!ok)
            {
                return;
            }
            Random rand = Random.Shared;
            int i;
            for (i = 1; i < _tusr.trophyTimeInfoTable.Count && _tconf[i].gid == 0; i++)
            {
                if (!IsTrophyGot(i))
                {
                    // Same timestamp in both tables - they used to get two different
                    // random values, leaving TROPUSR and TROPTRNS out of sync.
                    DateTime t = new DateTime(TrophyUtility.LongRandom(_ps3Time.Ticks, _randomEndTime.Ticks, rand));
                    _tusr.UnlockTrophy(i, t);
                    _tpsn.PutTrophy(i, _tusr.trophyTypeTable[i].Type, t);
                }
            }
            if (!IsTrophyGot(0))
            {
                DateTime last = LastTrophyTime().AddSeconds(1);
                _tusr.UnlockTrophy(0, last);
                _tpsn.PutTrophy(0, _tusr.trophyTypeTable[0].Type, last);
            }
            for (; i < _tusr.trophyTimeInfoTable.Count; i++)
            {
                if (!IsTrophyGot(i))
                {
                    // Same timestamp in both tables - they used to get two different
                    // random values, leaving TROPUSR and TROPTRNS out of sync.
                    DateTime t = new DateTime(TrophyUtility.LongRandom(_ps3Time.Ticks, _randomEndTime.Ticks, rand));
                    _tusr.UnlockTrophy(i, t);
                    _tpsn.PutTrophy(i, _tusr.trophyTypeTable[i].Type, t);
                }
            }
            _haveBeenEdited = true;
            RebuildTrophyList();
            StatusMessage = "Instant platinum applied.";
        }

        [RelayCommand]
        private void RandomizeTimestamps()
        {
            if (!_isOpen)
            {
                return;
            }
            Random rand = Random.Shared;
            // Never generate times before the last PSN sync - those edits were
            // silently rejected, so "Randomize" often appeared to do nothing.
            DateTime start = RandomStartTime < _lastSyncTrophyTime ? _lastSyncTrophyTime : RandomStartTime;
            DateTime end = RandomEndTimeProp;
            if (end <= start)
            {
                StatusMessage = "Randomize: the end time must be after " + start.ToString("yyyy-MM-dd HH:mm:ss") + ".";
                return;
            }
            int failed = RunBatch(() =>
            {
                int f = 0;
                DateTime latest = DateTime.MinValue;
                for (int i = 0; i < Trophies.Count; i++)
                {
                    if (i == 0 && _tconf.HasPlatinium)
                    {
                        continue; // platinum is placed after everything else below
                    }
                    if (IsTrophyGot(i) && !IsTrophySync(i))
                    {
                        DateTime t = new DateTime(TrophyUtility.LongRandom(start.Ticks, end.Ticks, rand));
                        if (!ChangeTrophyTime(i, t, out _))
                        {
                            f++;
                        }
                    }
                    if (IsTrophyGot(i))
                    {
                        DateTime cur = CurrentTime(i);
                        if (cur > latest)
                        {
                            latest = cur;
                        }
                    }
                }
                // Platinum always pops right after the final trophy.
                if (_tconf.HasPlatinium && IsTrophyGot(0) && !IsTrophySync(0) && latest > DateTime.MinValue)
                {
                    if (!ChangeTrophyTime(0, latest.AddSeconds(rand.Next(1, 4)), out _))
                    {
                        f++;
                    }
                }
                return f;
            });
            StatusMessage = failed == 0 ? "Timestamps randomized." : "Timestamps randomized (" + failed + " trophies could not be changed).";
        }

        [RelayCommand]
        private void CopyTimestamps()
        {
            if (!_isOpen || Trophies.Count == 0)
            {
                return;
            }
            DateTime anchor = BatchCustomTime;
            TimeSpan step = TimeSpan.FromMinutes(5);
            RunBatch(() =>
            {
                for (int i = 0; i < Trophies.Count; i++)
                {
                    if (IsTrophyGot(i) && !IsTrophySync(i))
                    {
                        ChangeTrophyTime(i, anchor, out _);
                        anchor = anchor.Add(step);
                    }
                }
                return 0;
            });
            StatusMessage = "Timestamps copied sequentially from batch time.";
        }

        [RelayCommand]
        private void BatchSetCustomTime()
        {
            if (!_isOpen)
            {
                return;
            }
            RunBatch(() =>
            {
                // Platinum (id 0) goes last: it can only unlock after the rest.
                int count = Trophies.Count;
                foreach (int i in Enumerable.Range(1, Math.Max(0, count - 1)).Append(0))
                {
                    if (i >= count)
                    {
                        continue;
                    }
                    if (!IsTrophyGot(i))
                    {
                        UnlockTrophy(i, BatchCustomTime, out _);
                        BatchCustomTime = BatchCustomTime.AddMinutes(1);
                    }
                    else if (!IsTrophySync(i))
                    {
                        ChangeTrophyTime(i, BatchCustomTime, out _);
                        BatchCustomTime = BatchCustomTime.AddMinutes(1);
                    }
                }
                return 0;
            });
            StatusMessage = "Batch custom time applied.";
        }

        private int RunBatch(Func<int> body)
        {
            int? selected = SelectedTrophy?.Id;
            _suspendRebuild = true;
            try
            {
                return body();
            }
            finally
            {
                _suspendRebuild = false;
                RebuildTrophyList();
                if (selected.HasValue)
                {
                    SelectedTrophy = Trophies.FirstOrDefault(t => t.Id == selected.Value);
                }
            }
        }

        private DateTime CurrentTime(int id)
        {
            return _tpsn[id].HasValue ? _tpsn[id].Value.Time : _tusr.trophyTimeInfoTable[id].Time;
        }

        private static void DeleteTempRoot(string temp)
        {
            if (string.IsNullOrEmpty(temp))
            {
                return;
            }
            try
            {
                string parent = Path.GetDirectoryName(temp);
                string target = string.IsNullOrEmpty(parent) ? temp : parent;
                // Only ever delete inside the system temp folder.
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                if (Path.GetFullPath(target).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar), tempRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    TrophyUtility.DeleteDirectory(target);
                }
            }
            catch
            {
            }
        }

        // Called by the window on exit so decrypted temp copies never outlive the app.
        public void Shutdown()
        {
            if (_isOpen)
            {
                CloseFileInternal(false);
            }
        }

        private DateTime LastTrophyTime()
        {
            if (DateTime.Compare(_tpsn.LastTrophyTime, _tusr.LastTrophyTime) > 0)
            {
                return _tpsn.LastTrophyTime;
            }
            return _tusr.LastTrophyTime;
        }

        // ---------- Save / Close ----------
        [RelayCommand]
        private async Task SaveAsync()
        {
            await TrySaveAsync();
        }

        // Returns false when the save failed so callers (e.g. window close) can
        // keep the app open instead of throwing away the user's edits.
        public async Task<bool> TrySaveAsync()
        {
            if (!_isOpen)
            {
                return true;
            }
            StatusMessage = "Saving ...";
            string path = _path;
            string pathTemp = _pathTemp;
            string profile = SelectedProfile ?? "Default Profile";
            bool needBackup = !_backupDone;
            string error = null;
            string backup = null;
            bool writingOriginals = false;
            await Task.Run(() =>
            {
                string encPathTemp = TrophyUtility.GetTemporaryDirectory();
                try
                {
                    _tpsn.Save();
                    _tusr.Save();
                    TrophyUtility.CopyTrophyData(pathTemp, encPathTemp, false);
                    TrophyUtility.encryptTrophy(encPathTemp, profile);
                    // Back up the untouched originals before the first overwrite.
                    if (needBackup)
                    {
                        backup = TrophyUtility.BackupTrophyData(path);
                    }
                    writingOriginals = true;
                    TrophyUtility.CopyTrophyData(encPathTemp, path, true);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    LogDiag("Save FAILED: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (!TrophyUtility.TryDeleteDirectory(encPathTemp))
                    {
                        LogDiag("Could not delete temp folder '" + encPathTemp + "'.");
                    }
                }
            });
            // Record the backup even when the save failed afterwards: it holds the
            // untouched originals, and a later save must not "back up" a folder
            // that a failed copy left half-overwritten.
            if (backup != null)
            {
                _backupDone = true;
                LastBackupPath = backup;
            }
            if (error != null)
            {
                if (writingOriginals)
                {
                    string restoreFrom = string.IsNullOrEmpty(LastBackupPath) ? "your backup" : LastBackupPath;
                    StatusMessage = "Save failed while writing the trophy folder — restore it from " + restoreFrom + ".";
                    RaiseAlert("Save failed", error + Environment.NewLine + Environment.NewLine +
                        "Some trophy files may already have been overwritten. The originals are in " + restoreFrom + ".");
                }
                else
                {
                    StatusMessage = "Save failed — your original trophy folder was not changed.";
                    RaiseAlert("Save failed", error);
                }
                return false;
            }
            _haveBeenEdited = false;
            RebuildTrophyList();
            PfdStatus = "Saved + re-signed (PFD)";
            StatusMessage = backup != null ? "Saved. Original backed up to " + backup : "Saved.";
            return true;
        }

        [RelayCommand]
        private async Task SaveAsAsync()
        {
            if (!_isOpen || HostWindow == null)
            {
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                return;
            }
            // A folder picker: the old file picker asked for "TROPTRNS.DAT" but
            // actually wrote every trophy file into that file's directory.
            IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Save a re-signed copy into folder ...",
                AllowMultiple = false
            });
            if (folders == null || folders.Count == 0)
            {
                return;
            }
            string targetDir = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
            if (string.IsNullOrEmpty(targetDir))
            {
                return;
            }
            string pathTemp = _pathTemp;
            string profile = SelectedProfile ?? "Default Profile";
            string error = null;
            await Task.Run(() =>
            {
                string encPathTemp = TrophyUtility.GetTemporaryDirectory();
                try
                {
                    _tpsn.Save();
                    _tusr.Save();
                    TrophyUtility.CopyTrophyData(pathTemp, encPathTemp, false);
                    TrophyUtility.encryptTrophy(encPathTemp, profile);
                    TrophyUtility.CopyTrophyData(encPathTemp, targetDir, true);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    LogDiag("SaveAs FAILED: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (!TrophyUtility.TryDeleteDirectory(encPathTemp))
                    {
                        LogDiag("Could not delete temp folder '" + encPathTemp + "'.");
                    }
                }
            });
            if (error != null)
            {
                StatusMessage = "Save As failed.";
                RaiseAlert("Save As failed", error);
                return;
            }
            StatusMessage = "Saved copy to " + targetDir;
        }

        [RelayCommand]
        private void CloseFile()
        {
            CloseFileInternal(true);
        }

        private void CloseFileInternal(bool refreshUi)
        {
            string tempToDelete = _pathTemp;
            _tpsn = null;
            _tusr = null;
            _tconf = null;
            _path = string.Empty;
            _pathTemp = string.Empty;
            _haveBeenEdited = false;
            _isOpen = false;
            // Legacy parity: decrypted temp copies must not accumulate in %TEMP%.
            DeleteTempRoot(tempToDelete);
            void clear()
            {
                ClearTrophies();
                ApplyFilter();
                GameTitle = string.Empty;
                TitleId = string.Empty;
                AccountId = string.Empty;
                TrophySetVersion = string.Empty;
                SyncState = string.Empty;
                PfdStatus = "No folder loaded";
                CountText = "00/00";
                PointsText = "000/000";
                ProgressMax = 100;
                ProgressValue = 0;
                CurrentFolder = string.Empty;
                StatusMessage = "No folder loaded.";
                OnPropertyChanged(nameof(IsOpen));
                if (HostWindow != null)
                {
                    HostWindow.Title = "TrophyPrompt";
                }
            }
            if (refreshUi)
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    clear();
                }
                else
                {
                    Dispatcher.UIThread.Post(clear);
                }
            }
            else
            {
                clear();
            }
        }

        [RelayCommand]
        private void Refresh()
        {
            if (!_isOpen)
            {
                return;
            }
            RebuildTrophyList();
            RefreshHeader();
        }

        [RelayCommand]
        private void SetFilter(string filter)
        {
            SelectedFilter = filter ?? "All";
        }

        public void WireRowActions(TrophyItemViewModel vm)
        {
            vm.ToggleRequested = (row) => { _ = ToggleRowAsync(row); };
            vm.EditTimeRequested = (row) =>
            {
                EditTimeRequestedHandler?.Invoke(row);
            };
        }

        private async Task ToggleRowAsync(TrophyItemViewModel row)
        {
            string error;
            if (IsTrophyGot(row.Id))
            {
                bool ok = await ConfirmDefaultAsync("Delete Trophy", "This trophy will be deleted, sure?");
                if (!ok)
                {
                    return;
                }
                if (!LockTrophy(row.Id, out error) && error != null)
                {
                    StatusMessage = error;
                }
            }
            else
            {
                // Legacy parity: unlocking a locked trophy always asks for a
                // timestamp (DateTimePickForm) instead of stamping silently.
                if (EditTimeRequestedHandler != null)
                {
                    EditTimeRequestedHandler(row);
                }
                else
                {
                    string error2;
                    if (!UnlockTrophy(row.Id, BatchCustomTime, out error2) && error2 != null)
                    {
                        StatusMessage = error2;
                    }
                }
            }
        }

        public Action<TrophyItemViewModel> EditTimeRequestedHandler { get; set; }

        public bool ApplyEditTime(TrophyItemViewModel row, DateTime time, out string error)
        {
            if (IsTrophyGot(row.Id))
            {
                return ChangeTrophyTime(row.Id, time, out error);
            }
            return UnlockTrophy(row.Id, time, out error);
        }

        public Task<bool> SaveFilesAsync()
        {
            return TrySaveAsync();
        }

        // ---------- Exporters ----------
        private int FindTrophyIndex(TrophyDto trophy)
        {
            if (trophy == null || _tconf == null)
            {
                return -1;
            }
            if (trophy.Id >= 0 && trophy.Id < _tconf.Count)
            {
                return trophy.Id;
            }
            for (int i = 0; i < _tconf.Count; i++)
            {
                if (string.Equals(_tconf[i].name, trophy.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private List<string> CheckChronology(IList<TrophyDto> list)
        {
            List<string> warnings = new List<string>();
            DateTime prev = DateTime.MinValue;
            for (int k = 0; k < list.Count; k++)
            {
                if (!list[k].Unlocked)
                {
                    continue;
                }
                if (list[k].Timestamp < _lastSyncTrophyTime)
                {
                    warnings.Add("Row " + k + " (" + list[k].Name + ") is before PSN sync time.");
                }
                if (list[k].Timestamp < prev)
                {
                    warnings.Add("Non-chronological sequence at row " + k + " (" + list[k].Name + ").");
                }
                if (list[k].Timestamp > prev)
                {
                    prev = list[k].Timestamp;
                }
            }
            return warnings;
        }

        [RelayCommand]
        private async Task ExportJsonAsync()
        {
            LogDiag("ExportJsonCommand fired. IsOpen = " + _isOpen + ", HostWindow null = " + (HostWindow == null) + ".");
            if (!_isOpen || HostWindow == null)
            {
                StatusMessage = "Open a trophy folder first (File → Open Folder, or drag it onto the window).";
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                return;
            }
            string suggestedName = string.IsNullOrWhiteSpace(GameTitle) ? "trophies" : GameTitle;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                suggestedName = suggestedName.Replace(c.ToString(), "_");
            }
            IStorageFile file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export trophies to JSON",
                SuggestedFileName = suggestedName + ".json",
                FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("JSON") { Patterns = new List<string> { "*.json" } } },
            });
            if (file == null)
            {
                return;
            }
            try
            {
                List<TrophyDto> trophies = new List<TrophyDto>();
                for (int i = 0; i < _tconf.Count; i++)
                {
                    TrophyDto dto = new TrophyDto
                    {
                        Id = i,
                        Name = _tconf[i].name,
                        Description = _tconf[i].detail,
                        Unlocked = IsTrophyGot(i),
                        Timestamp = DateTime.MinValue
                    };
                    if (dto.Unlocked)
                    {
                        dto.Timestamp = _tpsn[i].HasValue ? _tpsn[i].Value.Time : _tusr.trophyTimeInfoTable[i].Time;
                    }
                    trophies.Add(dto);
                }
                List<string> warnings = CheckChronology(trophies);
                var exportData = new ExportRootDto
                {
                    GameTitle = GameTitle,
                    TitleId = TitleId,
                    AccountId = AccountId,
                    Trophies = trophies,
                    SystemPrompt = ExportRootDto.SystemPromptText
                };
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    Culture = CultureInfo.InvariantCulture
                };
                string json = JsonConvert.SerializeObject(exportData, settings);
                string localPath = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(localPath))
                {
                    using (Stream s = await file.OpenWriteAsync())
                    using (StreamWriter w = new StreamWriter(s))
                    {
                        await w.WriteAsync(json);
                    }
                }
                else
                {
                    File.WriteAllText(localPath, json);
                    // Sidecar notice block (system_prompt): LLM workflow + chronology warnings.
                    StringBuilder notice = new StringBuilder();
                    notice.AppendLine("TrophyPrompt — JSON export notice (system_prompt).");
                    notice.AppendLine("LLM workflow: 1) Export this JSON and open it in Claude/GPT. 2) Ask the model for realistic unlock timestamps. 3) Keep Id/Name stable; update only Timestamp/Unlocked; import back.");
                    notice.AppendLine("Timestamp format: yyyy-MM-dd HH:mm:ss. Prefer chronological order.");
                    if (warnings.Count > 0)
                    {
                        notice.AppendLine("WARNINGS (non-chronological sequence):");
                        foreach (string w in warnings)
                        {
                            notice.AppendLine("- " + w);
                        }
                    }
                    else
                    {
                        notice.AppendLine("Sequence check: OK (chronological).");
                    }
                    File.WriteAllText(Path.ChangeExtension(localPath, ".notice.txt"), notice.ToString());
                }
                StatusMessage = warnings.Count > 0
                    ? "Exported JSON with " + warnings.Count + " sequence warning(s)."
                    : "Exported JSON successfully.";
                LogDiag("ExportJson: OK -> '" + localPath + "', rows=" + trophies.Count + ", warnings=" + warnings.Count + ".");
            }
            catch (Exception ex)
            {
                StatusMessage = "JSON export failed: " + ex.Message;
                LogDiag("ExportJson FAILED: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        [RelayCommand]
        private async Task ImportJsonAsync()
        {
            LogDiag("ImportJsonCommand fired. IsOpen = " + _isOpen + ", HostWindow null = " + (HostWindow == null) + ".");
            if (!_isOpen || HostWindow == null)
            {
                StatusMessage = "Open a trophy folder first (File → Open Folder, or drag it onto the window).";
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                return;
            }
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import trophies from JSON",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { new FilePickerFileType("JSON") { Patterns = new List<string> { "*.json" } } },
            });
            if (files == null || files.Count == 0)
            {
                return;
            }
            try
            {
                string text;
                string lp = files[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(lp))
                {
                    text = File.ReadAllText(lp);
                }
                else
                {
                    using (Stream s = await files[0].OpenReadAsync())
                    using (StreamReader r = new StreamReader(s))
                    {
                        text = await r.ReadToEndAsync();
                    }
                }
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    Culture = CultureInfo.InvariantCulture,
                    DateParseHandling = DateParseHandling.DateTime,
                    DateTimeZoneHandling = DateTimeZoneHandling.Unspecified
                };
                List<TrophyDto> list;
                string trimmed = text.TrimStart();
                if (trimmed.StartsWith("["))
                {
                    // Legacy 1:1 format: plain JSON array.
                    list = JsonConvert.DeserializeObject<List<TrophyDto>>(text, settings);
                }
                else
                {
                    // Current format: ExportRootDto wrapper (metadata + Trophies + SystemPrompt footer).
                    ExportRootDto root = JsonConvert.DeserializeObject<ExportRootDto>(text, settings);
                    list = root != null ? root.Trophies : null;
                }
                if (list == null || list.Count == 0)
                {
                    StatusMessage = "No trophy data in JSON.";
                    return;
                }
                var resolved = new List<(int Index, TrophyDto Dto)>();
                var seen = new HashSet<int>();
                foreach (TrophyDto t in list)
                {
                    if (t == null)
                    {
                        continue;
                    }
                    int idx = FindTrophyIndex(t);
                    if (idx >= 0 && seen.Add(idx))
                    {
                        resolved.Add((idx, t));
                    }
                }
                bool hasPlat = _tconf.HasPlatinium;
                List<string> failures = new List<string>();
                RunBatch(() =>
                {
                    // Order matters: the platinum must be locked BEFORE other base
                    // trophies and unlocked AFTER them, or the edit is rejected.
                    // Exports list the platinum first (id 0), so a straight
                    // in-order import used to silently drop the platinum.
                    foreach (var r in ImportOrder.Sort(resolved, x => x.Index, x => x.Dto.Unlocked, hasPlat))
                    {
                        string err = null;
                        bool ok = true;
                        DateTime sel = r.Dto.Timestamp == DateTime.MinValue ? DateTime.Now : r.Dto.Timestamp;
                        if (r.Dto.Unlocked)
                        {
                            if (!IsTrophyGot(r.Index))
                            {
                                ok = UnlockTrophy(r.Index, sel, out err);
                            }
                            else if (!IsTrophySync(r.Index) && CurrentTime(r.Index) != sel)
                            {
                                ok = ChangeTrophyTime(r.Index, sel, out err);
                            }
                        }
                        else if (IsTrophyGot(r.Index))
                        {
                            ok = LockTrophy(r.Index, out err);
                        }
                        if (!ok)
                        {
                            failures.Add("#" + r.Index + " " + _tconf[r.Index].name + ": " + err);
                        }
                    }
                    return failures.Count;
                });
                int matched = resolved.Count;
                StatusMessage = matched == 0
                    ? "No trophies matched."
                    : "Imported " + matched + " entries" + (failures.Count > 0 ? ", " + failures.Count + " rejected." : ".");
                if (failures.Count > 0)
                {
                    RaiseAlert("Some trophies were not imported", string.Join(Environment.NewLine, failures.Take(15)) +
                        (failures.Count > 15 ? Environment.NewLine + "... and " + (failures.Count - 15) + " more" : string.Empty));
                }
                LogDiag("ImportJson: OK, matched=" + matched + ", failed=" + failures.Count + ".");
            }
            catch (Exception ex)
            {
                StatusMessage = "JSON import failed: " + ex.Message;
                LogDiag("ImportJson FAILED: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        [RelayCommand]
        private async Task ExportCsvAsync()
        {
            if (!_isOpen || HostWindow == null)
            {
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                return;
            }
            IStorageFile file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export summary CSV",
                SuggestedFileName = "trophies.csv",
                FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("CSV") { Patterns = new List<string> { "*.csv" } } },
            });
            if (file == null)
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Id,Name,Type,Hidden,Unlocked,Sync,Timestamp,Group");
            foreach (TrophyItemViewModel t in Trophies)
            {
                sb.AppendLine(t.Id + "," + CsvField.Escape(t.Name) + "," + t.TypeName + "," + (t.IsHidden ? "yes" : "no") + "," + (t.IsUnlocked ? "yes" : "no") + "," + (t.IsSync ? "yes" : "no") + "," + t.TimestampText + "," + t.Group);
            }
            string lp = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(lp))
            {
                File.WriteAllText(lp, sb.ToString(), Encoding.UTF8);
            }
            else
            {
                using (Stream s = await file.OpenWriteAsync())
                using (StreamWriter w = new StreamWriter(s))
                {
                    await w.WriteAsync(sb.ToString());
                }
            }
            StatusMessage = "Exported CSV.";
        }

        [RelayCommand]
        private async Task ExportTextAsync()
        {
            if (!_isOpen || HostWindow == null)
            {
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                return;
            }
            IStorageFile file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export summary text log",
                SuggestedFileName = "trophies.txt",
                FileTypeChoices = new List<FilePickerFileType> { new FilePickerFileType("Text") { Patterns = new List<string> { "*.txt" } } },
            });
            if (file == null)
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(GameTitle + " [" + TitleId + "]");
            sb.AppendLine("Account: " + AccountId + " | Set: " + TrophySetVersion + " | " + SyncState);
            sb.AppendLine("Progress: " + CountText + " | Points: " + PointsText);
            foreach (TrophyItemViewModel t in Trophies)
            {
                sb.AppendLine("#" + t.Id + " [" + t.TypeName + "] " + t.Name + " — " + (t.IsUnlocked ? t.TimestampText : "locked") + (t.IsSync ? " (synced)" : string.Empty));
            }
            string lp = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(lp))
            {
                File.WriteAllText(lp, sb.ToString(), Encoding.UTF8);
            }
            else
            {
                using (Stream s = await file.OpenWriteAsync())
                using (StreamWriter w = new StreamWriter(s))
                {
                    await w.WriteAsync(sb.ToString());
                }
            }
            StatusMessage = "Exported text log.";
        }
    }
}
