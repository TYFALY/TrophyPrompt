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
                StatusMessage = "DIAG: HostWindow is null — command fired but window not wired.";
                LogDiag("OpenFolderCommand aborted: HostWindow null.");
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                StatusMessage = "DIAG: TopLevel is null — window not attached yet.";
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
                StatusMessage = "DIAG: picker cancelled or returned nothing.";
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
                try
                {
                    string temp = TrophyUtility.CopyTrophyDirToTemp(folder);
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
                    GC.Collect();
                    LogDiag("OpenFolderAtAsync FileNotFound: " + ex.Message);
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
                    GC.Collect();
                    string msg = ex.Message;
                    LogDiag("OpenFolderAtAsync FAILED: " + ex.GetType().Name + ": " + msg);
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusMessage = msg;
                    });
                    RaiseAlert("Open folder failed", msg);
                }
            });
        }

        private void RebuildTrophyList()
        {
            Trophies.Clear();
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
                byte[] iconBytes = TrophyUtility.GetTrophyIconBytes(_path, _tconf[i].id);
                if (iconBytes == null)
                {
                    iconBytes = TrophyUtility.GetTrophyIconBytes(_pathTemp, _tconf[i].id);
                }
                vm.SetIconFromBytes(iconBytes);
                vm.RefreshDerived();
                WireRowActions(vm);
                Trophies.Add(vm);
            }
            string ignoredParadoxError;
            ValidateTrophies(out ignoredParadoxError);
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
                error = "You can't unlock platinum when other thropy is unlocked.";
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
                error = "You can't lock other trophies while platinum is locked.";
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
            Random rand = new Random((int)DateTime.Now.Ticks);
            int i;
            for (i = 1; i < _tusr.trophyTimeInfoTable.Count && _tconf[i].gid == 0; i++)
            {
                if (!IsTrophyGot(i))
                {
                    DateTime t = new DateTime(TrophyUtility.LongRandom(_ps3Time.Ticks, _randomEndTime.Ticks, rand));
                    _tusr.UnlockTrophy(i, t);
                    _tpsn.PutTrophy(i, _tusr.trophyTypeTable[i].Type, new DateTime(TrophyUtility.LongRandom(_ps3Time.Ticks, _randomEndTime.Ticks, rand)));
                }
            }
            if (!IsTrophyGot(0))
            {
                DateTime last = LastTrophyTime().AddSeconds(1);
                _tusr.UnlockTrophy(0, last);
                _tpsn.PutTrophy(0, _tusr.trophyTypeTable[0].Type, LastTrophyTime().AddSeconds(1));
            }
            for (; i < _tusr.trophyTimeInfoTable.Count; i++)
            {
                if (!IsTrophyGot(i))
                {
                    DateTime t = new DateTime(TrophyUtility.LongRandom(_ps3Time.Ticks, _randomEndTime.Ticks, rand));
                    _tusr.UnlockTrophy(i, t);
                    _tpsn.PutTrophy(i, _tusr.trophyTypeTable[i].Type, new DateTime(TrophyUtility.LongRandom(_ps3Time.Ticks, _randomEndTime.Ticks, rand)));
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
            Random rand = new Random((int)DateTime.Now.Ticks);
            for (int i = 0; i < Trophies.Count; i++)
            {
                if (IsTrophyGot(i) && !IsTrophySync(i))
                {
                    DateTime t = new DateTime(TrophyUtility.LongRandom(RandomStartTime.Ticks, RandomEndTimeProp.Ticks, rand));
                    string err;
                    ChangeTrophyTime(i, t, out err);
                }
            }
            RebuildTrophyList();
            StatusMessage = "Timestamps randomized.";
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
            for (int i = 0; i < Trophies.Count; i++)
            {
                if (IsTrophyGot(i) && !IsTrophySync(i))
                {
                    string err;
                    ChangeTrophyTime(i, anchor, out err);
                    anchor = anchor.Add(step);
                }
            }
            RebuildTrophyList();
            StatusMessage = "Timestamps copied sequentially from batch time.";
        }

        // ---------- Copy From (psntrophyleaders scrape, legacy toolStripMenuItem1) ----------
        public bool ApplyCopiedTimes(IList<long> times, out string error)
        {
            error = null;
            if (!_isOpen || times == null)
            {
                error = "No trophy folder loaded.";
                return false;
            }
            try
            {
                // Legacy locks everything first so the grid refreshes cleanly.
                TROPTRNS.TrophyInfo? ti = _tpsn.PopTrophy();
                while (ti.HasValue)
                {
                    _tusr.LockTrophy(ti.Value.TrophyID);
                    ti = _tpsn.PopTrophy();
                }
                int n = Math.Min(times.Count, _tusr.trophyTimeInfoTable.Count);
                for (int i = 0; i < n; i++)
                {
                    if (!_tpsn[i].HasValue && times[i] != 0)
                    {
                        DateTime time = times[i].TimeStampToDateTime();
                        _tusr.UnlockTrophy(i, time);
                        _tpsn.PutTrophy(i, _tusr.trophyTypeTable[i].Type, time);
                    }
                }
                _haveBeenEdited = true;
                RebuildTrophyList();
                StatusMessage = "Copied times from profile.";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [RelayCommand]
        private void BatchSetCustomTime()
        {
            if (!_isOpen)
            {
                return;
            }
            for (int i = 0; i < Trophies.Count; i++)
            {
                if (!IsTrophyGot(i))
                {
                    string err;
                    UnlockTrophy(i, BatchCustomTime, out err);
                    BatchCustomTime = BatchCustomTime.AddMinutes(1);
                }
                else if (!IsTrophySync(i))
                {
                    string err;
                    ChangeTrophyTime(i, BatchCustomTime, out err);
                    BatchCustomTime = BatchCustomTime.AddMinutes(1);
                }
            }
            RebuildTrophyList();
            StatusMessage = "Batch custom time applied.";
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
            if (!_isOpen)
            {
                return;
            }
            string paradoxError;
            if (!ValidateTrophies(out paradoxError))
            {
                StatusMessage = "Save blocked: " + paradoxError;
                RaiseAlert("Save blocked", paradoxError);
                return;
            }
            StatusMessage = "Saving ...";
            await Task.Run(() =>
            {
                _tpsn.Save();
                _tusr.Save();
                _haveBeenEdited = false;
                string encPathTemp = TrophyUtility.GetTemporaryDirectory();
                try
                {
                    TrophyUtility.CopyTrophyData(_pathTemp, encPathTemp, false);
                    TrophyUtility.encryptTrophy(encPathTemp, SelectedProfile ?? "Default Profile");
                    TrophyUtility.CopyTrophyData(encPathTemp, _path, true);
                }
                finally
                {
                    TrophyUtility.DeleteDirectory(encPathTemp);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    RebuildTrophyList();
                    PfdStatus = "Saved + re-signed (PFD)";
                    StatusMessage = "Saved.";
                });
            });
        }

        [RelayCommand]
        private async Task SaveAsAsync()
        {
            if (!_isOpen || HostWindow == null)
            {
                return;
            }
            string paradoxError;
            if (!ValidateTrophies(out paradoxError))
            {
                StatusMessage = "Save blocked: " + paradoxError;
                RaiseAlert("Save blocked", paradoxError);
                return;
            }
            TopLevel topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel == null)
            {
                return;
            }
            IStorageFile file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save trophy folder copy as ...",
                SuggestedFileName = "TROPTRNS.DAT",
            });
            if (file == null)
            {
                return;
            }
            string targetDir = Path.GetDirectoryName(file.TryGetLocalPath() ?? file.Path.LocalPath);
            if (string.IsNullOrEmpty(targetDir))
            {
                return;
            }
            await Task.Run(() =>
            {
                _tpsn.Save();
                _tusr.Save();
                string encPathTemp = TrophyUtility.GetTemporaryDirectory();
                try
                {
                    TrophyUtility.CopyTrophyData(_pathTemp, encPathTemp, false);
                    TrophyUtility.encryptTrophy(encPathTemp, SelectedProfile ?? "Default Profile");
                    TrophyUtility.CopyTrophyData(encPathTemp, targetDir, true);
                }
                finally
                {
                    TrophyUtility.DeleteDirectory(encPathTemp);
                }
                Dispatcher.UIThread.Post(() => { StatusMessage = "Saved copy to " + targetDir; });
            });
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
            if (!string.IsNullOrEmpty(tempToDelete))
            {
                try
                {
                    string parent = Path.GetDirectoryName(tempToDelete);
                    TrophyUtility.DeleteDirectory(string.IsNullOrEmpty(parent) ? tempToDelete : parent);
                }
                catch
                {
                }
            }
            void clear()
            {
                Trophies.Clear();
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

        public Task SaveFilesAsync()
        {
            return SaveAsync();
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

        // Chronological paradox check. Flags offending rows (HasParadox +
        // ParadoxReason drive the red row highlight) and returns false with
        // the first reason when the set must not be written back.
        public bool ValidateTrophies(out string errorMessage)
        {
            List<ParadoxSnapshot> snapshots = new List<ParadoxSnapshot>(Trophies.Count);
            for (int i = 0; i < Trophies.Count; i++)
            {
                TrophyItemViewModel t = Trophies[i];
                t.HasParadox = false;
                t.ParadoxReason = string.Empty;
                snapshots.Add(new ParadoxSnapshot
                {
                    Id = t.Id,
                    Name = t.Name ?? string.Empty,
                    IsUnlocked = t.IsUnlocked,
                    Timestamp = t.Timestamp,
                    IsPlatinum = t.TrophyType == TropType.Platinum,
                    Group = t.Group ?? string.Empty,
                    Order = i
                });
            }
            List<ParadoxResult> results = ParadoxValidator.Validate(snapshots);
            if (results.Count == 0)
            {
                errorMessage = null;
                return true;
            }
            Dictionary<int, TrophyItemViewModel> byId = new Dictionary<int, TrophyItemViewModel>(Trophies.Count);
            for (int i = 0; i < Trophies.Count; i++)
            {
                byId[Trophies[i].Id] = Trophies[i];
            }
            for (int i = 0; i < results.Count; i++)
            {
                TrophyItemViewModel vm;
                if (byId.TryGetValue(results[i].Id, out vm))
                {
                    vm.HasParadox = true;
                    vm.ParadoxReason = results[i].Reason;
                }
            }
            errorMessage = results[0].Reason;
            return false;
        }

        private List<string> CheckChronology(IList<TrophyDto> list)        {
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
                StatusMessage = "DIAG: Export blocked — no folder open. Open a folder first.";
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
                // Paradoxes never block export (export -> AI -> import is how
                // you fix them); they only block Save. Flag rows + warn here.
                string paradoxError;
                bool paradoxFree = ValidateTrophies(out paradoxError);
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
                    if (!paradoxFree)
                    {
                        notice.AppendLine("PARADOX WARNING (exported anyway — fix before Save): " + paradoxError);
                    }
                    File.WriteAllText(Path.ChangeExtension(localPath, ".notice.txt"), notice.ToString());
                }
                StatusMessage = warnings.Count > 0
                    ? "Exported JSON with " + warnings.Count + " sequence warning(s)."
                    : "Exported JSON successfully.";
                if (!paradoxFree)
                {
                    StatusMessage += " Paradox flagged — fix before Save: " + paradoxError;
                }
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
                StatusMessage = "DIAG: Import blocked — no folder open. Open a folder first.";
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
                int matched = 0;
                foreach (TrophyDto t in list)
                {
                    if (t == null)
                    {
                        continue;
                    }
                    int idx = FindTrophyIndex(t);
                    if (idx < 0)
                    {
                        continue;
                    }
                    matched++;
                    DateTime sel = t.Timestamp == DateTime.MinValue ? DateTime.Now : t.Timestamp;
                    string err;
                    if (t.Unlocked)
                    {
                        if (!IsTrophyGot(idx))
                        {
                            UnlockTrophy(idx, sel, out err);
                        }
                        else
                        {
                            ChangeTrophyTime(idx, sel, out err);
                        }
                    }
                    else if (IsTrophyGot(idx))
                    {
                        LockTrophy(idx, out err);
                    }
                }
                RebuildTrophyList();
                StatusMessage = matched == 0 ? "No trophies matched." : "Imported " + matched + " entries.";
                LogDiag("ImportJson: OK, matched=" + matched + ".");
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
                sb.AppendLine(t.Id + ",\"" + (t.Name ?? string.Empty).Replace("\"", "\"\"") + "\"," + t.TypeName + "," + (t.IsHidden ? "yes" : "no") + "," + (t.IsUnlocked ? "yes" : "no") + "," + (t.IsSync ? "yes" : "no") + "," + t.TimestampText + "," + t.Group);
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
