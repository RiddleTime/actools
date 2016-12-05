using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using AcManager.Tools.AcManagersNew;
using AcManager.Tools.GameProperties;
using AcManager.Tools.Helpers;
using AcManager.Tools.Helpers.Api;
using AcManager.Tools.Helpers.Api.Kunos;
using AcManager.Tools.Miscellaneous;
using AcManager.Tools.Objects;
using AcManager.Tools.SemiGui;
using AcTools.Processes;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using FirstFloor.ModernUI.Windows.Converters;
using JetBrains.Annotations;

namespace AcManager.Tools.Managers.Online {
    public partial class ServerEntry : Displayable, IComparer, IWithId {
        public class Session {
            public bool IsActive { get; set; }

            /// <summary>
            /// Seconds.
            /// </summary>
            public long Duration { get; set; }

            public Game.SessionType Type { get; set; }

            public string DisplayType => Type.GetDescription() ?? Type.ToString();

            public string DisplayTypeShort => DisplayType.Substring(0, 1);

            public string DisplayDuration => Type == Game.SessionType.Race ?
                    PluralizingConverter.PluralizeExt((int)Duration, ToolsStrings.Online_Session_LapsDuration) :
                    Duration.ToReadableTime();
        }

        public class CarEntry : Displayable, IWithId {
            private CarSkinObject _availableSkin;

            [NotNull]
            public CarObject CarObject { get; }

            public CarEntry([NotNull] CarObject carObject) {
                CarObject = carObject;
            }

            [CanBeNull]
            public CarSkinObject AvailableSkin {
                get { return _availableSkin; }
                set {
                    if (Equals(value, _availableSkin)) return;
                    _availableSkin = value;
                    OnPropertyChanged();

                    if (Total == 0 && value != null) {
                        CarObject.SelectedSkin = value;
                    }
                }
            }

            private int _total;

            public int Total {
                get { return _total; }
                set {
                    if (value == _total) return;
                    _total = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAvailable));
                    OnPropertyChanged(nameof(DisplayName));
                }
            }

            private int _available;

