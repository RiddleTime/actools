﻿using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using AcManager.Tools.SemiGui;
using AcTools.DataFile;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using JetBrains.Annotations;

namespace AcManager.Tools.Helpers {
    public partial class AcSettingsHolder {
        private static FileSystemWatcher _watcher;

        private static void InitializeWatcher() {
            if (_watcher != null) return;

            var directory = FileUtils.GetDocumentsCfgDirectory();
            Directory.CreateDirectory(directory);

            _watcher = new FileSystemWatcher {
                Path = directory,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = "*",
                EnableRaisingEvents = true,
                IncludeSubdirectories = true
            };
        }

        public abstract class IniSettings : NotifyPropertyChanged {
            private readonly string _filename;

            protected IniSettings(string name) {
                try {
                    _filename = Path.Combine(FileUtils.GetDocumentsCfgDirectory(), name + ".ini");
                    Reload();

                    InitializeWatcher();
                    _watcher.Changed += InnerWatcher_Changed;
                    _watcher.Created += InnerWatcher_Changed;
                    _watcher.Deleted += InnerWatcher_Changed;
                    _watcher.Renamed += InnerWatcher_Renamed;
                } catch (Exception e) {
                    Logging.Warning("IniSettings exception: " + e);
                }
            }

            private void InnerWatcher_Renamed(object sender, RenamedEventArgs e) {
                if (FileUtils.IsAffected(e.OldFullPath, _filename) || FileUtils.IsAffected(e.FullPath, _filename)) {
                    ReloadLater();
                }
            }

            private void InnerWatcher_Changed(object sender, FileSystemEventArgs e) {
                if (FileUtils.IsAffected(e.FullPath, _filename)) {
                    ReloadLater();
                }
            }

            protected void Reload() {
                Ini = new IniFile(_filename);
                _loading = true;
                LoadFromIni();
                _loading = false;
            }

            private bool _reloading;
            private bool _loading;
            private DateTime _lastSaved;

            private async void ReloadLater() {
                if (_reloading || _saving || DateTime.Now - _lastSaved < TimeSpan.FromSeconds(1)) return;

                _reloading = true;
                await Task.Delay(200);

                try {
                    Ini = new IniFile(_filename);
                    _loading = true;
                    LoadFromIni();
                    _loading = false;
                } finally {
                    _reloading = false;
                }
            }

            private bool _saving;

            protected async void Save() {
                if (_saving || _loading) return;

                _saving = true;
                await Task.Delay(500);

                try {
                    SetToIni();
                    Ini.Save(_filename);
                    _lastSaved = DateTime.Now;
                } catch (Exception e) {
                    NonfatalError.Notify("Can't save AC settings", "Make sure app has access to cfg folder.", e);
                } finally {
                    _saving = false;
                }
            }

            protected void ForceSave() {
                var l = _loading;
                _loading = false;
                Save();
                _loading = l;
            }

            protected IniFile Ini;

            /// <summary>
            /// Called from IniSettings constructor!
            /// </summary>
            protected abstract void LoadFromIni();

            protected abstract void SetToIni();

            [NotifyPropertyChangedInvocator]
            protected override void OnPropertyChanged([CallerMemberName] string propertyName = null) {
                base.OnPropertyChanged(propertyName);
                Save();
            }

            [NotifyPropertyChangedInvocator]
            protected void OnPropertyChanged(bool save = true, [CallerMemberName] string propertyName = null) {
                base.OnPropertyChanged(propertyName);
                if (save) {
                    Save();
                }
            }
        }

        private class InnerZeroToOffConverter : IValueConverter {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                double d;
                return value == null || double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d) && Equals(d, 0d)
                        ? (parameter ?? "Off") : value;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                return value == parameter || value as string ==  "Off" ? 0d : value;
            }
        }

        public static IValueConverter ZeroToOffConverter { get; } = new InnerZeroToOffConverter();
    }
}