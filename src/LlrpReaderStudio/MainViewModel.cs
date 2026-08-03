using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LlrpReaderStudio.Core;
using LlrpSdk;

namespace LlrpReaderStudio;

internal sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ReaderFleetService fleet = new();
    private readonly Dictionary<Guid, ReaderItemViewModel> readerIndex = [];
    private ReaderItemViewModel? selectedReader;
    private int selectedPageIndex;
    private string profileName = "Reader";
    private string profileHost = "192.168.1.100";
    private string profilePort = "5084";
    private string statusMessage = "Add a reader profile to begin.";
    private string targetEpc = string.Empty;
    private TagMemoryBank memoryBank = TagMemoryBank.User;
    private string wordPointer = "0";
    private string wordCount = "6";
    private string accessPassword = "00000000";
    private string tagData = string.Empty;
    private ReaderSettings? settingsDraft;
    private string settingsOrigin = "No settings loaded";
    private bool includeInventoryDraft;
    private string antennas = "0";
    private string session = "0";
    private string population = "32";
    private string reportEvery = "1";
    private string filterEpc = string.Empty;
    private InventoryReportTrigger reportTrigger = InventoryReportTrigger.UponNTagsOrEndOfAiSpec;
    private InventoryStartTriggerType startTriggerType = InventoryStartTriggerType.None;
    private InventoryStopTriggerType stopTriggerType = InventoryStopTriggerType.None;
    private string startGpiPort = "1";
    private bool startGpiState;
    private string stopDuration = "0";
    private bool attachedDataEnabled;
    private string attachedMemoryBank = "2";
    private string attachedWordPointer = "0";
    private string attachedWordCount = "6";
    private string attachedAccessPassword = "00000000";
    private string gpoPort = "1";
    private bool gpoState;
    private string toiEpc = string.Empty;

    public MainViewModel()
    {
        fleet.ReaderStatusChanged += OnReaderStatusChanged;
        fleet.TagObserved += OnTagObserved;

        AddReaderCommand = new RelayCommand(AddReader);
        RemoveReaderCommand = new AsyncRelayCommand(RemoveReaderAsync, HasReader);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, HasReader);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, HasReader);
        StartInventoryCommand = new AsyncRelayCommand(StartInventoryAsync, HasReader);
        StartAllCommand = new AsyncRelayCommand(StartAllAsync, () => Readers.Count > 0);
        StopInventoryCommand = new AsyncRelayCommand(StopInventoryAsync, HasReader);
        StopAllCommand = new AsyncRelayCommand(StopAllAsync, () => Readers.Count > 0);
        ClearTagsCommand = new RelayCommand(ClearTags);
        ReadMemoryCommand = new AsyncRelayCommand(ReadMemoryAsync, HasReader);
        WriteMemoryCommand = new AsyncRelayCommand(WriteMemoryAsync, HasReader);
        QuerySettingsCommand = new AsyncRelayCommand(QuerySettingsAsync, HasReader);
        DefaultSettingsCommand = new AsyncRelayCommand(DefaultSettingsAsync, HasReader);
        ApplySettingsCommand = new AsyncRelayCommand(ApplySettingsAsync, () => HasReader() && settingsDraft is not null);
        SetGpoCommand = new AsyncRelayCommand(SetGpoAsync, HasReader);
        AddToiCommand = new RelayCommand(AddToi);
        ShowInventoryCommand = new RelayCommand(() => SelectedPageIndex = 0);
        ShowTagMemoryCommand = new RelayCommand(() => SelectedPageIndex = 1);
        ShowToiCommand = new RelayCommand(() => SelectedPageIndex = 2);
        ShowSettingsCommand = new RelayCommand(() => SelectedPageIndex = 3);
    }

    public ObservableCollection<ReaderItemViewModel> Readers { get; } = [];
    public ObservableCollection<TagRowViewModel> Tags { get; } = [];
    public ObservableCollection<string> TagsOfInterest { get; } = [];
    public IReadOnlyList<TagMemoryBank> MemoryBanks { get; } = Enum.GetValues<TagMemoryBank>();
    public IReadOnlyList<InventoryReportTrigger> ReportTriggers { get; } = Enum.GetValues<InventoryReportTrigger>();
    public IReadOnlyList<InventoryStartTriggerType> StartTriggerTypes { get; } = Enum.GetValues<InventoryStartTriggerType>();
    public IReadOnlyList<InventoryStopTriggerType> StopTriggerTypes { get; } = Enum.GetValues<InventoryStopTriggerType>();

    public ReaderItemViewModel? SelectedReader
    {
        get => selectedReader;
        set
        {
            if (SetProperty(ref selectedReader, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int SelectedPageIndex
    {
        get => selectedPageIndex;
        set => SetProperty(ref selectedPageIndex, value);
    }

    public string ProfileName { get => profileName; set => SetProperty(ref profileName, value); }
    public string ProfileHost { get => profileHost; set => SetProperty(ref profileHost, value); }
    public string ProfilePort { get => profilePort; set => SetProperty(ref profilePort, value); }
    public string StatusMessage { get => statusMessage; set => SetProperty(ref statusMessage, value); }
    public string TargetEpc { get => targetEpc; set => SetProperty(ref targetEpc, value); }
    public TagMemoryBank MemoryBank { get => memoryBank; set => SetProperty(ref memoryBank, value); }
    public string WordPointer { get => wordPointer; set => SetProperty(ref wordPointer, value); }
    public string WordCount { get => wordCount; set => SetProperty(ref wordCount, value); }
    public string AccessPassword { get => accessPassword; set => SetProperty(ref accessPassword, value); }
    public string TagData { get => tagData; set => SetProperty(ref tagData, value); }
    public string SettingsOrigin { get => settingsOrigin; set => SetProperty(ref settingsOrigin, value); }
    public bool IncludeInventoryDraft { get => includeInventoryDraft; set => SetProperty(ref includeInventoryDraft, value); }
    public string Antennas { get => antennas; set => SetProperty(ref antennas, value); }
    public string Session { get => session; set => SetProperty(ref session, value); }
    public string Population { get => population; set => SetProperty(ref population, value); }
    public string ReportEvery { get => reportEvery; set => SetProperty(ref reportEvery, value); }
    public string FilterEpc { get => filterEpc; set => SetProperty(ref filterEpc, value); }
    public InventoryReportTrigger ReportTrigger { get => reportTrigger; set => SetProperty(ref reportTrigger, value); }
    public InventoryStartTriggerType StartTriggerType { get => startTriggerType; set => SetProperty(ref startTriggerType, value); }
    public InventoryStopTriggerType StopTriggerType { get => stopTriggerType; set => SetProperty(ref stopTriggerType, value); }
    public string StartGpiPort { get => startGpiPort; set => SetProperty(ref startGpiPort, value); }
    public bool StartGpiState { get => startGpiState; set => SetProperty(ref startGpiState, value); }
    public string StopDuration { get => stopDuration; set => SetProperty(ref stopDuration, value); }
    public bool AttachedDataEnabled { get => attachedDataEnabled; set => SetProperty(ref attachedDataEnabled, value); }
    public string AttachedMemoryBank { get => attachedMemoryBank; set => SetProperty(ref attachedMemoryBank, value); }
    public string AttachedWordPointer { get => attachedWordPointer; set => SetProperty(ref attachedWordPointer, value); }
    public string AttachedWordCount { get => attachedWordCount; set => SetProperty(ref attachedWordCount, value); }
    public string AttachedAccessPassword { get => attachedAccessPassword; set => SetProperty(ref attachedAccessPassword, value); }
    public string GpoPort { get => gpoPort; set => SetProperty(ref gpoPort, value); }
    public bool GpoState { get => gpoState; set => SetProperty(ref gpoState, value); }
    public string ToiEpc { get => toiEpc; set => SetProperty(ref toiEpc, value); }

    public ICommand AddReaderCommand { get; }
    public ICommand RemoveReaderCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand StartInventoryCommand { get; }
    public ICommand StartAllCommand { get; }
    public ICommand StopInventoryCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand ClearTagsCommand { get; }
    public ICommand ReadMemoryCommand { get; }
    public ICommand WriteMemoryCommand { get; }
    public ICommand QuerySettingsCommand { get; }
    public ICommand DefaultSettingsCommand { get; }
    public ICommand ApplySettingsCommand { get; }
    public ICommand SetGpoCommand { get; }
    public ICommand AddToiCommand { get; }
    public ICommand ShowInventoryCommand { get; }
    public ICommand ShowTagMemoryCommand { get; }
    public ICommand ShowToiCommand { get; }
    public ICommand ShowSettingsCommand { get; }

    public async ValueTask DisposeAsync()
    {
        fleet.ReaderStatusChanged -= OnReaderStatusChanged;
        fleet.TagObserved -= OnTagObserved;
        await fleet.DisposeAsync();
    }

    private void AddReader()
    {
        RunSync(() =>
        {
            var profile = new ReaderProfile
            {
                Name = string.IsNullOrWhiteSpace(ProfileName) ? ProfileHost : ProfileName.Trim(),
                Host = ProfileHost.Trim(),
                Port = int.Parse(ProfilePort, CultureInfo.InvariantCulture),
            };
            ReaderStatus status = fleet.Add(profile);
            var item = new ReaderItemViewModel(status);
            readerIndex.Add(profile.Id, item);
            Readers.Add(item);
            SelectedReader = item;
            StatusMessage = $"Added {profile.Name}.";
        });
    }

    private async Task RemoveReaderAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            await fleet.RemoveAsync(reader.Id);
            readerIndex.Remove(reader.Id);
            Readers.Remove(reader);
            SelectedReader = Readers.FirstOrDefault();
            StatusMessage = $"Removed {reader.Name}.";
        });
    }

    private Task ConnectAsync() => WithReaderAsync(
        (id, token) => fleet.ConnectAsync(id, token),
        "Connected.");

    private Task DisconnectAsync() => WithReaderAsync(
        (id, token) => fleet.DisconnectAsync(id, token),
        "Disconnected.");

    private Task StartInventoryAsync() => WithReaderAsync(
        (id, token) => fleet.StartInventoryAsync(id, BuildInventory(), token),
        "Inventory started.");

    private Task StopInventoryAsync() => WithReaderAsync(
        (id, token) => fleet.StopInventoryAsync(id, token),
        "Inventory stopped.");

    private async Task StartAllAsync()
    {
        InventorySettings inventory = BuildInventory();
        await RunAsync(async () =>
        {
            foreach (ReaderItemViewModel reader in Readers.Where(static reader => reader.State == StudioReaderState.Connected))
            {
                await fleet.StartInventoryAsync(reader.Id, inventory);
            }
            StatusMessage = "Inventory started on all connected readers.";
        });
    }

    private async Task StopAllAsync()
    {
        await RunAsync(async () =>
        {
            foreach (ReaderItemViewModel reader in Readers.Where(static reader => reader.State == StudioReaderState.Inventorying))
            {
                await fleet.StopInventoryAsync(reader.Id);
            }
            StatusMessage = "Inventory stopped on all running readers.";
        });
    }

    private void ClearTags()
    {
        fleet.ClearTags();
        Tags.Clear();
        StatusMessage = "Aggregated tag observations cleared.";
    }

    private async Task ReadMemoryAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            ushort wordsToRead = ushort.Parse(WordCount, CultureInfo.InvariantCulture);
            if (wordsToRead is 0 or > 32)
            {
                throw new InvalidOperationException("Word count must be from 1 through 32.");
            }

            TagAccessResult result = await fleet.ReadTagMemoryAsync(reader.Id, new ReadTagRequest
            {
                Selection = BuildSelection(),
                AccessPassword = ParseUInt32Hex(AccessPassword),
                MemoryBank = MemoryBank,
                WordPointer = ushort.Parse(WordPointer, CultureInfo.InvariantCulture),
                WordCount = wordsToRead,
            });
            TagData = HexCodec.FormatWords(result.Operation.ReadData);
            StatusMessage = result.Operation.Success ? "Tag memory read completed." : result.Operation.Error ?? "Read failed.";
        });
    }

    private async Task WriteMemoryAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            ushort[] words = HexCodec.ParseWords(TagData);
            if (words.Length is 0 or > 32)
            {
                throw new InvalidOperationException("Write data must contain from 1 through 32 words.");
            }

            TagAccessResult result = await fleet.WriteTagMemoryAsync(reader.Id, new WriteTagRequest
            {
                Selection = BuildSelection(),
                AccessPassword = ParseUInt32Hex(AccessPassword),
                MemoryBank = MemoryBank,
                WordPointer = ushort.Parse(WordPointer, CultureInfo.InvariantCulture),
                WriteData = words,
            });
            StatusMessage = result.Operation.Success ? "Tag memory write completed." : result.Operation.Error ?? "Write failed.";
        });
    }

    private async Task QuerySettingsAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            ReaderSettingsSnapshot snapshot = await fleet.QuerySettingsAsync(reader.Id);
            LoadSettings(snapshot.Settings, $"Device snapshot · {snapshot.Inventory?.State.ToString() ?? "no managed inventory"}");
        });
    }

    private async Task DefaultSettingsAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            ReaderSettingsDefaults defaults = await fleet.GetDefaultSettingsAsync(reader.Id);
            LoadSettings(defaults.Settings, $"{defaults.Source} · {defaults.ProfileId}");
        });
    }

    private async Task ApplySettingsAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            ReaderSettings settings = (settingsDraft ?? new ReaderSettings()) with
            {
                Inventory = IncludeInventoryDraft ? BuildInventory() : null,
            };
            await fleet.ApplySettingsAsync(reader.Id, settings);
            settingsDraft = settings;
            SettingsOrigin += " · applied";
            StatusMessage = "Settings applied; inventory remains stopped until Start.";
        });
    }

    private async Task SetGpoAsync()
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            await fleet.SetGpoAsync(reader.Id, ushort.Parse(GpoPort, CultureInfo.InvariantCulture), GpoState);
            StatusMessage = $"GPO {GpoPort} set {(GpoState ? "high" : "low")}.";
        });
    }

    private void AddToi()
    {
        RunSync(() =>
        {
            string epc = HexCodec.FormatBytes(HexCodec.ParseBytes(ToiEpc));
            if (!TagsOfInterest.Contains(epc, StringComparer.OrdinalIgnoreCase))
            {
                TagsOfInterest.Add(epc);
            }
            ToiEpc = string.Empty;
        });
    }

    private void LoadSettings(ReaderSettings settings, string origin)
    {
        settingsDraft = settings;
        IncludeInventoryDraft = settings.Inventory is not null;
        InventorySettings inventory = settings.Inventory ?? new InventorySettings();
        Antennas = string.Join(",", inventory.AntennaIds);
        Session = inventory.Session.ToString(CultureInfo.InvariantCulture);
        Population = inventory.TagPopulationEstimate.ToString(CultureInfo.InvariantCulture);
        ReportEvery = inventory.ReportEveryNTags.ToString(CultureInfo.InvariantCulture);
        FilterEpc = inventory.Filters.Count == 1
            ? HexCodec.FormatBytes(inventory.Filters[0].Mask.ToArray())
            : string.Empty;
        ReportTrigger = inventory.Report.Trigger;
        StartTriggerType = inventory.StartTrigger.Type;
        StopTriggerType = inventory.StopTrigger.Type;
        StartGpiPort = inventory.StartTrigger.GpiPortNumber.ToString(CultureInfo.InvariantCulture);
        StartGpiState = inventory.StartTrigger.GpiState;
        StopDuration = inventory.StopTrigger.DurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        AttachedDataEnabled = inventory.AttachedData.Enabled;
        AttachedMemoryBank = inventory.AttachedData.MemoryBank.ToString(CultureInfo.InvariantCulture);
        AttachedWordPointer = inventory.AttachedData.WordPointer.ToString(CultureInfo.InvariantCulture);
        AttachedWordCount = inventory.AttachedData.WordCount.ToString(CultureInfo.InvariantCulture);
        AttachedAccessPassword = inventory.AttachedData.AccessPassword;
        SettingsOrigin = origin;
        StatusMessage = "Settings workspace loaded.";
        RaiseCommandStates();
    }

    private InventorySettings BuildInventory()
    {
        InventorySettings baseline = settingsDraft?.Inventory ?? new InventorySettings();
        IReadOnlyList<InventorySelectFilter> filters = string.IsNullOrWhiteSpace(FilterEpc)
            ? []
            : [new InventorySelectFilter
            {
                MemoryBank = 1,
                BitPointer = 32,
                Mask = HexCodec.ParseBytes(FilterEpc),
                MatchAction = InventorySelectAction.Select,
                NonMatchAction = InventorySelectAction.Unselect,
            }];
        return baseline with
        {
            AntennaIds = Antennas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static value => ushort.Parse(value, CultureInfo.InvariantCulture))
                .ToArray(),
            Session = byte.Parse(Session, CultureInfo.InvariantCulture),
            TagPopulationEstimate = ushort.Parse(Population, CultureInfo.InvariantCulture),
            ReportEveryNTags = ushort.Parse(ReportEvery, CultureInfo.InvariantCulture),
            Filters = filters,
            Report = baseline.Report with { Trigger = ReportTrigger },
            StartTrigger = baseline.StartTrigger with
            {
                Type = StartTriggerType,
                GpiPortNumber = ushort.Parse(StartGpiPort, CultureInfo.InvariantCulture),
                GpiState = StartGpiState,
            },
            StopTrigger = baseline.StopTrigger with
            {
                Type = StopTriggerType,
                DurationMilliseconds = uint.Parse(StopDuration, CultureInfo.InvariantCulture),
            },
            AttachedData = baseline.AttachedData with
            {
                Enabled = AttachedDataEnabled,
                MemoryBank = ushort.Parse(AttachedMemoryBank, CultureInfo.InvariantCulture),
                WordPointer = ushort.Parse(AttachedWordPointer, CultureInfo.InvariantCulture),
                WordCount = ushort.Parse(AttachedWordCount, CultureInfo.InvariantCulture),
                AccessPassword = AttachedAccessPassword,
            },
        };
    }

    private TagSelection BuildSelection()
    {
        byte[] data = HexCodec.ParseBytes(TargetEpc);
        if (data.Length == 0)
        {
            throw new InvalidOperationException("Enter an exact EPC before tag access.");
        }

        return new TagSelection
        {
            MemoryBank = TagMemoryBank.ElectronicProductCode,
            BitPointer = 32,
            BitLength = checked((ushort)(data.Length * 8)),
            Mask = Enumerable.Repeat((byte)0xFF, data.Length).ToArray(),
            Data = data,
        };
    }

    private static uint ParseUInt32Hex(string value) =>
        uint.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private bool HasReader() => SelectedReader is not null;
    private ReaderItemViewModel RequireReader() => SelectedReader ?? throw new InvalidOperationException("Select a reader first.");

    private async Task WithReaderAsync(
        Func<Guid, CancellationToken, Task> operation,
        string success)
    {
        ReaderItemViewModel reader = RequireReader();
        await RunAsync(async () =>
        {
            await operation(reader.Id, CancellationToken.None);
            StatusMessage = $"{reader.Name}: {success}";
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RunSync(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void OnReaderStatusChanged(object? sender, ReaderStatusChangedEventArgs args)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (readerIndex.TryGetValue(args.Status.Profile.Id, out ReaderItemViewModel? item))
            {
                item.Update(args.Status);
            }
            RaiseCommandStates();
        });
    }

    private void OnTagObserved(object? sender, FleetTagObservedEventArgs args)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TagRowViewModel? existing = Tags.FirstOrDefault(tag =>
                string.Equals(tag.Epc, args.Aggregate.Epc, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Tags.Insert(0, new TagRowViewModel(args.Aggregate));
            }
            else
            {
                existing.Update(args.Aggregate);
                int index = Tags.IndexOf(existing);
                if (index > 0)
                {
                    Tags.Move(index, 0);
                }
            }
        });
    }

    private void RaiseCommandStates()
    {
        foreach (ICommand command in new[]
        {
            RemoveReaderCommand, ConnectCommand, DisconnectCommand, StartInventoryCommand, StopInventoryCommand,
            ReadMemoryCommand, WriteMemoryCommand, QuerySettingsCommand, DefaultSettingsCommand,
            ApplySettingsCommand, SetGpoCommand, StartAllCommand, StopAllCommand,
        })
        {
            if (command is AsyncRelayCommand asyncCommand)
            {
                asyncCommand.RaiseCanExecuteChanged();
            }
        }
    }
}

