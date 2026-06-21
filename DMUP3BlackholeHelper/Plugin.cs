using DMUP3BlackholeHelper.Windows;
using Dalamud.Game.DutyState;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace DMUP3BlackholeHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string ConfigCommandName = "/dmup3";
    private const string ShortHelperCommandName = "/dmup3h";
    private const string HelperCommandName = "/dmup3helper";
    private const uint DmuTerritoryId = 1363;
    private const float BlackHoleResolutionWindowSeconds = 4.0f;
    private const float DeathCauseFreshnessSeconds = 15.0f;
    private const double StatusPulseDebounceSeconds = 2.0;
    private const ushort ChatGreenColorKey = 45;
    private const int NoSoundEffectId = 0;
    private const int QueuedChatDelayMs = 750;
    private static readonly IReadOnlySet<uint> BlackHoleResolutionActionIds = new HashSet<uint>
    {
        47868, // Nothingness
        48333, // Black Spark
    };

    public static readonly IReadOnlyList<ChatChannelOption> ChatChannelOptions =
    [
        new(AssignmentChatChannel.Say, "Say", "/s"),
        new(AssignmentChatChannel.Party, "Party", "/p"),
        new(AssignmentChatChannel.Alliance, "Alliance", "/alliance"),
        new(AssignmentChatChannel.FreeCompany, "Free Company", "/fc"),
        new(AssignmentChatChannel.CrossWorldLinkshell1, "Cross-world Linkshell 1", "/cwl1"),
        new(AssignmentChatChannel.CrossWorldLinkshell2, "Cross-world Linkshell 2", "/cwl2"),
        new(AssignmentChatChannel.CrossWorldLinkshell3, "Cross-world Linkshell 3", "/cwl3"),
        new(AssignmentChatChannel.CrossWorldLinkshell4, "Cross-world Linkshell 4", "/cwl4"),
        new(AssignmentChatChannel.CrossWorldLinkshell5, "Cross-world Linkshell 5", "/cwl5"),
        new(AssignmentChatChannel.CrossWorldLinkshell6, "Cross-world Linkshell 6", "/cwl6"),
        new(AssignmentChatChannel.CrossWorldLinkshell7, "Cross-world Linkshell 7", "/cwl7"),
        new(AssignmentChatChannel.CrossWorldLinkshell8, "Cross-world Linkshell 8", "/cwl8"),
    ];

    public static readonly IReadOnlyList<SoundEffectOption> SoundEffectOptions =
        new[] { new SoundEffectOption(NoSoundEffectId, "None") }
            .Concat(Enumerable.Range(1, 16).Select(id => new SoundEffectOption(id, $"<se.{id}>")))
            .ToList();

    internal static readonly IReadOnlyDictionary<uint, WatchedStatus> WatchedStatuses =
        new Dictionary<uint, WatchedStatus>
        {
            [3004] = new(3004, "First in Line", StatusKind.LineOrder, 10),
            [3005] = new(3005, "Second in Line", StatusKind.LineOrder, 20),
            [3006] = new(3006, "Third in Line", StatusKind.LineOrder, 30),
            [1604] = new(1604, "Accretion", StatusKind.Accretion, 40),
            [1605] = new(1605, "Primordial Crust", StatusKind.PrimordialCrust, 45),
            [5452] = new(5452, "Black Hole Active", StatusKind.BlackHole, 46),
            [5453] = new(5453, "Black Hole Complete", StatusKind.BlackHole, 47),
            [5454] = new(5454, "Black Hole Marker", StatusKind.BlackHole, 48),
            [1053] = new(1053, "Earth Resistance Down II", StatusKind.EarthResistance, 50),
            [2097] = new(2097, "Earth Resistance Down II", StatusKind.EarthResistance, 50),
            [3372] = new(3372, "Earth Resistance Down II", StatusKind.EarthResistance, 50),
        };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("DMUP3BlackholeHelper");
    private readonly ConfigWindow configWindow;
    private readonly HelperWindow helperWindow;
    private readonly List<PartyStatusEntry> currentEntries = [];
    private readonly List<PartyMemberSnapshot> currentMembers = [];
    private readonly List<LocalPlayerBlackHoleAssignment> currentAssignments = [];
    private readonly List<BlackHoleResolutionRecord> currentBlackHoleResolutions = [];
    private readonly List<PartyDeathRecord> currentPartyDeaths = [];
    private readonly List<BlackHolePullSnapshot> pullSnapshots = [];
    private readonly HashSet<string> accretionHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PartyStatusEntry> lineHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PartyDeathRecord> lastIncomingActionByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PartyStatusEntry> debugKnownEntries = new();
    private readonly Dictionary<uint, uint> statusIconCache = new();
    private readonly Dictionary<uint, string> actionNameCache = new();
    private readonly HashSet<string> capturedBlackHoleResolutionKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> deadMemberKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> observedBlackHoleWaveKeys = [];
    private readonly HashSet<string> activeBlackHoleMarkerKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeEarthResistanceKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> postedNowInstructionCalloutWaveKeys = [];
    private readonly Queue<QueuedChatMessage> queuedChatMessages = [];
    private Hook<ActionEffectHandler.Delegates.Receive>? actionEffectHook;
    private DateTime? blackHoleStartedAtUtc;
    private DateTime? lastBlackHoleSeenAtUtc;
    private DateTime lastBlackHoleMarkerPulseAtUtc = DateTime.MinValue;
    private DateTime lastEarthResistancePulseAtUtc = DateTime.MinValue;
    private int blackHoleMarkerPulseCount;
    private int earthPulseCount;
    private float lastKnownBlackHoleElapsedSeconds;
    private DateTime nextQueuedChatMessageAtUtc = DateTime.MinValue;
    private bool debugRecognizedTerritory;

    public Configuration Configuration { get; }

    public IReadOnlyList<PartyStatusEntry> CurrentEntries => currentEntries;

    public IReadOnlyList<LocalPlayerBlackHoleAssignment> CurrentAssignments => currentAssignments;

    public IReadOnlyList<BlackHoleResolutionRecord> CurrentBlackHoleResolutions => currentBlackHoleResolutions;

    public IReadOnlyList<PartyDeathRecord> CurrentPartyDeaths => currentPartyDeaths;

    public LocalPlayerBlackHoleAssignment? LocalAssignment { get; private set; }

    public IReadOnlyList<BlackHolePullSnapshot> PullSnapshots => pullSnapshots;

    public BlackHolePullSnapshot? LastPullSnapshot => pullSnapshots.LastOrDefault();

    public bool IsInDmu { get; private set; }

    public BlackHoleMechanicState MechanicState { get; private set; } = BlackHoleMechanicState.Inactive;

    public float CurrentPullElapsedSeconds => MechanicState.IsActive
        ? MechanicState.ElapsedSeconds
        : lastKnownBlackHoleElapsedSeconds;

    public bool HasHadAccretion(string memberKey)
    {
        return accretionHistory.Contains(memberKey);
    }

    public uint GetStatusIconId(uint statusId)
    {
        if (statusIconCache.TryGetValue(statusId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            iconId = status?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status icon for {StatusId}.", statusId);
        }

        if (iconId != 0)
        {
            statusIconCache[statusId] = iconId;
        }

        return iconId;
    }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        configWindow = new ConfigWindow(this);
        helperWindow = new HelperWindow(this)
        {
            IsOpen = Configuration.ShowHelper,
        };

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(helperWindow);

        CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the DMU P3 Blackhole Helper settings window.",
        });
        CommandManager.AddHandler(ShortHelperCommandName, new CommandInfo(OnHelperCommand)
        {
            HelpMessage = "Open the DMU P3 Blackhole Helper window.",
        });
        CommandManager.AddHandler(HelperCommandName, new CommandInfo(OnHelperCommand)
        {
            HelpMessage = "Open the DMU P3 Blackhole Helper window.",
        });
        unsafe
        {
            actionEffectHook = GameInteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                ActionEffectHandler.MemberFunctionPointers.Receive,
                OnReceiveActionEffect);
            actionEffectHook.Enable();
        }

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleHelperUi;
        DutyState.DutyStarted += OnDutyReset;
        DutyState.DutyWiped += OnDutyReset;
        DutyState.DutyRecommenced += OnDutyReset;
    }

    public void Dispose()
    {
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyReset;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleHelperUi;
        CommandManager.RemoveHandler(HelperCommandName);
        CommandManager.RemoveHandler(ShortHelperCommandName);
        CommandManager.RemoveHandler(ConfigCommandName);
        actionEffectHook?.Dispose();

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        helperWindow.Dispose();
    }

    public void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    public void ToggleHelperUi()
    {
        helperWindow.Toggle();
        Configuration.ShowHelper = helperWindow.IsOpen;
        SaveConfiguration();
    }

    public void OpenHelperUi()
    {
        Configuration.ShowHelper = true;
        helperWindow.IsOpen = true;
        SaveConfiguration();
    }

    public void SetShowHelper(bool enabled)
    {
        Configuration.ShowHelper = enabled;
        helperWindow.IsOpen = enabled;
        SaveConfiguration();
    }

    public void SetDebugChat(bool enabled)
    {
        Configuration.DebugChat = enabled;
        if (enabled)
        {
            debugRecognizedTerritory = false;
            debugKnownEntries.Clear();
        }

        SaveConfiguration();
    }

    public void SetPostInstructionsToChat(bool enabled)
    {
        Configuration.PostInstructionsToChat = enabled;
        ResetInstructionChatCallouts();
        SaveConfiguration();
    }

    public void SetNowSoundEffectId(int soundEffectId)
    {
        Configuration.NowSoundEffectId = ClampSoundEffectId(soundEffectId);
        SaveConfiguration();
    }

    public void TestNowSoundEffect()
    {
        PrintSoundEffectEcho(Configuration.NowSoundEffectId);
    }

    public void SetAssignmentChatChannel(AssignmentChatChannel channel)
    {
        Configuration.AssignmentChatChannel = GetChatChannelOption(channel).Channel;
        SaveConfiguration();
    }

    public static string GetChatChannelLabel(AssignmentChatChannel channel)
    {
        return GetChatChannelOption(channel).Label;
    }

    public static AssignmentChatChannel GetEffectiveChatChannel(AssignmentChatChannel channel)
    {
        return GetChatChannelOption(channel).Channel;
    }

    public void PrintAssignmentToChat(LocalPlayerBlackHoleAssignment assignment, string pullLabel)
    {
        QueueChat(
            Configuration.AssignmentChatChannel,
            $"[P3 Helper] {pullLabel} assignment: {assignment.MemberName} - {assignment.RoleName}");
    }

    public void SetPreviewWhenInactive(bool enabled)
    {
        Configuration.ShowPreviewWhenInactive = enabled;
        SaveConfiguration();
    }

    public void SetHelperFontScale(float scale)
    {
        Configuration.HelperFontScale = Math.Clamp(scale, 0.75f, 2.0f);
        SaveConfiguration();
    }

    public void SetHelperIconScale(float scale)
    {
        Configuration.HelperIconScale = Math.Clamp(scale, 0.75f, 3.0f);
        SaveConfiguration();
    }

    public void SetHelperBackgroundOpacity(float opacity)
    {
        Configuration.HelperBackgroundOpacity = Math.Clamp(opacity, 0.15f, 1.0f);
        SaveConfiguration();
    }

    public void SaveConfiguration()
    {
        Configuration.Save();
    }

    private void OnConfigCommand(string command, string args)
    {
        ToggleConfigUi();
    }

    private void OnHelperCommand(string command, string args)
    {
        ToggleHelperUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        FlushQueuedChatMessages(DateTime.UtcNow);
        RefreshStatusSnapshot();
    }

    private void RefreshStatusSnapshot()
    {
        IsInDmu = ClientState.TerritoryType == DmuTerritoryId;
        if (!IsInDmu)
        {
            CaptureCurrentPullSnapshot("Left DMU");
            currentEntries.Clear();
            currentMembers.Clear();
            currentAssignments.Clear();
            currentBlackHoleResolutions.Clear();
            currentPartyDeaths.Clear();
            capturedBlackHoleResolutionKeys.Clear();
            lastIncomingActionByMember.Clear();
            deadMemberKeys.Clear();
            LocalAssignment = null;
            accretionHistory.Clear();
            lineHistory.Clear();
            ResetBlackHoleState(resetInstructionCallouts: true);
            UpdateDebugState(inDmu: false, []);
            return;
        }

        var nextEntries = new List<PartyStatusEntry>();
        var nextMembers = new List<PartyMemberSnapshot>();
        var nextDeathStates = new List<(string MemberKey, string MemberName, int PartyIndex, bool IsDead)>();
        var partyIndex = 0;
        foreach (var member in PartyList)
        {
            var memberName = member.Name.TextValue;
            var memberKey = member.ContentId != 0
                ? member.ContentId.ToString("X16")
                : $"{memberName}:{partyIndex}";
            var isDps = IsDpsJob(member.ClassJob.RowId);
            nextMembers.Add(new PartyMemberSnapshot(
                memberKey,
                memberName,
                partyIndex,
                isDps,
                member.ContentId,
                member.EntityId));
            var isDead = member.GameObject?.IsDead == true ||
                (member.MaxHP > 0 && member.CurrentHP == 0);
            nextDeathStates.Add((memberKey, memberName, partyIndex, isDead));

            foreach (var status in member.Statuses)
            {
                if (!WatchedStatuses.TryGetValue(status.StatusId, out var watchedStatus))
                {
                    continue;
                }

                nextEntries.Add(new PartyStatusEntry(
                    $"{memberKey}:{status.StatusId}",
                    memberKey,
                    memberName,
                    partyIndex,
                    isDps,
                    status.StatusId,
                    watchedStatus.Name,
                    watchedStatus.Kind,
                    status.RemainingTime,
                    watchedStatus.SortOrder));
            }

            partyIndex++;
        }

        foreach (var entry in nextEntries.Where(entry => entry.Kind == StatusKind.Accretion))
        {
            accretionHistory.Add(entry.MemberKey);
        }

        foreach (var entry in nextEntries.Where(entry => entry.Kind == StatusKind.LineOrder))
        {
            lineHistory[entry.MemberKey] = entry;
        }

        currentMembers.Clear();
        currentMembers.AddRange(nextMembers.OrderBy(member => member.PartyIndex));

        currentEntries.Clear();
        currentEntries.AddRange(nextEntries
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.PartyIndex)
            .ThenBy(entry => entry.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.StatusId));

        currentAssignments.Clear();
        currentAssignments.AddRange(currentMembers.Select(member => BuildAssignmentForMember(member, currentEntries, lineHistory, accretionHistory)));

        LocalAssignment = BuildLocalAssignment();
        UpdateBlackHoleState(currentEntries);
        UpdatePartyDeathTimeline(nextDeathStates);
        UpdateInstructionChatCallout();
        UpdateDebugState(inDmu: true, currentEntries);
    }

    private void UpdateBlackHoleState(IReadOnlyList<PartyStatusEntry> entries)
    {
        var now = DateTime.UtcNow;
        var hasBlackHoleMarker = entries.Any(entry => entry.StatusId == 5454);
        var hasStartMarker = entries.Any(entry => entry.Kind == StatusKind.LineOrder || entry.StatusId == 5454);
        var hasMechanicMarker = entries.Any(entry =>
            entry.Kind is StatusKind.LineOrder or StatusKind.Accretion or StatusKind.EarthResistance or StatusKind.BlackHole);

        if (hasStartMarker && blackHoleStartedAtUtc is null)
        {
            blackHoleStartedAtUtc = now;
            lastBlackHoleSeenAtUtc = now;
            blackHoleMarkerPulseCount = 0;
            earthPulseCount = 0;
            observedBlackHoleWaveKeys.Clear();
            activeBlackHoleMarkerKeys.Clear();
            activeEarthResistanceKeys.Clear();
            lastBlackHoleMarkerPulseAtUtc = DateTime.MinValue;
            lastEarthResistancePulseAtUtc = DateTime.MinValue;
            lastKnownBlackHoleElapsedSeconds = 0.0f;
        }

        if (hasMechanicMarker)
        {
            lastBlackHoleSeenAtUtc = now;
        }

        var currentBlackHoleMarkerKeys = entries
            .Where(entry => entry.StatusId == 5454)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var hasNewBlackHoleMarker = currentBlackHoleMarkerKeys
            .Any(key => !activeBlackHoleMarkerKeys.Contains(key));
        activeBlackHoleMarkerKeys.Clear();
        activeBlackHoleMarkerKeys.UnionWith(currentBlackHoleMarkerKeys);

        if (blackHoleStartedAtUtc is not null &&
            hasNewBlackHoleMarker &&
            IsDebouncedPulse(now, lastBlackHoleMarkerPulseAtUtc))
        {
            lastBlackHoleMarkerPulseAtUtc = now;
            blackHoleMarkerPulseCount = Math.Max(blackHoleMarkerPulseCount + 1, earthPulseCount + 1);
            if (BlackHoleTimeline.GetWaveByPulseCount(blackHoleMarkerPulseCount) is { } markerWave)
            {
                observedBlackHoleWaveKeys.Add(markerWave.Key);
                CalibrateBlackHoleClock(now, markerWave.StartsAtSeconds);
            }
        }

        var currentEarthResistanceKeys = entries
            .Where(entry => entry.Kind == StatusKind.EarthResistance)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var hasNewEarthResistance = currentEarthResistanceKeys
            .Any(key => !activeEarthResistanceKeys.Contains(key));
        activeEarthResistanceKeys.Clear();
        activeEarthResistanceKeys.UnionWith(currentEarthResistanceKeys);

        if (blackHoleStartedAtUtc is not null &&
            hasNewEarthResistance &&
            IsDebouncedPulse(now, lastEarthResistancePulseAtUtc))
        {
            lastEarthResistancePulseAtUtc = now;
            earthPulseCount++;
            if (BlackHoleTimeline.GetWaveByPulseCount(earthPulseCount) is { } resolvedWave)
            {
                CalibrateBlackHoleClock(now, resolvedWave.MarkerAtSeconds);
            }
        }

        if (blackHoleStartedAtUtc is null)
        {
            MechanicState = BlackHoleMechanicState.Inactive;
            return;
        }

        var elapsedSeconds = (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds;
        lastKnownBlackHoleElapsedSeconds = elapsedSeconds;
        if (!hasMechanicMarker &&
            lastBlackHoleSeenAtUtc is not null &&
            (now - lastBlackHoleSeenAtUtc.Value).TotalSeconds > 2.0)
        {
            ResetBlackHoleState();
            return;
        }

        var timelineCurrentWave = BlackHoleTimeline.GetCurrentWave(elapsedSeconds);
        var observedCurrentWave = GetObservedCurrentWave(timelineCurrentWave, elapsedSeconds);
        var currentWave = observedCurrentWave ??
            (ShouldExposeCurrentWave(timelineCurrentWave, elapsedSeconds) ? timelineCurrentWave : null);
        var nextWave = GetObservedNextWave(currentWave) ??
            (currentWave is null && timelineCurrentWave is not null
                ? timelineCurrentWave
                : BlackHoleTimeline.GetNextWave(elapsedSeconds));
        var lastResolvedWave = BlackHoleTimeline.GetWaveByPulseCount(earthPulseCount);
        MechanicState = new BlackHoleMechanicState(
            IsActive: true,
            ElapsedSeconds: elapsedSeconds,
            CurrentWave: currentWave,
            NextWave: nextWave,
            LastResolvedWave: lastResolvedWave,
            EarthPulseCount: earthPulseCount);
    }

    private void ResetBlackHoleState(bool resetInstructionCallouts = false)
    {
        blackHoleStartedAtUtc = null;
        lastBlackHoleSeenAtUtc = null;
        blackHoleMarkerPulseCount = 0;
        earthPulseCount = 0;
        observedBlackHoleWaveKeys.Clear();
        activeBlackHoleMarkerKeys.Clear();
        activeEarthResistanceKeys.Clear();
        lastBlackHoleMarkerPulseAtUtc = DateTime.MinValue;
        lastEarthResistancePulseAtUtc = DateTime.MinValue;
        if (resetInstructionCallouts)
        {
            ResetInstructionChatCallouts();
        }

        MechanicState = BlackHoleMechanicState.Inactive;
    }

    private static bool IsDebouncedPulse(DateTime now, DateTime lastPulseAtUtc)
    {
        return lastPulseAtUtc == DateTime.MinValue ||
            (now - lastPulseAtUtc).TotalSeconds >= StatusPulseDebounceSeconds;
    }

    private void CalibrateBlackHoleClock(DateTime now, float anchorElapsedSeconds)
    {
        if (blackHoleStartedAtUtc is null)
        {
            return;
        }

        blackHoleStartedAtUtc = now - TimeSpan.FromSeconds(anchorElapsedSeconds);
        lastKnownBlackHoleElapsedSeconds = anchorElapsedSeconds;
    }

    private bool ShouldExposeCurrentWave(BlackHoleWave? wave, float elapsedSeconds)
    {
        if (wave is null)
        {
            return false;
        }

        if (observedBlackHoleWaveKeys.Contains(wave.Key))
        {
            return true;
        }

        return elapsedSeconds >= wave.StartsAtSeconds;
    }

    private BlackHoleWave? GetObservedCurrentWave(BlackHoleWave? timelineCurrentWave, float elapsedSeconds)
    {
        if (blackHoleMarkerPulseCount > earthPulseCount &&
            BlackHoleTimeline.GetWaveByPulseCount(blackHoleMarkerPulseCount) is { } markerWave &&
            elapsedSeconds < markerWave.EndsAtSeconds)
        {
            return markerWave;
        }

        if (timelineCurrentWave is not null && observedBlackHoleWaveKeys.Contains(timelineCurrentWave.Key))
        {
            return timelineCurrentWave;
        }

        return null;
    }

    private BlackHoleWave? GetObservedNextWave(BlackHoleWave? currentWave)
    {
        if (currentWave is not null)
        {
            return BlackHoleTimeline.GetWaveByPulseCount(GetWavePulseNumber(currentWave) + 1);
        }

        var nextPulseCount = Math.Max(blackHoleMarkerPulseCount, earthPulseCount) + 1;
        return BlackHoleTimeline.GetWaveByPulseCount(nextPulseCount);
    }

    private static int GetWavePulseNumber(BlackHoleWave wave)
    {
        var index = BlackHoleTimeline.Waves
            .Select((timelineWave, timelineIndex) => new { timelineWave, timelineIndex })
            .FirstOrDefault(entry => entry.timelineWave.Key == wave.Key)
            ?.timelineIndex;
        return index is null ? 0 : index.Value + 1;
    }

    private void UpdatePartyDeathTimeline(IReadOnlyList<(string MemberKey, string MemberName, int PartyIndex, bool IsDead)> deathStates)
    {
        var currentMemberKeys = deathStates
            .Select(state => state.MemberKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var staleKey in deadMemberKeys.Where(key => !currentMemberKeys.Contains(key)).ToList())
        {
            deadMemberKeys.Remove(staleKey);
        }

        var shouldRecordDeaths = blackHoleStartedAtUtc is not null ||
            currentBlackHoleResolutions.Count > 0 ||
            lineHistory.Count > 0 ||
            accretionHistory.Count > 0;

        foreach (var state in deathStates.OrderBy(state => state.PartyIndex))
        {
            if (!state.IsDead)
            {
                deadMemberKeys.Remove(state.MemberKey);
                continue;
            }

            if (!deadMemberKeys.Add(state.MemberKey) || !shouldRecordDeaths)
            {
                continue;
            }

            currentPartyDeaths.Add(CreatePartyDeathRecord(state.MemberKey, state.MemberName, state.PartyIndex));
        }
    }

    private PartyDeathRecord CreatePartyDeathRecord(string memberKey, string memberName, int partyIndex)
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = blackHoleStartedAtUtc is not null
            ? (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds
            : lastKnownBlackHoleElapsedSeconds;
        var wave = BlackHoleTimeline.GetCurrentWave(elapsedSeconds) ??
            BlackHoleTimeline.GetNearestWaveByMarkerTime(elapsedSeconds, 8.0f);

        if (!lastIncomingActionByMember.TryGetValue(memberKey, out var cause) ||
            (now - cause.SeenAtUtc).TotalSeconds > DeathCauseFreshnessSeconds)
        {
            return new PartyDeathRecord(
                now,
                elapsedSeconds,
                memberKey,
                memberName,
                partyIndex,
                0,
                "Unknown",
                string.Empty,
                wave);
        }

        return new PartyDeathRecord(
            now,
            elapsedSeconds,
            memberKey,
            memberName,
            partyIndex,
            cause.ActionId,
            cause.ActionName,
            cause.GlobalSequence,
            cause.Wave ?? wave);
    }

    private unsafe void OnReceiveActionEffect(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            CapturePartyTargetActions(casterEntityId, header, targetEntityIds);
            CaptureBlackHoleResolution(casterEntityId, header, targetEntityIds);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not process action effect for Black Hole resolution.");
        }

        actionEffectHook?.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private unsafe void CapturePartyTargetActions(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        GameObjectId* targetEntityIds)
    {
        if (!IsInDmu ||
            header is null ||
            targetEntityIds is null ||
            header->NumTargets == 0 ||
            !BlackHoleResolutionActionIds.Contains(header->ActionId) ||
            IsPartyEntity(casterEntityId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var elapsedSeconds = blackHoleStartedAtUtc is not null
            ? (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds
            : lastKnownBlackHoleElapsedSeconds;
        var wave = BlackHoleTimeline.GetCurrentWave(elapsedSeconds) ??
            BlackHoleTimeline.GetNearestWaveByMarkerTime(elapsedSeconds, 8.0f);

        for (var i = 0; i < header->NumTargets; i++)
        {
            var member = FindPartyMemberByTargetId(targetEntityIds[i]);
            if (member is null)
            {
                continue;
            }

            lastIncomingActionByMember[member.MemberKey] = new PartyDeathRecord(
                now,
                elapsedSeconds,
                member.MemberKey,
                member.MemberName,
                member.PartyIndex,
                header->ActionId,
                GetActionName(header->ActionId),
                $"{header->GlobalSequence}",
                wave);
        }
    }

    private unsafe void CaptureBlackHoleResolution(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        GameObjectId* targetEntityIds)
    {
        if (!IsInDmu ||
            !MechanicState.IsActive ||
            header is null ||
            targetEntityIds is null ||
            header->NumTargets == 0 ||
            IsPartyEntity(casterEntityId))
        {
            return;
        }

        var wave = BlackHoleTimeline.GetNearestWaveByMarkerTime(
            MechanicState.ElapsedSeconds,
            BlackHoleResolutionWindowSeconds);
        if (wave is null)
        {
            return;
        }

        var expectedAssignments = GetExpectedAssignmentsForWave(wave);
        if (expectedAssignments.Count == 0)
        {
            return;
        }

        var hitAssignments = new List<LocalPlayerBlackHoleAssignment>();
        for (var i = 0; i < header->NumTargets; i++)
        {
            var member = FindPartyMemberByTargetId(targetEntityIds[i]);
            if (member is null)
            {
                continue;
            }

            var assignment = currentAssignments.FirstOrDefault(currentAssignment => currentAssignment.MemberKey == member.MemberKey);
            if (assignment is not null && hitAssignments.All(hit => hit.MemberKey != assignment.MemberKey))
            {
                hitAssignments.Add(assignment);
            }
        }

        if (hitAssignments.Count == 0)
        {
            return;
        }

        observedBlackHoleWaveKeys.Add(wave.Key);
        CalibrateBlackHoleClock(DateTime.UtcNow, wave.MarkerAtSeconds);

        var resolutionKey = BuildResolutionKey(wave, header, hitAssignments);
        if (!capturedBlackHoleResolutionKeys.Add(resolutionKey))
        {
            return;
        }

        var expectedKeys = expectedAssignments.Select(assignment => assignment.MemberKey).ToHashSet(StringComparer.Ordinal);
        var hits = hitAssignments
            .Select((assignment, hitOrder) => new BlackHoleResolutionHit(
                assignment.MemberKey,
                assignment.MemberName,
                assignment.PartyIndex,
                assignment.RoleName,
                hitOrder,
                expectedKeys.Contains(assignment.MemberKey)))
            .OrderBy(hit => hit.PartyIndex)
            .ToList();

        currentBlackHoleResolutions.Add(new BlackHoleResolutionRecord(
            DateTime.UtcNow,
            wave,
            header->ActionId,
            $"{header->GlobalSequence}",
            GetActionName(header->ActionId),
            hits));

        if (Configuration.DebugChat)
        {
            var hitNames = string.Join(", ", hits.Select(hit => hit.MemberName));
            PrintDebug($"Captured {GetActionName(header->ActionId)} ({header->ActionId}) for {wave.Label}: {hitNames}.");
        }
    }

    private IReadOnlyList<LocalPlayerBlackHoleAssignment> GetExpectedAssignmentsForWave(BlackHoleWave wave)
    {
        var waveInstructions = BlackHoleStrategy.Instructions
            .Where(instruction => instruction.IsForWave(wave))
            .ToList();
        if (waveInstructions.Count == 0)
        {
            return [];
        }

        return currentAssignments
            .Where(assignment => waveInstructions.Any(instruction => instruction.Role.Matches(assignment)))
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();
    }

    private PartyMemberSnapshot? FindPartyMemberByTargetId(GameObjectId targetId)
    {
        return currentMembers.FirstOrDefault(member => TargetMatchesMember(targetId, member));
    }

    private static bool TargetMatchesMember(GameObjectId targetId, PartyMemberSnapshot member)
    {
        if (targetId.ObjectId != 0 && member.EntityId == targetId.ObjectId)
        {
            return true;
        }

        if (targetId.Id <= uint.MaxValue)
        {
            var shortId = (uint)targetId.Id;
            return shortId != 0 && member.EntityId == shortId;
        }

        return false;
    }

    private bool IsPartyEntity(uint entityId)
    {
        return entityId != 0 && currentMembers.Any(member => member.EntityId == entityId);
    }

    private string GetActionName(uint actionId)
    {
        if (actionNameCache.TryGetValue(actionId, out var cachedName))
        {
            return cachedName;
        }

        var actionName = $"Action {actionId}";
        try
        {
            var action = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(actionId);
            var sheetName = action?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                actionName = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load action name for {ActionId}.", actionId);
        }

        actionNameCache[actionId] = actionName;
        return actionName;
    }

    private static unsafe string BuildResolutionKey(
        BlackHoleWave wave,
        ActionEffectHandler.Header* header,
        IReadOnlyList<LocalPlayerBlackHoleAssignment> hitAssignments)
    {
        var hitKey = string.Join(
            ",",
            hitAssignments
                .Select(assignment => assignment.MemberKey)
                .Order(StringComparer.Ordinal));
        return $"{wave.Key}:{header->ActionId}:{header->GlobalSequence}:{hitKey}";
    }

    private void UpdateInstructionChatCallout()
    {
        if (!Configuration.PostInstructionsToChat)
        {
            return;
        }

        var assignment = LocalAssignment;
        if (assignment is null || !assignment.HasLine)
        {
            return;
        }

        if (GetNowCalloutWave(assignment) is { } currentWave)
        {
            TryPostInstructionChatCallout("NOW", currentWave, assignment);
        }
    }

    private BlackHoleWave? GetNowCalloutWave(LocalPlayerBlackHoleAssignment assignment)
    {
        if (!MechanicState.IsActive || blackHoleMarkerPulseCount <= 0)
        {
            return null;
        }

        var observedWave = BlackHoleTimeline.GetWaveByPulseCount(blackHoleMarkerPulseCount);
        if (observedWave is null ||
            !observedBlackHoleWaveKeys.Contains(observedWave.Key) ||
            postedNowInstructionCalloutWaveKeys.Contains(observedWave.Key))
        {
            return null;
        }

        return GetInstructionsForWave(assignment, observedWave).Count > 0
            ? observedWave
            : null;
    }

    private void TryPostInstructionChatCallout(
        string header,
        BlackHoleWave wave,
        LocalPlayerBlackHoleAssignment assignment)
    {
        var instructions = GetInstructionsForWave(assignment, wave);
        if (instructions.Count == 0)
        {
            return;
        }

        postedNowInstructionCalloutWaveKeys.Add(wave.Key);

        var lines = BuildInstructionCalloutLines(header, wave, instructions);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == 0)
            {
                PrintEcho(CreateGreenText(lines[i]));
                PrintSoundEffectEcho(Configuration.NowSoundEffectId);
                continue;
            }

            PrintEcho(lines[i]);
        }
    }

    private static IReadOnlyList<BlackHoleInstruction> GetInstructionsForWave(
        LocalPlayerBlackHoleAssignment assignment,
        BlackHoleWave wave)
    {
        return BlackHoleStrategy.GetInstructionsFor(assignment)
            .Where(instruction => instruction.IsForWave(wave))
            .OrderBy(instruction => instruction.Tether)
            .ToList();
    }

    private static IReadOnlyList<string> BuildInstructionCalloutLines(
        string header,
        BlackHoleWave wave,
        IReadOnlyList<BlackHoleInstruction> instructions)
    {
        var lines = new List<string>
        {
            $"[P3 Helper] {header}: {wave.Label}",
        };

        lines.AddRange(instructions.Select(instruction => $"Tether {instruction.Tether}: {instruction.Action}"));
        return lines;
    }

    private void ResetInstructionChatCallouts()
    {
        postedNowInstructionCalloutWaveKeys.Clear();
    }

    private static int ClampSoundEffectId(int soundEffectId)
    {
        return Math.Clamp(soundEffectId, SoundEffectOptions[0].Id, SoundEffectOptions[^1].Id);
    }

    private static unsafe void PrintSoundEffectEcho(int soundEffectId)
    {
        var id = ClampSoundEffectId(soundEffectId);
        if (id == NoSoundEffectId)
        {
            return;
        }

        var uiModule = UIModule.Instance();
        var shellModule = RaptureShellModule.Instance();
        if (uiModule == null || shellModule == null)
        {
            Log.Debug("Could not send sound effect echo because the UI shell is unavailable.");
            return;
        }

        using var command = new Utf8String($"/echo <se.{id}>");
        shellModule->ExecuteCommandInner(&command, uiModule);
    }

    private static void PrintEcho(string message)
    {
        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = message,
        });
    }

    private static void PrintEcho(SeString message)
    {
        ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = message,
        });
    }

    private void QueueChat(AssignmentChatChannel channel, string message)
    {
        queuedChatMessages.Enqueue(new QueuedChatMessage(GetChatChannelOption(channel).Channel, SanitizeChatText(message)));
    }

    private void FlushQueuedChatMessages(DateTime now)
    {
        if (queuedChatMessages.Count == 0 || nextQueuedChatMessageAtUtc > now)
        {
            return;
        }

        var nextMessage = queuedChatMessages.Dequeue();
        SendChat(nextMessage.Channel, nextMessage.Message);
        nextQueuedChatMessageAtUtc = now.AddMilliseconds(QueuedChatDelayMs);
    }

    private static unsafe void SendChat(AssignmentChatChannel channel, string message)
    {
        try
        {
            var uiModule = UIModule.Instance();
            var shellModule = RaptureShellModule.Instance();
            if (uiModule == null || shellModule == null)
            {
                Log.Warning("Could not send DMU P3 assignment chat message because the UI shell is unavailable.");
                ChatGui.Print("[P3 Helper] Could not send chat message.");
                return;
            }

            using var command = new Utf8String($"{GetChatChannelOption(channel).Command} {SanitizeChatText(message)}");
            shellModule->ExecuteCommandInner(&command, uiModule);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not send DMU P3 assignment chat message.");
            ChatGui.Print("[P3 Helper] Could not send chat message.");
        }
    }

    private static ChatChannelOption GetChatChannelOption(AssignmentChatChannel channel)
    {
        return ChatChannelOptions.FirstOrDefault(option => option.Channel == channel) ??
            ChatChannelOptions.First(option => option.Channel == AssignmentChatChannel.Party);
    }

    private static string SanitizeChatText(string message)
    {
        return message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static SeString CreateGreenText(string message)
    {
        return new SeStringBuilder()
            .AddUiForeground(message, ChatGreenColorKey)
            .Build();
    }

    private void UpdateDebugState(bool inDmu, IReadOnlyList<PartyStatusEntry> entries)
    {
        if (!Configuration.DebugChat)
        {
            debugRecognizedTerritory = inDmu;
            debugKnownEntries.Clear();
            foreach (var entry in entries)
            {
                debugKnownEntries[entry.Key] = entry;
            }

            return;
        }

        if (inDmu && !debugRecognizedTerritory)
        {
            PrintDebug("Recognized Dancing Mad Ultimate.");
        }
        else if (!inDmu && debugRecognizedTerritory)
        {
            PrintDebug("Left Dancing Mad Ultimate.");
        }

        var nextEntries = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        foreach (var entry in nextEntries.Values)
        {
            if (!debugKnownEntries.ContainsKey(entry.Key))
            {
                PrintDebug($"{entry.MemberName} gained {entry.StatusName}.");
            }
        }

        foreach (var entry in debugKnownEntries.Values)
        {
            if (!nextEntries.ContainsKey(entry.Key))
            {
                PrintDebug($"{entry.MemberName} lost {entry.StatusName}.");
            }
        }

        debugRecognizedTerritory = inDmu;
        debugKnownEntries.Clear();
        foreach (var entry in nextEntries.Values)
        {
            debugKnownEntries[entry.Key] = entry;
        }
    }

    private static void PrintDebug(string message)
    {
        ChatGui.Print($"[DMU P3 Blackhole Helper] {message}");
    }

    private void OnDutyReset(IDutyStateEventArgs args)
    {
        if (args.TerritoryType.RowId != DmuTerritoryId)
        {
            return;
        }

        CaptureCurrentPullSnapshot("Wipe/reset detected");
        currentEntries.Clear();
        currentMembers.Clear();
        currentAssignments.Clear();
        LocalAssignment = null;
        accretionHistory.Clear();
        lineHistory.Clear();
        currentBlackHoleResolutions.Clear();
        currentPartyDeaths.Clear();
        capturedBlackHoleResolutionKeys.Clear();
        lastIncomingActionByMember.Clear();
        deadMemberKeys.Clear();
        ResetBlackHoleState(resetInstructionCallouts: true);
    }

    private LocalPlayerBlackHoleAssignment? BuildLocalAssignment()
    {
        var localContentId = PlayerState.ContentId;
        var localMember = currentMembers.FirstOrDefault(member => member.ContentId != 0 && member.ContentId == localContentId);
        if (localMember is null)
        {
            return null;
        }

        return currentAssignments.FirstOrDefault(assignment => assignment.MemberKey == localMember.MemberKey) ??
            BuildAssignmentForMember(localMember, currentEntries, lineHistory, accretionHistory);
    }

    private static LocalPlayerBlackHoleAssignment BuildAssignmentForMember(
        PartyMemberSnapshot member,
        IReadOnlyList<PartyStatusEntry> entries,
        IReadOnlyDictionary<string, PartyStatusEntry> lineEntriesByMember,
        IReadOnlyCollection<string> accretionMemberKeys)
    {
        var currentLineEntry = entries
            .Where(entry => entry.MemberKey == member.MemberKey && entry.Kind == StatusKind.LineOrder)
            .OrderBy(entry => entry.SortOrder)
            .FirstOrDefault();
        lineEntriesByMember.TryGetValue(member.MemberKey, out var historicalLineEntry);
        var lineEntry = currentLineEntry ?? historicalLineEntry;

        if (lineEntry is null)
        {
            return new LocalPlayerBlackHoleAssignment(
                member.MemberKey,
                member.MemberName,
                member.PartyIndex,
                member.IsDps,
                accretionMemberKeys.Contains(member.MemberKey),
                LineGroup.None,
                0,
                "No line debuff",
                0.0f);
        }

        return new LocalPlayerBlackHoleAssignment(
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.IsDps,
            accretionMemberKeys.Contains(member.MemberKey),
            GetLineGroup(lineEntry.StatusId),
            lineEntry.StatusId,
            lineEntry.StatusName,
            currentLineEntry?.RemainingTime ?? 0.0f);
    }

    private void CaptureCurrentPullSnapshot(string reason)
    {
        if (!HasP3Record())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var combatElapsedSeconds = blackHoleStartedAtUtc is not null
            ? (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds
            : lastKnownBlackHoleElapsedSeconds;
        pullSnapshots.Add(new BlackHolePullSnapshot(
            DateTime.UtcNow,
            reason,
            combatElapsedSeconds,
            currentEntries.ToList(),
            currentMembers.ToList(),
            currentAssignments.ToList(),
            currentBlackHoleResolutions.ToList(),
            MechanicState,
            currentPartyDeaths.ToList()));
    }

    private bool HasP3Record()
    {
        return currentEntries.Count > 0 ||
            currentBlackHoleResolutions.Count > 0 ||
            currentPartyDeaths.Count > 0 ||
            accretionHistory.Count > 0 ||
            lineHistory.Count > 0 ||
            blackHoleStartedAtUtc is not null;
    }

    private static LineGroup GetLineGroup(uint statusId)
    {
        return statusId switch
        {
            3004 => LineGroup.First,
            3005 => LineGroup.Second,
            3006 => LineGroup.Third,
            _ => LineGroup.None,
        };
    }

    private static bool IsDpsJob(uint classJobId)
    {
        return classJobId is
            2 or 4 or 5 or 7 or 26 or 29 or
            20 or 22 or 23 or 25 or 27 or 30 or 31 or 34 or 35 or 36 or 38 or 39 or 41 or 42;
    }

    private readonly record struct QueuedChatMessage(
        AssignmentChatChannel Channel,
        string Message);
}
