using LlrpDevice.Abstractions;

namespace LlrpDevice.Virtual;

/// <summary>Mutable per-device tag state used by the virtual implementation.</summary>
public sealed class VirtualTagStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, VirtualTagState> _tags;

    public VirtualTagStore(IEnumerable<VirtualTagDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _tags = new Dictionary<string, VirtualTagState>(StringComparer.OrdinalIgnoreCase);
        foreach (VirtualTagDefinition definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            string key = Convert.ToHexString(definition.ElectronicProductCode.Span);
            if (!_tags.TryAdd(key, new VirtualTagState(definition)))
            {
                throw new ArgumentException($"The virtual tag EPC {key} is duplicated.", nameof(definitions));
            }
        }
    }

    public IReadOnlyList<TagObservation> Snapshot()
    {
        lock (_gate)
        {
            return _tags.Values
                .Where(static tag => !tag.Killed)
                .Select(static tag => tag.ToObservation())
                .ToArray();
        }
    }

    public TagObservation? MarkSeen(ReadOnlySpan<byte> epc, DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            if (!TryGet(epc, out VirtualTagState? tag) || tag.Killed)
            {
                return null;
            }

            tag.MarkSeen(timestampUtc);
            return tag.ToObservation();
        }
    }

    public bool PassesStateAwareSingulation(
        ReadOnlySpan<byte> epc,
        LlrpInventorySingulationControl? singulation)
    {
        if (singulation?.StateAwareSingulation is not { } stateAware)
        {
            return true;
        }

        lock (_gate)
        {
            return TryGet(epc, out VirtualTagState? tag) && tag.PassesStateAwareSingulation(singulation, stateAware);
        }
    }

    public void ApplyStateAwareFilter(
        ReadOnlySpan<byte> epc,
        LlrpInventoryFilter filter,
        bool match)
    {
        if (filter.StateTarget is not { } target || filter.StateAction is not { } action)
        {
            return;
        }

        lock (_gate)
        {
            if (TryGet(epc, out VirtualTagState? tag))
            {
                tag.ApplyStateAwareFilter(target, action, match);
            }
        }
    }

    public IReadOnlyList<ushort> ReadWords(ReadOnlySpan<byte> epc, LlrpTagMemoryBank bank, int pointer, int count)
    {
        lock (_gate)
        {
            if (!TryGet(epc, out VirtualTagState tag))
            {
                return [];
            }

            return tag.ReadWords(bank, pointer, count);
        }
    }

    public bool WriteWords(ReadOnlySpan<byte> epc, LlrpTagMemoryBank bank, int pointer, IReadOnlyList<ushort> words)
    {
        lock (_gate)
        {
            return TryGet(epc, out VirtualTagState tag) && tag.WriteWords(bank, pointer, words);
        }
    }

    public bool BlockErase(ReadOnlySpan<byte> epc, LlrpTagMemoryBank bank, int pointer, int count)
    {
        lock (_gate)
        {
            return TryGet(epc, out VirtualTagState tag) && tag.BlockErase(bank, pointer, count);
        }
    }

    public bool Lock(ReadOnlySpan<byte> epc, IReadOnlyList<LlrpTagLockRequest> requests, uint accessPassword)
    {
        lock (_gate)
        {
            return TryGet(epc, out VirtualTagState tag) && tag.Lock(requests, accessPassword);
        }
    }

    public bool Kill(ReadOnlySpan<byte> epc, uint killPassword)
    {
        lock (_gate)
        {
            return TryGet(epc, out VirtualTagState tag) && tag.Kill(killPassword);
        }
    }

    public bool TryGetMemoryBytes(ReadOnlySpan<byte> epc, LlrpTagMemoryBank bank, out ReadOnlyMemory<byte> bytes)
    {
        lock (_gate)
        {
            if (!TryGet(epc, out VirtualTagState tag))
            {
                bytes = ReadOnlyMemory<byte>.Empty;
                return false;
            }

            bytes = tag.GetMemoryBytes(bank);
            return true;
        }
    }

    private bool TryGet(ReadOnlySpan<byte> epc, out VirtualTagState tag)
    {
        if (_tags.TryGetValue(Convert.ToHexString(epc), out VirtualTagState? found) && found is not null)
        {
            tag = found;
            return true;
        }

        tag = null!;
        return false;
    }

    private sealed class VirtualTagState
    {
        private readonly VirtualTagDefinition _definition;
        private readonly ushort[] _userMemory;
        private readonly Dictionary<LlrpTagMemoryBank, LlrpTagLockPrivilege> _locks = [];
        private bool _selected;
        private readonly bool[] _sessionStateB = new bool[4];
        private DateTimeOffset? _firstSeenUtc;
        private DateTimeOffset? _lastSeenUtc;
        private uint _seenCount;

        public VirtualTagState(VirtualTagDefinition definition)
        {
            _definition = definition;
            _userMemory = definition.UserMemory.ToArray();
        }

        public bool Killed { get; private set; }

        public bool PassesStateAwareSingulation(
            LlrpInventorySingulationControl singulation,
            LlrpInventoryStateAwareSingulation stateAware)
        {
            if (singulation.Session > 3)
            {
                return false;
            }

            bool stateMatches = stateAware.Target switch
            {
                LlrpInventorySingulationTarget.StateA => !_sessionStateB[singulation.Session],
                LlrpInventorySingulationTarget.StateB => _sessionStateB[singulation.Session],
                _ => false,
            };
            bool selectedMatches = stateAware.SelectedFlag switch
            {
                LlrpInventorySelectedFlag.Set => _selected,
                LlrpInventorySelectedFlag.Clear => !_selected,
                _ => false,
            };
            return stateMatches && selectedMatches;
        }

        public void ApplyStateAwareFilter(
            LlrpInventoryStateTarget target,
            LlrpInventoryStateAction action,
            bool match)
        {
            switch (action)
            {
                case LlrpInventoryStateAction.AssertStateAOrSelectedAndDeassertStateBOrUnselected:
                    SetTargetToAOrSelected(target, match);
                    break;
                case LlrpInventoryStateAction.AssertStateAOrSelectedAndNoOperation:
                    if (match)
                    {
                        SetTargetToAOrSelected(target, true);
                    }
                    break;
                case LlrpInventoryStateAction.NoOperationAndDeassertStateBOrUnselected:
                    if (!match)
                    {
                        SetTargetToBOrUnselected(target);
                    }
                    break;
                case LlrpInventoryStateAction.NegateStateOrSelectedAndNoOperation:
                    if (match)
                    {
                        NegateTarget(target);
                    }
                    break;
                case LlrpInventoryStateAction.DeassertStateBOrUnselectedAndAssertStateAOrSelected:
                    SetTargetToBOrUnselected(target, match);
                    break;
                case LlrpInventoryStateAction.DeassertStateBOrUnselectedAndNoOperation:
                    if (match)
                    {
                        SetTargetToBOrUnselected(target);
                    }
                    break;
                case LlrpInventoryStateAction.NoOperationAndAssertStateAOrSelected:
                    if (!match)
                    {
                        SetTargetToAOrSelected(target, true);
                    }
                    break;
                case LlrpInventoryStateAction.NoOperationAndNegateStateOrSelected:
                    if (!match)
                    {
                        NegateTarget(target);
                    }
                    break;
            }
        }

        public TagObservation ToObservation() => new()
        {
            ElectronicProductCode = _definition.ElectronicProductCode,
            Tid = _definition.Tid,
            PeakRssi = _definition.PeakRssi,
            AntennaId = _definition.AntennaId,
            ChannelIndex = _definition.ChannelIndex,
            FirstSeenUtc = _firstSeenUtc ?? DateTimeOffset.UtcNow,
            LastSeenUtc = _lastSeenUtc,
            SeenCount = _seenCount == 0 ? 1 : _seenCount,
        };

        public void MarkSeen(DateTimeOffset timestampUtc)
        {
            _firstSeenUtc ??= timestampUtc;
            _lastSeenUtc = timestampUtc;
            _seenCount = _seenCount == uint.MaxValue ? uint.MaxValue : _seenCount + 1;
        }

        public IReadOnlyList<ushort> ReadWords(LlrpTagMemoryBank bank, int pointer, int count)
        {
            ushort[] memory = GetWords(bank);
            if (pointer < 0 || count < 0 || pointer > memory.Length - count || IsReadLocked(bank))
            {
                return [];
            }

            return memory.Skip(pointer).Take(count).ToArray();
        }

        public bool WriteWords(LlrpTagMemoryBank bank, int pointer, IReadOnlyList<ushort> words)
        {
            ArgumentNullException.ThrowIfNull(words);
            if (bank is not (LlrpTagMemoryBank.ElectronicProductCode or LlrpTagMemoryBank.User) ||
                pointer < 0 || IsWriteLocked(bank))
            {
                return false;
            }

            if (bank == LlrpTagMemoryBank.User)
            {
                if (pointer > _userMemory.Length - words.Count)
                {
                    return false;
                }

                for (int index = 0; index < words.Count; index++)
                {
                    _userMemory[pointer + index] = words[index];
                }

                return true;
            }

            return false;
        }

        public bool BlockErase(LlrpTagMemoryBank bank, int pointer, int count)
        {
            if (bank != LlrpTagMemoryBank.User || pointer < 0 || count < 0 ||
                pointer > _userMemory.Length - count || IsWriteLocked(bank))
            {
                return false;
            }

            Array.Clear(_userMemory, pointer, count);
            return true;
        }

        public bool Lock(IReadOnlyList<LlrpTagLockRequest> requests, uint accessPassword)
        {
            if (accessPassword != _definition.AccessPassword)
            {
                return false;
            }

            foreach (LlrpTagLockRequest request in requests)
            {
                _locks[request.MemoryBank] = request.Privilege;
            }

            return requests.Count > 0;
        }

        public bool Kill(uint killPassword)
        {
            if (killPassword != _definition.KillPassword || killPassword == 0)
            {
                return false;
            }

            Killed = true;
            return true;
        }

        public ReadOnlyMemory<byte> GetMemoryBytes(LlrpTagMemoryBank bank) => bank switch
        {
            LlrpTagMemoryBank.Reserved => WordsToBytes(new ushort[8]),
            LlrpTagMemoryBank.ElectronicProductCode =>
                WordsToBytes([0, 0, .. BytesToWords(_definition.ElectronicProductCode.Span)]),
            LlrpTagMemoryBank.Tid => _definition.Tid,
            LlrpTagMemoryBank.User => WordsToBytes(_userMemory),
            _ => ReadOnlyMemory<byte>.Empty,
        };

        private ushort[] GetWords(LlrpTagMemoryBank bank) => bank switch
        {
            LlrpTagMemoryBank.Reserved => new ushort[8],
            LlrpTagMemoryBank.ElectronicProductCode => [0, 0, .. BytesToWords(_definition.ElectronicProductCode.Span)],
            LlrpTagMemoryBank.Tid => BytesToWords(_definition.Tid.Span),
            LlrpTagMemoryBank.User => _userMemory,
            _ => [],
        };

        private bool IsReadLocked(LlrpTagMemoryBank bank) =>
            _locks.TryGetValue(bank, out LlrpTagLockPrivilege privilege) && privilege == LlrpTagLockPrivilege.PermaLock;

        private bool IsWriteLocked(LlrpTagMemoryBank bank) =>
            _locks.TryGetValue(bank, out LlrpTagLockPrivilege privilege) && privilege is LlrpTagLockPrivilege.PermaLock or LlrpTagLockPrivilege.Unlock;

        private void SetTargetToAOrSelected(LlrpInventoryStateTarget target, bool match)
        {
            if (match)
            {
                SetTarget(target, target == LlrpInventoryStateTarget.SelectedFlag);
            }
            else
            {
                SetTargetToBOrUnselected(target);
            }
        }

        private void SetTargetToBOrUnselected(LlrpInventoryStateTarget target)
        {
            SetTarget(target, target != LlrpInventoryStateTarget.SelectedFlag);
        }

        private void SetTargetToBOrUnselected(LlrpInventoryStateTarget target, bool match)
        {
            if (match)
            {
                SetTargetToBOrUnselected(target);
            }
            else
            {
                SetTargetToAOrSelected(target, true);
            }
        }

        private void SetTarget(LlrpInventoryStateTarget target, bool value)
        {
            if (target == LlrpInventoryStateTarget.SelectedFlag)
            {
                _selected = value;
                return;
            }

            _sessionStateB[(int)target - (int)LlrpInventoryStateTarget.Session0] = value;
        }

        private void NegateTarget(LlrpInventoryStateTarget target) => SetTarget(target, !GetTarget(target));

        private bool GetTarget(LlrpInventoryStateTarget target) => target == LlrpInventoryStateTarget.SelectedFlag
            ? _selected
            : _sessionStateB[(int)target - (int)LlrpInventoryStateTarget.Session0];

        private static ushort[] BytesToWords(ReadOnlySpan<byte> bytes)
        {
            var words = new ushort[bytes.Length / 2];
            for (int index = 0; index < words.Length; index++)
            {
                words[index] = (ushort)((bytes[index * 2] << 8) | bytes[index * 2 + 1]);
            }

            return words;
        }

        private static byte[] WordsToBytes(ReadOnlySpan<ushort> words)
        {
            var bytes = new byte[words.Length * 2];
            for (int index = 0; index < words.Length; index++)
            {
                bytes[index * 2] = (byte)(words[index] >> 8);
                bytes[index * 2 + 1] = (byte)words[index];
            }

            return bytes;
        }
    }
}