            public int Available {
                get { return _available; }
                set {
                    if (value == _available) return;
                    _available = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsAvailable));
                    OnPropertyChanged(nameof(DisplayName));
                }
            }

            public bool IsAvailable => Total == 0 || Available > 0;

            public override string DisplayName {
                get { return Total == 0 ? CarObject.DisplayName : $@"{CarObject.DisplayName} ({Available}/{Total})"; }
                set { }
            }

            protected bool Equals(CarEntry other) {
                return Equals(_availableSkin, other._availableSkin) && CarObject.Equals(other.CarObject) && Total == other.Total && Available == other.Available;
            }

            public override bool Equals(object obj) {
                return !ReferenceEquals(null, obj) && (ReferenceEquals(this, obj) || obj.GetType() == GetType() && Equals((CarEntry)obj));
            }

            public override int GetHashCode() {
                unchecked {
                    var hashCode = _availableSkin?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ CarObject.GetHashCode();
                    hashCode = (hashCode * 397) ^ Total;
                    hashCode = (hashCode * 397) ^ Available;
                    return hashCode;
                }
            }

            public string Id => CarObject.Id;
        }

        public class CarOrOnlyCarIdEntry {
            [CanBeNull]
            public CarObject CarObject => (CarObject)CarObjectWrapper?.Loaded();

            [CanBeNull]
            public AcItemWrapper CarObjectWrapper { get; }

            public string CarId { get; }

            public bool CarExists => CarObjectWrapper != null;

            public CarOrOnlyCarIdEntry(string carId, AcItemWrapper carObjectWrapper = null) {
                CarId = carId;
                CarObjectWrapper = carObjectWrapper;
            }

            public CarOrOnlyCarIdEntry([NotNull] AcItemWrapper carObjectWrapper) {
                CarObjectWrapper = carObjectWrapper;
                CarId = carObjectWrapper.Value.Id;
            }
        }

        public class CurrentDriver {
            public string Name { get; set; }

            public string Team { get; set; }

            public string CarId { get; set; }

            public string CarSkinId { get; set; }

            private CarObject _car;

            public CarObject Car => _car ?? (_car = CarsManager.Instance.GetById(CarId));

            private CarSkinObject _carSkin;

            public CarSkinObject CarSkin => _carSkin ??
                    (_carSkin = CarSkinId != null ? Car?.GetSkinById(CarSkinId) : Car?.GetFirstSkinOrNull());

            protected bool Equals(CurrentDriver other) {
                return string.Equals(Name, other.Name) && string.Equals(Team, other.Team) && string.Equals(CarId, other.CarId) && string.Equals(CarSkinId, other.CarSkinId);
            }

            public override bool Equals(object obj) {
                return !ReferenceEquals(null, obj) && (ReferenceEquals(this, obj) || obj.GetType() == GetType() && Equals((CurrentDriver)obj));
            }

            public override int GetHashCode() {
                unchecked {
                    var hashCode = Name?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (Team?.GetHashCode() ?? 0);
                    hashCode = (hashCode * 397) ^ (CarId?.GetHashCode() ?? 0);
                    hashCode = (hashCode * 397) ^ (CarSkinId?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }
        }

        public string Id { get; }

        /// <summary>
        /// IP-address, non-changeable.
        /// </summary>
        public string Ip { get; }

        private int _portHttp;

        /// <summary>
        /// For json-requests directly to launcher server, non-changeable.
        /// </summary>
        public int PortHttp {
            get { return _portHttp; }
            private set {
                if (Equals(value, _portHttp)) return;
                _portHttp = value;
                OnPropertyChanged();
            }
        }

        private int _port;

        /// <summary>
        /// As a query argument for //aclobby1.grecian.net/lobby.ashx/�.
        /// </summary>
        public int Port {
            get { return _port; }
            private set {
                if (Equals(value, _port)) return;
                _port = value;
                OnPropertyChanged();
            }
        }

        private int _portRace;

        /// <summary>
        /// For race.ini & acs.exe.
        /// </summary>
        public int PortRace {
            get { return _portRace; }
            private set {
                if (Equals(value, _portRace)) return;
                _portRace = value;
                OnPropertyChanged();
            }
        }

        private bool _isFullyLoaded;

        public bool IsFullyLoaded {
            get { return _isFullyLoaded; }
            set {
                if (Equals(value, _isFullyLoaded)) return;
                _isFullyLoaded = value;
                OnPropertyChanged();
            }
        }

        public bool IsLan { get; }

        public readonly ServerInformation OriginalInformation;

        public ServerEntry([NotNull] ServerInformation information) {
            if (information == null) throw new ArgumentNullException(nameof(information));

            Id = information.GetUniqueId();
            OriginalInformation = information;

            Ip = information.Ip;
            PortHttp = information.PortHttp;
            IsLan = information.IsLan;

            Ping = null;
            SetSomeProperties(information);
        }

        private static readonly Regex SpacesCollapseRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SortingCheatsRegex = new Regex(@"^(?:AA+|[ !-]+|A(?![b-zB-Z0-9])+)+| ?-$", RegexOptions.Compiled);
        private static readonly Regex SimpleCleanUpRegex = new Regex(@"^AA+\s*", RegexOptions.Compiled);

        private static string CleanUp(string name, [CanBeNull] string oldName) {
            name = name.Trim();
            name = SpacesCollapseRegex.Replace(name, " ");
            if (SettingsHolder.Online.FixNames) {
                name = SortingCheatsRegex.Replace(name, "");
            } else if (oldName != null && SimpleCleanUpRegex.IsMatch(name) && !SimpleCleanUpRegex.IsMatch(oldName)) {
                name = SimpleCleanUpRegex.Replace(name, "");
            }
            return name;
        }

        private void SetSomeProperties(ServerInformation information) {
            IsFullyLoaded = information.IsFullyLoaded;

            Port = information.Port;
            PortRace = information.PortRace;

            PreviousUpdateTime = DateTime.Now;
            DisplayName = information.Name == null ? Id : CleanUp(information.Name, DisplayName);

            {
                var country = information.Country?.FirstOrDefault() ?? "";
                Country = Country != null && country == @"na" ? Country : country;
            }

            {
                var countryId = information.Country?.ElementAtOrDefault(1) ?? "";
                CountryId = CountryId != null && countryId == @"na" ? CountryId : countryId;
            }

            CurrentDriversCount = information.Clients;
            Capacity = information.Capacity;

            PasswordRequired = information.Password;
            if (PasswordRequired) {
                Password = ValuesStorage.GetEncryptedString(PasswordStorageKey);
            }

            CarIds = information.CarIds;
            CarsOrTheirIds = CarIds?.Select(x => new CarOrOnlyCarIdEntry(x, GetCarWrapper(x))).ToList();
            TrackId = information.TrackId;
            Track = TrackId == null ? null : GetTrack(TrackId);

            string errorMessage = null;
            bool error;

            if (IsFullyLoaded) {
                error = SetMissingCarErrorIfNeeded(ref errorMessage);
                error |= SetMissingTrackErrorIfNeeded(ref errorMessage);
            } else {
                error = false;
                errorMessage = "Information�s missing";
            }

            if (error) {
                Status = ServerStatus.Error;
                ErrorMessage = errorMessage;
            } else if (Status == ServerStatus.Error){
                Status = ServerStatus.Unloaded;
                ErrorMessage = errorMessage;
            }

            var seconds = (int)Game.ConditionProperties.GetSeconds(information.Time);
            Time = $@"{seconds / 60 / 60:D2}:{seconds / 60 % 60:D2}";
            SessionEnd = DateTime.Now + TimeSpan.FromSeconds(information.TimeLeft - Math.Round(information.Timestamp / 1000d));

            Sessions = information.SessionTypes?.Select((x, i) => new Session {
                IsActive = x == information.Session,
                Duration = information.Durations?.ElementAtOrDefault(i) ?? 0,
                Type = (Game.SessionType)x
            }).ToList();

            BookingMode = !information.PickUp;
        }

        private bool SetMissingCarErrorIfNeeded(ref string errorMessage) {
            if (!IsFullyLoaded || CarsOrTheirIds == null) return false;

            var list = CarsOrTheirIds.Where(x => !x.CarExists).Select(x => x.CarId).ToList();
            if (!list.Any()) return false;
            errorMessage += (list.Count == 1
                    ? string.Format(ToolsStrings.Online_Server_CarIsMissing, IdToBb(list[0]))
                    : string.Format(ToolsStrings.Online_Server_CarsAreMissing, list.Select(x => IdToBb(x)).JoinToString(@", "))) + Environment.NewLine;
            return true;
        }

        private bool SetMissingTrackErrorIfNeeded(ref string errorMessage) {
            if (!IsFullyLoaded || Track != null) return false;
            errorMessage += string.Format(ToolsStrings.Online_Server_TrackIsMissing, IdToBb(TrackId, false)) + Environment.NewLine;
            return true;
        }

        private DateTime _previousUpdateTime;

        public DateTime PreviousUpdateTime {
            get { return _previousUpdateTime; }
            set {
                if (Equals(value, _previousUpdateTime)) return;
                _previousUpdateTime = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Update current entry using new information.
        /// </summary>
        /// <param name="information"></param>
        /// <returns>True if update is possible and was done, false if 
        /// changes require to recreate whole ServerEntry</returns>
        public bool UpdateValues(ServerInformation information) {
            if (Ip != information.Ip) {
                Logging.Warning($"Can�t update server: IP changed (from {Ip} to {information.Ip})");
                return false;
            }

            if (PortHttp != information.PortHttp) {
                Logging.Warning($"Can�t update server: main port changed (from {PortHttp} to {information.PortHttp})");
                return false;
            }

            SetSomeProperties(information);
            return true;
        }

        private bool _passwordRequired;

        public bool PasswordRequired {
            get { return _passwordRequired; }
            set {
                if (Equals(value, _passwordRequired)) return;
                _passwordRequired = value;
                OnPropertyChanged();

                _wrongPassword = false;
                OnPropertyChanged(nameof(WrongPassword));
                _joinCommand?.RaiseCanExecuteChanged();
            }
        }

        private const string PasswordStorageKeyBase = "__smt_pw";

        private string PasswordStorageKey => $@"{PasswordStorageKeyBase}_{Id}";

        private string _password;

        public string Password {
            get { return _password; }
            set {
                if (Equals(value, _password)) return;
                _password = value;
                ValuesStorage.SetEncrypted(PasswordStorageKey, value);
                OnPropertyChanged();

                _wrongPassword = false;
                OnPropertyChanged(nameof(WrongPassword));
                _joinCommand?.RaiseCanExecuteChanged();
            }
        }

        private bool _wrongPassword;

        public bool WrongPassword {
            get { return _wrongPassword; }
            set {
                if (Equals(value, _wrongPassword)) return;
                _wrongPassword = value;
                OnPropertyChanged();
                _joinCommand?.RaiseCanExecuteChanged();
            }
        }

        private string _country;

        public string Country {
            get { return _country; }
            set {
                if (value == @"na") value = ToolsStrings.Common_NA;
                if (Equals(value, _country)) return;
                _country = value;
                OnPropertyChanged();
            }
        }

        private string _countryId;

        public string CountryId {
            get { return _countryId; }
            set {
                if (value == @"na") value = "";
                if (Equals(value, _countryId)) return;
                _countryId = value;
                OnPropertyChanged();
            }
        }

        private bool _bookingMode;

        public bool BookingMode {
            get { return _bookingMode; }
            set {
                if (Equals(value, _bookingMode)) return;
                _bookingMode = value;
                OnPropertyChanged();
                _joinCommand?.RaiseCanExecuteChanged();

                if (!value) {
                    DisposeHelper.Dispose(ref _ui);
                }
            }
        }

        private int _currentDriversCount;

        public int CurrentDriversCount {
            get { return _currentDriversCount; }
            set {
                if (Equals(value, _currentDriversCount)) return;
                _currentDriversCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(DisplayClients));
            }
        }

        public bool IsEmpty => CurrentDriversCount == 0;

        private ServerActualInformation _actualInformation;

        public ServerActualInformation ActualInformation {
            get { return _actualInformation; }
            set {
                if (Equals(value, _actualInformation)) return;
                _actualInformation = value;
                OnPropertyChanged();
            }
        }

        private string _time;

        public string Time {
            get { return _time; }
            set {
                if (Equals(value, _time)) return;
                _time = value;
                OnPropertyChanged();
            }
        }

        private DateTime _sessionEnd;

        public DateTime SessionEnd {
            get { return _sessionEnd; }
            set {
                if (Equals(value, _sessionEnd)) return;
                _sessionEnd = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTimeLeft));
            }
        }

        private Game.SessionType? _currentSessionType;

        public Game.SessionType? CurrentSessionType {
            get { return _currentSessionType; }
            set {
                if (Equals(value, _currentSessionType)) return;
                _currentSessionType = value;
                OnPropertyChanged();
            }
        }

        public string DisplayTimeLeft {
            get {
                var now = DateTime.Now;
                return CurrentSessionType == Game.SessionType.Race ? ToolsStrings.Online_Server_SessionInProcess
                        : SessionEnd <= now ? ToolsStrings.Online_Server_SessionEnded : (SessionEnd - now).ToProperString();
            }
        }

        public void OnTick() {
            OnPropertyChanged(nameof(DisplayTimeLeft));
            if (IsBooked && BookingErrorMessage == null) {
                OnPropertyChanged(nameof(BookingTimeLeft));
            }
        }

        public void OnSessionEndTick() {
            OnPropertyChanged(nameof(SessionEnd));
        }

        private ServerStatus _status;

        public ServerStatus Status {
            get { return _status; }
            set {
                if (Equals(value, _status)) return;
                _status = value;
                OnPropertyChanged();

                _joinCommand?.RaiseCanExecuteChanged();
                _addToRecentCommand?.RaiseCanExecuteChanged();

                if (value != ServerStatus.Loading) {
                    HasErrors = value == ServerStatus.Error;
                }
            }
        }

        private bool _hasErrors;

        public bool HasErrors {
            get { return _hasErrors; }
            set {
                if (Equals(value, _hasErrors)) return;
                _hasErrors = value;
                OnPropertyChanged();
            }
        }

        private string _errorMessage;

        [CanBeNull]
        public string ErrorMessage {
            get { return _errorMessage; }
            set {
                if (Equals(value, _errorMessage)) return;
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        private int _capacity;

        public int Capacity {
            get { return _capacity; }
            set {
                if (Equals(value, _capacity)) return;
                _capacity = value;
                OnPropertyChanged();
            }
        }

        public string DisplayClients => $@"{CurrentDriversCount}/{Capacity}";

        private long? _ping;

        public long? Ping {
            get { return _ping; }
            set {
                if (Equals(value, _ping)) return;
                _ping = value;
                OnPropertyChanged();
            }
        }

        private bool _isAvailable;

        public bool IsAvailable {
            get { return _isAvailable; }
            set {
                if (Equals(value, _isAvailable)) return;
                _isAvailable = value;
                OnPropertyChanged();
                _joinCommand?.RaiseCanExecuteChanged();
            }
        }

        private string _trackId;

        [CanBeNull]
        public string TrackId {
            get { return _trackId; }
            set {
                if (Equals(value, _trackId)) return;
                _trackId = value;
                OnPropertyChanged();
            }
        }

        private string[] _carIds;

        [CanBeNull]
        public string[] CarIds {
            get { return _carIds; }
            set {
                if (Equals(value, _carIds)) return;
                _carIds = value;
                OnPropertyChanged();
            }
        }

        [CanBeNull]
        private TrackObjectBase _track;

        [CanBeNull]
        public TrackObjectBase Track {
            get { return _track; }
            set {
                if (Equals(value, _track)) return;
                _track = value;
                OnPropertyChanged();
            }
        }

        private List<CarOrOnlyCarIdEntry> _carsOrTheirIds;

        [CanBeNull]
        public List<CarOrOnlyCarIdEntry> CarsOrTheirIds {
            get { return _carsOrTheirIds; }
            set {
                if (Equals(value, _carsOrTheirIds)) return;
                _carsOrTheirIds = value;
                OnPropertyChanged();
            }
        }

        [CanBeNull]
        private BetterObservableCollection<CarEntry> _cars;

        [CanBeNull]
        public BetterObservableCollection<CarEntry> Cars {
            get { return _cars; }
            set {
                if (Equals(value, _cars)) return;
                _cars = value;
                OnPropertyChanged();
            }
        }

        [CanBeNull]
        private ListCollectionView _carsView;

        [CanBeNull]
        public ListCollectionView CarsView {
            get { return _carsView; }
            set {
                if (Equals(value, _carsView)) return;
                _carsView = value;
                OnPropertyChanged();
            }
        }

        private static AcItemWrapper GetCarWrapper([NotNull] string informationId) {
            return CarsManager.Instance.GetWrapperById(informationId);
        }

        private static TrackObjectBase GetTrack([NotNull] string informationId) {
            return TracksManager.Instance.GetLayoutByKunosId(informationId);
        }

        public int Compare(object x, object y) {
            return string.Compare(((CarEntry)x).CarObject.DisplayName, ((CarEntry)y).CarObject.DisplayName, StringComparison.CurrentCulture);
        }

        private static string IdToBb(string id, bool car = true) {
            if (car) return string.Format(ToolsStrings.Online_Server_MissingCarBbCode, id);

            id = Regex.Replace(id, @"-([^-]+)$", "/$1");
            if (!id.Contains(@"/")) id = $@"{id}/{id}";
            return string.Format(ToolsStrings.Online_Server_MissingTrackBbCode, id);
        }

        [NotNull]
        public BetterObservableCollection<CurrentDriver> CurrentDrivers { get; } = new BetterObservableCollection<CurrentDriver>();

        private List<Session> _sessions;

        [CanBeNull]
        public List<Session> Sessions {
            get { return _sessions; }
            set {
                if (Equals(value, _sessions)) return;
                _sessions = value;
                OnPropertyChanged();
                CurrentSessionType = Sessions?.FirstOrDefault(x => x.IsActive)?.Type;
                _joinCommand?.RaiseCanExecuteChanged();
            }
        }

        public enum UpdateMode {
            Lite,
            Normal,
            Full
        }

        public Task Update(UpdateMode mode, bool background = false) {
            if (!background) {
                Status = ServerStatus.Loading;
                IsAvailable = false;
            }

            return UpdateInner(mode, background);
        }

        private double _updateProgress;

        public double UpdateProgress {
            get { return _updateProgress; }
            set {
                if (Equals(value, _updateProgress)) return;
                _updateProgress = value;
                OnPropertyChanged();
            }
        }

        private string _updateProgressMessage;

        public string UpdateProgressMessage {
            get { return _updateProgressMessage; }
            set {
                if (Equals(value, _updateProgressMessage)) return;
                _updateProgressMessage = value;
                OnPropertyChanged();
            }
        }

        private async Task UpdateInner(UpdateMode mode, bool background) {
            var errorMessage = "";

            try {
                if (!background) {
                    CurrentDrivers.Clear();
                    OnPropertyChanged(nameof(CurrentDrivers));

                    Status = ServerStatus.Loading;
                    IsAvailable = false;
                }

                var informationUpdated = false;
                if (!IsFullyLoaded) {
                    UpdateProgress = 0.1;
                    UpdateProgressMessage = "Loading actual server information�";

                    var newInformation = await GetInformation();
                    if (newInformation == null) {
                        errorMessage = "Can�t get any server information";
                        return;
                    } else if (!UpdateValues(newInformation)) {
                        errorMessage = ToolsStrings.Online_Server_NotImplemented;
                        return;
                    }

                    informationUpdated = true;
                }

                SetMissingTrackErrorIfNeeded(ref errorMessage);
                SetMissingCarErrorIfNeeded(ref errorMessage);
                if (!string.IsNullOrWhiteSpace(errorMessage)) return;

                if (OriginsFromKunos && SteamIdHelper.Instance.Value == null) {
                    throw new InformativeException(ToolsStrings.Common_SteamIdIsMissing);
                }

                if (mode == UpdateMode.Full) {
                    UpdateProgress = 0.2;
                    UpdateProgressMessage = "Loading actual server information�";
                    var newInformation = await GetInformation(informationUpdated);
                    if (newInformation == null) {
                        if (!informationUpdated) {
                            errorMessage = ToolsStrings.Online_Server_CannotRefresh;
                            return;
                        }
                    } else if (!UpdateValues(newInformation)) {
                        errorMessage = ToolsStrings.Online_Server_NotImplemented;
                        return;
                    }
                }

                UpdateProgress = 0.3;
                UpdateProgressMessage = "Pinging server�";
                var pair = SettingsHolder.Online.ThreadsPing
                        ? await Task.Run(() => KunosApiProvider.TryToPingServer(Ip, Port, SettingsHolder.Online.PingTimeout))
                        : await KunosApiProvider.TryToPingServerAsync(Ip, Port, SettingsHolder.Online.PingTimeout);
                if (pair != null) {
                    Ping = (long)pair.Item2.TotalMilliseconds;
                } else {
                    Ping = null;
                    errorMessage = ToolsStrings.Online_Server_CannotPing;
                    return;
                }

                UpdateProgress = 0.4;
                UpdateProgressMessage = "Loading players list�";
                var information = await KunosApiProvider.TryToGetCurrentInformationAsync(Ip, PortHttp);
                if (information == null) {
                    errorMessage = ToolsStrings.Online_Server_Unavailable;
                    return;
                }

                ActualInformation = information;
                if (CurrentDrivers.ReplaceIfDifferBy(from x in information.Cars
                                                     where x.IsConnected
                                                     select new CurrentDriver {
                                                         Name = x.DriverName,
                                                         Team = x.DriverTeam,
                                                         CarId = x.CarId,
                                                         CarSkinId = x.CarSkinId
                                                     })) {
                    OnPropertyChanged(nameof(CurrentDrivers));
                }

                if (CarsOrTheirIds == null) {
                    // This is not supposed to happen
                    errorMessage = "Data is still missing";
                    return;
                }

                // CurrentDriversCount = information.Cars.Count(x => x.IsConnected);

                List<CarObject> carObjects;
                if (CarsOrTheirIds.Select(x => x.CarObjectWrapper).Any(x => x?.IsLoaded == false)) {
                    UpdateProgress = 0.5;
                    UpdateProgressMessage = "Loading cars�";
                    await Task.Delay(50);
                    carObjects = new List<CarObject>(CarsOrTheirIds.Count);

                    var i = 0;
                    foreach (var carOrOnlyCarIdEntry in CarsOrTheirIds.Select(x => x.CarObjectWrapper).Where(x => x != null)) {
                        UpdateProgress = 0.5 + 0.2 * i++ / CarsOrTheirIds.Count;
                        UpdateProgressMessage = $"Loading cars ({carOrOnlyCarIdEntry.Id})�";
                        var loaded = await carOrOnlyCarIdEntry.LoadedAsync();
                        carObjects.Add((CarObject)loaded);
                    }
                } else {
                    carObjects = (from x in CarsOrTheirIds
                                  where x.CarObjectWrapper != null
                                  select (CarObject)x.CarObjectWrapper.Value).ToList();
                }
                
                {
                    var i = 0;
                    var l = carObjects.Count(x => !x.SkinsManager.IsLoaded);
                    foreach (var carObject in carObjects.Where(x => !x.SkinsManager.IsLoaded)) {
                        UpdateProgress = 0.7 + 0.2 * i++ / l;
                        
                        UpdateProgressMessage = $"Loading {carObject.DisplayName} skins�";
                        await Task.Delay(50);
                        await carObject.SkinsManager.EnsureLoadedAsync();
                    }
                }

                List<CarEntry> cars;
                if (BookingMode) {
                    cars = CarsOrTheirIds.Select(x => x.CarObject == null ? null : new CarEntry(x.CarObject) {
                        AvailableSkin = x.CarObject.SelectedSkin
                    }).ToList();
                } else {
                    cars = information.Cars.Where(x => x.IsEntryList)
                                      .GroupBy(x => x.CarId)
                                      .Select(g => {
                                          var group = g.ToList();
                                          var id = group[0].CarId;
                                          var existing = Cars?.GetByIdOrDefault(id);
                                          if (existing != null) {
                                              var car = existing.CarObject;
                                              var availableSkinId = group.FirstOrDefault(y => y.IsConnected == false)?.CarSkinId;
                                              existing.Total = group.Count;
                                              existing.Available = group.Count(y => !y.IsConnected && y.IsEntryList);
                                              existing.AvailableSkin = availableSkinId == null
                                                      ? null : availableSkinId == string.Empty ? car.GetFirstSkinOrNull() : car.GetSkinById(availableSkinId);
                                              return existing;
                                          } else {
                                              var car = carObjects.GetByIdOrDefault(id, StringComparison.OrdinalIgnoreCase);
                                              if (car == null) return null;

                                              var availableSkinId = group.FirstOrDefault(y => y.IsConnected == false)?.CarSkinId;
                                              return new CarEntry(car) {
                                                  Total = group.Count,
                                                  Available = group.Count(y => !y.IsConnected && y.IsEntryList),
                                                  AvailableSkin = availableSkinId == null ? null : availableSkinId == string.Empty
                                                          ? car.GetFirstSkinOrNull() : car.GetSkinById(availableSkinId)
                                              };
                                          }
                                      }).ToList();
                }

                if (cars.Contains(null)) {
                    errorMessage = ToolsStrings.Online_Server_CarsDoNotMatch;
                    return;
                }

                var changed = true;
                if (Cars == null || CarsView == null) {
                    Cars = new BetterObservableCollection<CarEntry>(cars);
                    CarsView = new ListCollectionView(Cars) { CustomSort = this };
                    CarsView.CurrentChanged += SelectedCarChanged;
                } else {
                    // temporary removing listener to avoid losing selected car
                    CarsView.CurrentChanged -= SelectedCarChanged;
                    if (Cars.ReplaceIfDifferBy(cars)) {
                        OnPropertyChanged(nameof(Cars));
                    } else {
                        changed = false;
                    }

                    CarsView.CurrentChanged += SelectedCarChanged;
                }

                if (changed) {
                    LoadSelectedCar();
                }
            } catch (InformativeException e) {
                errorMessage = $@"{e.Message}.";
            } catch (Exception e) {
                errorMessage = string.Format(ToolsStrings.Online_Server_UnhandledError, e.Message);
                Logging.Warning("UpdateInner(): " + e);
            } finally {
                UpdateProgressMessage = null;
                ErrorMessage = errorMessage;
                if (!string.IsNullOrWhiteSpace(errorMessage)) {
                    Status = ServerStatus.Error;
                } else if (Status == ServerStatus.Loading) {
                    Status = ServerStatus.Ready;
                }

                AvailableUpdate();
            }
        }

        private string _nonAvailableReason;

        public string NonAvailableReason {
            get { return _nonAvailableReason; }
            set {
                if (Equals(value, _nonAvailableReason)) return;
                _nonAvailableReason = value;
                OnPropertyChanged();
            }
        }

        private string GetNonAvailableReason() {
            if (!IsFullyLoaded || Sessions == null) return "Can�t get any information";
            if (Status != ServerStatus.Ready) return "CM isn�t ready";

            var currentItem = CarsView?.CurrentItem as CarEntry;
            if (currentItem == null) return "Car isn�t selected";

            if (PasswordRequired) {
                if (WrongPassword) return ToolsStrings.ArchiveInstallator_PasswordIsInvalid;
                if (string.IsNullOrEmpty(Password)) return ToolsStrings.ArchiveInstallator_PasswordIsRequired;
            }

            if (BookingMode) {
                var currentSession = Sessions.FirstOrDefault(x => x.IsActive);
                if (currentSession?.Type != Game.SessionType.Booking) return "Wait for the next booking";
            } else {
                if (!currentItem.IsAvailable) return "Selected car isn�t available";
            }

            return null;
        }

        private void AvailableUpdate() {
            NonAvailableReason = GetNonAvailableReason();
            IsAvailable = NonAvailableReason == null;
        }

        private void LoadSelectedCar() {
            if (Cars == null || CarsView == null) return;

            var selected = LimitedStorage.Get(LimitedSpace.OnlineSelectedCar, Id);
            var firstAvailable = (selected == null ? null : Cars.GetByIdOrDefault(selected)) ?? Cars.FirstOrDefault(x => x.IsAvailable);
            CarsView.MoveCurrentTo(firstAvailable);
        }

        private CarEntry _selectedCarEntry;

        [CanBeNull]
        public CarEntry SelectedCarEntry {
            get { return _selectedCarEntry; }
            set {
                if (Equals(value, _selectedCarEntry)) return;
                _selectedCarEntry = value;
                OnPropertyChanged();
            }
        }

        private void SelectedCarChanged(object sender, EventArgs e) {
            SelectedCarEntry = CarsView?.CurrentItem as CarEntry;

            var selectedCar = SelectedCarEntry?.CarObject;
            LimitedStorage.Set(LimitedSpace.OnlineSelectedCar, Id, selectedCar?.Id);
            AvailableUpdate();
        }

        private CommandBase _addToRecentCommand;

        public ICommand AddToRecentCommand => _addToRecentCommand ?? (_addToRecentCommand = new DelegateCommand(() => {
            //RecentManagerOld.Instance.AddRecentServer(OriginalInformation);
        }, () => Status == ServerStatus.Ready /*&& RecentManagerOld.Instance.GetWrapperById(Id) == null*/));

        private CommandBase _joinCommand;

        public ICommand JoinCommand => _joinCommand ?? (_joinCommand = new AsyncCommand<object>(Join,
                o => ReferenceEquals(o, ForceJoin) || IsAvailable));

        private CommandBase _cancelBookingCommand;

        public ICommand CancelBookingCommand => _cancelBookingCommand ?? (_cancelBookingCommand = new AsyncCommand(CancelBooking, () => IsBooked));

        [CanBeNull]
        private IBookingUi _ui;

        public static readonly object ActualJoin = new object();
        public static readonly object ForceJoin = new object();

        [CanBeNull]
        public CarSkinObject GetSelectedCarSkin() {
            return (CarsView?.CurrentItem as CarEntry)?.AvailableSkin;
        }

        private bool _isBooked;

        public bool IsBooked {
            get { return _isBooked; }
            set {
                if (Equals(value, _isBooked)) return;
                _isBooked = value;
                OnPropertyChanged();
                _cancelBookingCommand?.RaiseCanExecuteChanged();
            }
        }

        private DateTime _startTime;

        public DateTime StartTime {
            get { return _startTime; }
            set {
                if (Equals(value, _startTime)) return;
                _startTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BookingTimeLeft));
            }
        }

        private string _bookingErrorMessage;

        public string BookingErrorMessage {
            get { return _bookingErrorMessage; }
            set {
                if (Equals(value, _bookingErrorMessage)) return;
                _bookingErrorMessage = value;
                OnPropertyChanged();
            }
        }

        public TimeSpan BookingTimeLeft {
            get {
                var result = StartTime - DateTime.Now;
                return result <= TimeSpan.Zero ? TimeSpan.Zero : result;
            }
        }

        private async Task CancelBooking() {
            DisposeHelper.Dispose(ref _ui);

            if (!IsBooked) return;
            IsBooked = false;
            await Task.Run(() => KunosApiProvider.TryToUnbook(Ip, PortHttp));
        }

        private void PrepareBookingUi() {
            if (_ui == null) {
                _ui = _factory.Create();
                _ui.Show(this);
            }
        }

        private void ProcessBookingResponse(BookingResult response) {
            if (_ui?.CancellationToken.IsCancellationRequested == true) {
                CancelBooking().Forget();
                return;
            }

            if (response == null) {
                BookingErrorMessage = "Cannot get any response";
                return;
            }

            if (response.IsSuccessful) {
                StartTime = DateTime.Now + response.Left;
                BookingErrorMessage = null;
                IsBooked = response.IsSuccessful;
            } else {
                BookingErrorMessage = response.ErrorMessage;
                IsBooked = false;
            }

            _ui?.OnUpdate(response);
        }

        public async Task<bool> RebookSkin() {
            if (!IsBooked || !BookingMode || BookingTimeLeft < TimeSpan.FromSeconds(2) || CarIds == null) {
                return false;
            }

            var carEntry = CarsView?.CurrentItem as CarEntry;
            if (carEntry == null) return false;

            var carId = carEntry.CarObject.Id;
            var correctId = CarIds.FirstOrDefault(x => string.Equals(x, carId, StringComparison.OrdinalIgnoreCase));

            PrepareBookingUi();

            var result = await Task.Run(() => KunosApiProvider.TryToBook(Ip, PortHttp, Password, correctId, carEntry.AvailableSkin?.Id,
                    DriverName.GetOnline(), ""));
            if (result?.IsSuccessful != true) return false;

            ProcessBookingResponse(result);
            return true;
        }

        private async Task Join(object o) {
            var carEntry = CarsView?.CurrentItem as CarEntry;
            if (carEntry == null || CarIds == null) return;

            var carId = carEntry.CarObject.Id;
            var correctId = CarIds.FirstOrDefault(x => string.Equals(x, carId, StringComparison.OrdinalIgnoreCase));

            if (BookingMode && !ReferenceEquals(o, ActualJoin) && !ReferenceEquals(o, ForceJoin)) {
                if (_factory == null) {
                    Logging.Error("Booking: UI factory is missing");
                    return;
                }

                PrepareBookingUi();
                ProcessBookingResponse(await Task.Run(() => KunosApiProvider.TryToBook(Ip, PortHttp, Password, correctId, carEntry.AvailableSkin?.Id,
                        DriverName.GetOnline(), "")));
                return;
            }

            DisposeHelper.Dispose(ref _ui);
            IsBooked = false;
            BookingErrorMessage = null;

            var properties = new Game.StartProperties(new Game.BasicProperties {
                CarId = carId,
                CarSkinId = carEntry.AvailableSkin?.Id,
                TrackId = Track?.Id,
                TrackConfigurationId = Track?.LayoutId
            }, null, null, null, new Game.OnlineProperties {
                RequestedCar = correctId,
                ServerIp = Ip,
                ServerName = DisplayName,
                ServerPort = PortRace,
                ServerHttpPort = PortHttp,
                Guid = SteamIdHelper.Instance.Value,
                Password = Password
            });

            await GameWrapper.StartAsync(properties);
            var whatsGoingOn = properties.GetAdditional<AcLogHelper.WhatsGoingOn>();
            WrongPassword = whatsGoingOn?.Type == AcLogHelper.WhatsGoingOnType.OnlineWrongPassword;
            // if (whatsGoingOn == null) RecentManagerOld.Instance.AddRecentServer(OriginalInformation);
        }

        private ICommand _refreshCommand;

        public ICommand RefreshCommand => _refreshCommand ?? (_refreshCommand = new DelegateCommand(() => {
            Update(UpdateMode.Full).Forget();
        }));

        private static IAnyFactory<IBookingUi> _factory;

        public static void RegisterFactory(IAnyFactory<IBookingUi> factory) {
            _factory = factory;
        }

        public override string ToString() {
            return Id;
        }
    }
}