internal sealed class ReaderItemViewModel : ObservableObject
{
    private StudioReaderState state;
    private string details = string.Empty;

    public ReaderItemViewModel(ReaderStatus status)
    {
        Id = status.Profile.Id;
        Name = status.Profile.Name;
        Endpoint = $"{status.Profile.Host}:{status.Profile.Port}";
        Update(status);
    }

    public Guid Id { get; }
    public string Name { get; }
    public string Endpoint { get; }
    public StudioReaderState State { get => state; private set => SetProperty(ref state, value); }
    public string Details { get => details; private set => SetProperty(ref details, value); }

    public void Update(ReaderStatus status)
    {
        State = status.State;
        Details = status.Error ?? string.Join(" · ", new[] { status.Model, status.Firmware }.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }
}

internal sealed class TagRowViewModel : ObservableObject
{
    private long readCount;
    private DateTimeOffset lastSeen;
    private sbyte? lastRssi;
    private string readers = string.Empty;
    private string antennas = string.Empty;

    public TagRowViewModel(TagObservation observation)
    {
        Epc = observation.Epc;
        FirstSeen = observation.FirstSeen;
        Update(observation);
    }

    public string Epc { get; }
    public DateTimeOffset FirstSeen { get; }
    public long ReadCount { get => readCount; private set => SetProperty(ref readCount, value); }
    public DateTimeOffset LastSeen { get => lastSeen; private set => SetProperty(ref lastSeen, value); }
    public sbyte? LastRssi { get => lastRssi; private set => SetProperty(ref lastRssi, value); }
    public string Readers { get => readers; private set => SetProperty(ref readers, value); }
    public string Antennas { get => antennas; private set => SetProperty(ref antennas, value); }

    public void Update(TagObservation observation)
    {
        ReadCount = observation.ReadCount;
        LastSeen = observation.LastSeen;
        LastRssi = observation.LastRssi;
        Readers = string.Join(", ", observation.Readers);
        Antennas = string.Join(", ", observation.Antennas);
    }
}
