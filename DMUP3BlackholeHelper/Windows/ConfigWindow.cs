using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DMUP3BlackholeHelper.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private static readonly Vector4 ActiveTextColor = new(0.25f, 1.0f, 0.35f, 1.0f);
    private static readonly Vector4 ErrorTextColor = new(1.0f, 0.25f, 0.25f, 1.0f);
    private static readonly Vector4 UnassignedHitTextColor = new(0.35f, 0.65f, 1.0f, 1.0f);
    private const float ReviewGraceSeconds = 4.0f;

    private sealed record BlackHoleResolutionGroup(
        DateTime SeenAtUtc,
        BlackHoleWave Wave,
        uint ActionId,
        string ActionName,
        string GlobalSequence,
        int? Tether,
        IReadOnlyList<BlackHoleResolutionHit> ExpectedHits,
        IReadOnlyList<BlackHoleResolutionHit> UnexpectedHits);

    private sealed record BlackHoleWaveReview(
        BlackHoleWave Wave,
        IReadOnlyList<LocalPlayerBlackHoleAssignment> ExpectedAssignments,
        IReadOnlyList<BlackHoleResolutionGroup> ResolutionGroups,
        bool CanShowMissing,
        bool ShouldReview,
        IReadOnlySet<string> ExpectedHitKeys)
    {
        public static BlackHoleWaveReview Empty(BlackHoleWave wave)
        {
            return new BlackHoleWaveReview(wave, [], [], false, false, new HashSet<string>(StringComparer.Ordinal));
        }
    }

    public ConfigWindow(Plugin plugin) : base("DMU P3 Blackhole Helper###DMUP3BlackholeConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##DMUP3BlackholeTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Buff Summary"))
        {
            DrawBuffSummaryTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettingsTab()
    {
        if (ImGui.Button("Open helper"))
        {
            plugin.OpenHelperUi();
        }

        var showHelper = configuration.ShowHelper;
        if (ImGui.Checkbox("Show helper window", ref showHelper))
        {
            plugin.SetShowHelper(showHelper);
        }

        var postInstructionsToChat = configuration.PostInstructionsToChat;
        if (ImGui.Checkbox("Post my BH job to chat", ref postInstructionsToChat))
        {
            plugin.SetPostInstructionsToChat(postInstructionsToChat);
        }

        if (postInstructionsToChat)
        {
            DrawSoundEffectSetting();
        }

        var showPreview = configuration.ShowPreviewWhenInactive;
        if (ImGui.Checkbox("Preview helper when inactive", ref showPreview))
        {
            plugin.SetPreviewWhenInactive(showPreview);
        }

        var helperFontScale = configuration.HelperFontScale;
        if (ImGui.SliderFloat("Helper font scale", ref helperFontScale, 0.75f, 2.0f, "%.2f"))
        {
            plugin.SetHelperFontScale(helperFontScale);
        }

        var helperIconScale = configuration.HelperIconScale;
        if (ImGui.SliderFloat("Helper icon scale", ref helperIconScale, 0.75f, 3.0f, "%.2f"))
        {
            plugin.SetHelperIconScale(helperIconScale);
        }

        var helperBackgroundOpacity = configuration.HelperBackgroundOpacity;
        if (ImGui.SliderFloat("Helper background opacity", ref helperBackgroundOpacity, 0.15f, 1.0f, "%.2f"))
        {
            plugin.SetHelperBackgroundOpacity(helperBackgroundOpacity);
        }

        var debugChat = configuration.DebugChat;
        if (ImGui.Checkbox("Debug chat", ref debugChat))
        {
            plugin.SetDebugChat(debugChat);
        }

        ImGui.Separator();
        ImGui.TextWrapped("The helper only scans party statuses and Black Hole action effects while you are in DMU.");
        ImGui.TextWrapped("Buff Summary keeps each recorded P3 pull's line groups, Accretion history, Black Hole hit review, and death timeline for troubleshooting after a wipe.");
        ImGui.TextColored(ActiveTextColor, "Green means the expected tether player was hit by the resolving blast.");
        ImGui.TextColored(ErrorTextColor, "Red means an expected tether player was not hit by a Black Hole blast.");
        ImGui.TextColored(UnassignedHitTextColor, "Blue means an unassigned player was hit by a Black Hole blast.");
    }

    private void DrawSoundEffectSetting()
    {
        var selectedSound = Plugin.SoundEffectOptions.FirstOrDefault(option => option.Id == configuration.NowSoundEffectId) ??
            Plugin.SoundEffectOptions[0];

        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.BeginCombo("Mechanic alert", selectedSound.Label))
        {
            foreach (var option in Plugin.SoundEffectOptions)
            {
                var isSelected = option.Id == selectedSound.Id;
                if (ImGui.Selectable(option.Label, isSelected))
                {
                    plugin.SetNowSoundEffectId(option.Id);
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var hasSound = selectedSound.Id != 0;
        if (!hasSound)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Test sound") && hasSound)
        {
            plugin.TestNowSoundEffect();
        }

        if (!hasSound)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawBuffSummaryTab()
    {
        DrawBuffSummaryExample();

        if (!plugin.IsInDmu)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Waiting for DMU.");
            DrawPullHistory(plugin.PullSnapshots);
            return;
        }

        var entries = plugin.CurrentEntries;
        if (HasCurrentPullData(entries))
        {
            DrawCurrentPullSummary(entries);
        }
        else
        {
            ImGui.TextDisabled("Waiting for P3 debuffs.");
        }

        DrawPullHistory(plugin.PullSnapshots);
    }

    private static void DrawBuffSummaryExample()
    {
        if (!ImGui.CollapsingHeader("Example###BuffSummaryExample"))
        {
            return;
        }

        var assignments = CreateExampleAssignments();
        var entries = CreateExampleEntries(assignments);
        var wave = BlackHoleTimeline.Waves.First(wave => wave.Set == 2 && wave.Wave == 1);
        var capturedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        ImGui.TextDisabled($"Example pull - {capturedAt:HH:mm:ss} UTC");
        ImGui.TextWrapped("This stays visible so the mechanic attempt can be reviewed.");
        DrawSnapshotAssignments(assignments);
        ImGui.Separator();
        var resolutions = new[]
        {
            new BlackHoleResolutionRecord(
                capturedAt,
                wave,
                0,
                "example-1",
                "Example Black Hole",
                [
                    CreateExampleHit(assignments[0], hitOrder: 0, wasExpected: true),
                    CreateExampleHit(assignments[4], hitOrder: 1, wasExpected: false),
                    CreateExampleHit(assignments[5], hitOrder: 2, wasExpected: false),
                ]),
            new BlackHoleResolutionRecord(
                capturedAt,
                wave,
                0,
                "example-2",
                "Example Black Hole",
                [CreateExampleHit(assignments[6], hitOrder: 0, wasExpected: false)]),
            new BlackHoleResolutionRecord(
                capturedAt,
                wave,
                0,
                "example-3",
                "Example Black Hole",
                [CreateExampleHit(assignments[2], hitOrder: 0, wasExpected: true)]),
        };

        DrawBlackHoleResolutionReview(
            assignments,
            resolutions,
            new BlackHoleMechanicState(false, wave.MarkerAtSeconds + ReviewGraceSeconds, null, null, wave, 3),
            "Previous pull hit review",
            true,
            "Example");
        ImGui.Separator();
        DrawDeathTimeline(
            [
                new PartyDeathRecord(
                    capturedAt.AddSeconds(1),
                    wave.MarkerAtSeconds + 1.0f,
                    assignments[4].MemberKey,
                    assignments[4].MemberName,
                    assignments[4].PartyIndex,
                    0,
                    "Example Black Hole",
                    "example-death-1",
                    wave),
                new PartyDeathRecord(
                    capturedAt.AddSeconds(4),
                    wave.MarkerAtSeconds + 4.0f,
                    assignments[1].MemberKey,
                    assignments[1].MemberName,
                    assignments[1].PartyIndex,
                    0,
                    "Unknown",
                    string.Empty,
                    wave),
            ],
            "Previous pull death timeline");
        ImGui.Separator();
        DrawLineGroups(entries);
        ImGui.Separator();
        DrawEarthSummary(entries);
    }

    private static IReadOnlyList<LocalPlayerBlackHoleAssignment> CreateExampleAssignments()
    {
        return
        [
            CreateExampleAssignment(1, isDps: true, hadAccretion: false, LineGroup.First),
            CreateExampleAssignment(2, isDps: false, hadAccretion: false, LineGroup.First),
            CreateExampleAssignment(3, isDps: true, hadAccretion: true, LineGroup.First),
            CreateExampleAssignment(4, isDps: true, hadAccretion: false, LineGroup.Second),
            CreateExampleAssignment(5, isDps: false, hadAccretion: false, LineGroup.Second),
            CreateExampleAssignment(6, isDps: true, hadAccretion: true, LineGroup.Second),
            CreateExampleAssignment(7, isDps: true, hadAccretion: false, LineGroup.Third),
            CreateExampleAssignment(8, isDps: false, hadAccretion: false, LineGroup.Third),
        ];
    }

    private static LocalPlayerBlackHoleAssignment CreateExampleAssignment(
        int playerNumber,
        bool isDps,
        bool hadAccretion,
        LineGroup lineGroup)
    {
        var lineStatusId = lineGroup switch
        {
            LineGroup.First => 3004u,
            LineGroup.Second => 3005u,
            LineGroup.Third => 3006u,
            _ => 0u,
        };
        var lineName = lineGroup switch
        {
            LineGroup.First => "First in Line",
            LineGroup.Second => "Second in Line",
            LineGroup.Third => "Third in Line",
            _ => "No line debuff",
        };

        return new LocalPlayerBlackHoleAssignment(
            $"example-player-{playerNumber}",
            $"Player {playerNumber}",
            playerNumber - 1,
            isDps,
            hadAccretion,
            lineGroup,
            lineStatusId,
            lineName,
            0.0f);
    }

    private static IReadOnlyList<PartyStatusEntry> CreateExampleEntries(IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments)
    {
        return assignments
            .SelectMany(assignment =>
            {
                var entries = new List<PartyStatusEntry>
                {
                    new(
                        $"{assignment.MemberKey}:{assignment.LineStatusId}",
                        assignment.MemberKey,
                        assignment.MemberName,
                        assignment.PartyIndex,
                        assignment.IsDps,
                        assignment.LineStatusId,
                        assignment.LineName,
                        StatusKind.LineOrder,
                        0.0f,
                        assignment.LineGroup switch
                        {
                            LineGroup.First => 10,
                            LineGroup.Second => 20,
                            LineGroup.Third => 30,
                            _ => 0,
                        }),
                };

                if (assignment.HadAccretion)
                {
                    entries.Add(new PartyStatusEntry(
                        $"{assignment.MemberKey}:1604",
                        assignment.MemberKey,
                        assignment.MemberName,
                        assignment.PartyIndex,
                        assignment.IsDps,
                        1604,
                        "Accretion",
                        StatusKind.Accretion,
                        0.0f,
                        40));
                }

                return entries;
            })
            .ToList();
    }

    private static BlackHoleResolutionHit CreateExampleHit(
        LocalPlayerBlackHoleAssignment assignment,
        int hitOrder,
        bool wasExpected)
    {
        return new BlackHoleResolutionHit(
            assignment.MemberKey,
            assignment.MemberName,
            assignment.PartyIndex,
            assignment.RoleName,
            hitOrder,
            wasExpected);
    }

    private bool HasCurrentPullData(IReadOnlyList<PartyStatusEntry> entries)
    {
        return entries.Count > 0 ||
            plugin.CurrentAssignments.Any(assignment => assignment.HasLine) ||
            plugin.CurrentBlackHoleResolutions.Count > 0 ||
            plugin.CurrentPartyDeaths.Count > 0;
    }

    private void DrawCurrentPullSummary(IReadOnlyList<PartyStatusEntry> entries)
    {
        var header = $"Current pull - Timer {FormatCombatTimer(plugin.CurrentPullElapsedSeconds)}###CurrentPullSummary";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No live P3 statuses detected. Showing remembered pull assignments.");
            DrawSnapshotAssignmentsWithChat(plugin.CurrentAssignments, "Current pull", "Current");
            ImGui.Separator();
            DrawBlackHoleResolutionReview(
                plugin.CurrentAssignments,
                plugin.CurrentBlackHoleResolutions,
                plugin.MechanicState,
                "Current pull hit review",
                false,
                "Current");
            ImGui.Separator();
            DrawDeathTimeline(plugin.CurrentPartyDeaths, "Current pull death timeline");
            return;
        }

        DrawLocalAssignment();
        ImGui.Separator();
        DrawPersonalBlackHoleInstructions();
        ImGui.Separator();
        DrawSnapshotAssignmentsWithChat(plugin.CurrentAssignments, "Current pull", "Current");
        ImGui.Separator();
        DrawAccretionSummary(entries);
        ImGui.Separator();
        DrawLineGroups(entries);
        ImGui.Separator();
        DrawEarthSummary(entries);
        ImGui.Separator();
        DrawBlackHoleResolutionReview(
            plugin.CurrentAssignments,
            plugin.CurrentBlackHoleResolutions,
            plugin.MechanicState,
            "Current pull hit review",
            false,
            "Current");
        ImGui.Separator();
        DrawDeathTimeline(plugin.CurrentPartyDeaths, "Current pull death timeline");
        ImGui.Separator();
        DrawMechanicRules(entries);
        ImGui.Separator();
        DrawBlackHoleStrategy();
    }

    private void DrawPullHistory(IReadOnlyList<BlackHolePullSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recorded pulls");
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            DrawPullSnapshot(snapshots[i], i + 1);
        }
    }

    private void DrawPullSnapshot(BlackHolePullSnapshot snapshot, int pullNumber)
    {
        var header = $"Pull {pullNumber} - Timer {FormatCombatTimer(snapshot.CombatElapsedSeconds)}###PullSnapshot{pullNumber}";
        if (!ImGui.CollapsingHeader(header))
        {
            return;
        }

        ImGui.TextDisabled($"{snapshot.Reason} - {snapshot.CapturedAtUtc:HH:mm:ss} UTC");
        ImGui.TextWrapped("This stays visible so the mechanic attempt can be reviewed.");
        DrawSnapshotAssignmentsWithChat(snapshot.Assignments, $"Pull {pullNumber}", $"Pull{pullNumber}");
        ImGui.Separator();
        DrawBlackHoleResolutionReview(snapshot.Assignments, snapshot.Resolutions, snapshot.MechanicState, "Previous pull hit review", true, $"Pull{pullNumber}");
        ImGui.Separator();
        DrawDeathTimeline(snapshot.Deaths, "Previous pull death timeline");
        ImGui.Separator();
        DrawLineGroups(snapshot.Entries);
        ImGui.Separator();
        DrawEarthSummary(snapshot.Entries);
    }

    private void DrawSnapshotAssignmentsWithChat(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        string pullLabel,
        string idSuffix)
    {
        ImGui.TextUnformatted("Recorded assignments");
        DrawAssignmentChatChannelSelector(idSuffix);
        DrawAssignmentGroup(assignments, LineGroup.First, "First in Line", pullLabel, idSuffix);
        DrawAssignmentGroup(assignments, LineGroup.Second, "Second in Line", pullLabel, idSuffix);
        DrawAssignmentGroup(assignments, LineGroup.Third, "Third in Line", pullLabel, idSuffix);
        DrawAccretionHistory(assignments, pullLabel, idSuffix);
    }

    private void DrawAssignmentChatChannelSelector(string idSuffix)
    {
        var effectiveChannel = Plugin.GetEffectiveChatChannel(configuration.AssignmentChatChannel);
        ImGui.SetNextItemWidth(185.0f);
        if (ImGui.BeginCombo($"Assignment chat##AssignmentChatChannel{idSuffix}", Plugin.GetChatChannelLabel(effectiveChannel)))
        {
            foreach (var option in Plugin.ChatChannelOptions)
            {
                var selected = effectiveChannel == option.Channel;
                if (ImGui.Selectable(option.Label, selected))
                {
                    plugin.SetAssignmentChatChannel(option.Channel);
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Controls where the per-player assignment buttons post.");
        }
    }

    private static void DrawSnapshotAssignments(IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments)
    {
        ImGui.TextUnformatted("Recorded assignments");
        DrawAssignmentGroup(assignments, LineGroup.First, "First in Line");
        DrawAssignmentGroup(assignments, LineGroup.Second, "Second in Line");
        DrawAssignmentGroup(assignments, LineGroup.Third, "Third in Line");
        DrawAccretionHistory(assignments);
    }

    private static void DrawAccretionHistory(IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments)
    {
        var accretionAssignments = assignments
            .Where(assignment => assignment.HadAccretion)
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();

        ImGui.TextUnformatted($"Accretion history ({accretionAssignments.Count})");
        if (accretionAssignments.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("none recorded");
            return;
        }

        foreach (var assignment in accretionAssignments)
        {
            ImGui.BulletText($"{assignment.MemberName} - {assignment.RoleName}");
        }
    }

    private void DrawAccretionHistory(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        string pullLabel,
        string idSuffix)
    {
        var accretionAssignments = assignments
            .Where(assignment => assignment.HadAccretion)
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();

        ImGui.TextUnformatted($"Accretion history ({accretionAssignments.Count})");
        if (accretionAssignments.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("none recorded");
            return;
        }

        foreach (var assignment in accretionAssignments)
        {
            ImGui.BulletText($"{assignment.MemberName} - {assignment.RoleName}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Post##PostAccretionAssignment{idSuffix}{assignment.PartyIndex}{assignment.MemberKey}"))
            {
                plugin.PrintAssignmentToChat(assignment, pullLabel);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Posts {assignment.MemberName}'s Accretion assignment to the selected chat channel.");
            }
        }
    }

    private void DrawAssignmentGroup(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        LineGroup lineGroup,
        string label,
        string pullLabel,
        string idSuffix)
    {
        var group = assignments
            .Where(assignment => assignment.LineGroup == lineGroup)
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();

        ImGui.TextUnformatted($"{label} ({group.Count})");
        if (group.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("none recorded");
            return;
        }

        foreach (var assignment in group)
        {
            ImGui.BulletText($"{assignment.MemberName} - {assignment.RoleName}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Post##PostAssignment{idSuffix}{assignment.PartyIndex}{assignment.MemberKey}"))
            {
                plugin.PrintAssignmentToChat(assignment, pullLabel);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Posts {assignment.MemberName}'s assignment to the selected chat channel.");
            }
        }
    }

    private static void DrawAssignmentGroup(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        LineGroup lineGroup,
        string label)
    {
        var group = assignments
            .Where(assignment => assignment.LineGroup == lineGroup)
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();

        ImGui.TextUnformatted($"{label} ({group.Count})");
        if (group.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("none recorded");
            return;
        }

        foreach (var assignment in group)
        {
            ImGui.BulletText($"{assignment.MemberName} - {assignment.RoleName}");
        }
    }

    private static void DrawAccretionSummary(IReadOnlyList<PartyStatusEntry> entries)
    {
        ImGui.TextUnformatted("Accretion");
        var accretions = entries
            .Where(entry => entry.Kind == StatusKind.Accretion)
            .OrderBy(entry => entry.PartyIndex)
            .ToList();

        if (accretions.Count == 0)
        {
            ImGui.TextDisabled("No Accretion targets detected.");
            return;
        }

        foreach (var entry in accretions)
        {
            var lineOrder = GetLineOrderForMember(entries, entry.MemberKey);
            ImGui.BulletText($"{entry.MemberName} - {lineOrder} - {FormatTime(entry.RemainingTime)}");
        }

        var firstLineAccretion = accretions.FirstOrDefault(entry => HasStatus(entries, entry.MemberKey, 3004));
        if (firstLineAccretion is not null)
        {
            ImGui.TextWrapped($"Heal {firstLineAccretion.MemberName} first if both Accretions are active.");
        }
    }

    private static void DrawLineGroups(IReadOnlyList<PartyStatusEntry> entries)
    {
        ImGui.TextUnformatted("Line groups");
        DrawStatusGroup(entries, 3004, "First in Line");
        DrawStatusGroup(entries, 3005, "Second in Line");
        DrawStatusGroup(entries, 3006, "Third in Line");
    }

    private static void DrawEarthSummary(IReadOnlyList<PartyStatusEntry> entries)
    {
        ImGui.TextUnformatted("Earth Resistance Down");
        var earthTargets = entries
            .Where(entry => entry.Kind == StatusKind.EarthResistance)
            .OrderBy(entry => entry.PartyIndex)
            .ToList();

        if (earthTargets.Count == 0)
        {
            ImGui.TextDisabled("Not active.");
            return;
        }

        ImGui.TextWrapped($"{earthTargets.Count} active. Do not cleanse another earth debuff until it falls off.");
        foreach (var entry in earthTargets)
        {
            ImGui.BulletText($"{entry.MemberName} - {FormatTime(entry.RemainingTime)}");
        }
    }

    private static void DrawBlackHoleResolutionReview(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        IReadOnlyList<BlackHoleResolutionRecord> resolutions,
        BlackHoleMechanicState mechanicState,
        string label,
        bool finalizeMissing,
        string idSuffix)
    {
        ImGui.TextUnformatted(label);
        ImGui.TextWrapped("Uses resolving Black Hole action effects and only records party members hit by the blast.");

        var hasAnyReview = false;
        foreach (var setNumber in BlackHoleTimeline.Waves.Select(wave => wave.Set).Distinct().OrderBy(setNumber => setNumber))
        {
            if (DrawBlackHoleSetReview(assignments, resolutions, mechanicState, setNumber, finalizeMissing, idSuffix))
            {
                hasAnyReview = true;
            }
        }

        if (!hasAnyReview)
        {
            ImGui.TextDisabled("No Black Hole resolution records yet.");
        }
    }

    private static bool DrawBlackHoleSetReview(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        IReadOnlyList<BlackHoleResolutionRecord> resolutions,
        BlackHoleMechanicState mechanicState,
        int setNumber,
        bool finalizeMissing,
        string idSuffix)
    {
        var waveReviews = BlackHoleTimeline.Waves
            .Where(wave => wave.Set == setNumber)
            .Select(wave => BuildWaveReview(assignments, resolutions, mechanicState, wave, finalizeMissing))
            .Where(review => review.ShouldReview)
            .ToList();
        if (waveReviews.Count == 0)
        {
            return false;
        }

        var flags = setNumber == 1 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!ImGui.CollapsingHeader($"Black Hole Set {setNumber}###BlackHoleSetReview{idSuffix}{setNumber}", flags))
        {
            return true;
        }

        foreach (var review in waveReviews)
        {
            ImGui.TextUnformatted(review.Wave.Label);
            foreach (var resolutionGroup in review.ResolutionGroups)
            {
                DrawResolutionGroup(resolutionGroup);
            }

            if (!review.CanShowMissing)
            {
                continue;
            }

            foreach (var assignment in review.ExpectedAssignments.Where(assignment => !review.ExpectedHitKeys.Contains(assignment.MemberKey)))
            {
                ImGui.TextColored(
                    ErrorTextColor,
                    $"{assignment.MemberName} ({assignment.RoleName}) was not hit by a recorded Black Hole blast for {review.Wave.Label}.");
            }
        }

        return true;
    }

    private static BlackHoleWaveReview BuildWaveReview(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        IReadOnlyList<BlackHoleResolutionRecord> resolutions,
        BlackHoleMechanicState mechanicState,
        BlackHoleWave wave,
        bool finalizeMissing)
    {
        var expectedAssignments = GetExpectedAssignmentsForWave(assignments, wave);
        if (expectedAssignments.Count == 0)
        {
            return BlackHoleWaveReview.Empty(wave);
        }

        var waveResolutions = resolutions
            .Where(resolution => resolution.Wave.Set == wave.Set && resolution.Wave.Wave == wave.Wave)
            .OrderBy(resolution => resolution.SeenAtUtc)
            .ToList();
        var resolutionGroups = BuildResolutionGroups(assignments, wave, waveResolutions);
        var canShowMissing = finalizeMissing ||
            (mechanicState.IsActive && mechanicState.ElapsedSeconds >= wave.MarkerAtSeconds + ReviewGraceSeconds);
        var expectedHitKeys = resolutionGroups
            .SelectMany(resolutionGroup => resolutionGroup.ExpectedHits)
            .Select(hit => hit.MemberKey)
            .ToHashSet(StringComparer.Ordinal);

        return new BlackHoleWaveReview(
            wave,
            expectedAssignments,
            resolutionGroups,
            canShowMissing,
            resolutionGroups.Count > 0 || canShowMissing,
            expectedHitKeys);
    }

    private static void DrawDeathTimeline(IReadOnlyList<PartyDeathRecord> deaths, string label)
    {
        ImGui.TextUnformatted(label);
        if (deaths.Count == 0)
        {
            ImGui.TextDisabled("No deaths recorded.");
            return;
        }

        var orderedDeaths = deaths
            .OrderBy(death => death.SeenAtUtc)
            .ThenBy(death => death.PartyIndex)
            .ToList();
        for (var i = 0; i < orderedDeaths.Count; i++)
        {
            var death = orderedDeaths[i];
            ImGui.BulletText($"{i + 1}. {FormatCombatTimer(death.CombatElapsedSeconds)} - {death.MemberName} died to {FormatDeathCause(death)}.");
        }
    }

    private static IReadOnlyList<BlackHoleResolutionGroup> BuildResolutionGroups(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        BlackHoleWave wave,
        IReadOnlyList<BlackHoleResolutionRecord> resolutions)
    {
        return resolutions
            .GroupBy(resolution => new
            {
                resolution.ActionId,
                resolution.ActionName,
                resolution.GlobalSequence,
            })
            .OrderBy(group => group.Min(resolution => resolution.SeenAtUtc))
            .Select(group =>
            {
                var firstResolution = group
                    .OrderBy(resolution => resolution.SeenAtUtc)
                    .First();
                var hits = MergeResolutionHits(group.SelectMany(resolution => resolution.Hits));
                var tether = GetPrimaryTetherForResolution(assignments, wave, hits);
                var expectedHits = GetExpectedHitsForTether(assignments, wave, hits, tether);
                var expectedKeys = expectedHits.Select(hit => hit.MemberKey).ToHashSet(StringComparer.Ordinal);
                var unexpectedHits = hits
                    .Where(hit => !expectedKeys.Contains(hit.MemberKey))
                    .OrderBy(hit => hit.PartyIndex)
                    .ToList();

                return new BlackHoleResolutionGroup(
                    firstResolution.SeenAtUtc,
                    firstResolution.Wave,
                    group.Key.ActionId,
                    group.Key.ActionName,
                    group.Key.GlobalSequence,
                    tether,
                    expectedHits,
                    unexpectedHits);
            })
            .ToList();
    }

    private static IReadOnlyList<BlackHoleResolutionHit> MergeResolutionHits(IEnumerable<BlackHoleResolutionHit> hits)
    {
        return hits
            .GroupBy(hit => hit.MemberKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedHits = group
                    .OrderBy(hit => hit.HitOrder)
                    .ThenBy(hit => hit.PartyIndex)
                    .ToList();
                var firstHit = orderedHits[0];
                return firstHit with
                {
                    HitOrder = orderedHits.Min(hit => hit.HitOrder),
                    WasExpected = orderedHits.Any(hit => hit.WasExpected),
                };
            })
            .OrderBy(hit => hit.HitOrder)
            .ThenBy(hit => hit.PartyIndex)
            .ToList();
    }

    private static int? GetPrimaryTetherForResolution(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        BlackHoleWave wave,
        IReadOnlyList<BlackHoleResolutionHit> hits)
    {
        foreach (var hit in hits.OrderBy(hit => hit.HitOrder).ThenBy(hit => hit.PartyIndex))
        {
            var tether = GetTetherForHit(assignments, wave, hit);
            if (tether is not null)
            {
                return tether;
            }
        }

        return null;
    }

    private static IReadOnlyList<BlackHoleResolutionHit> GetExpectedHitsForTether(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        BlackHoleWave wave,
        IReadOnlyList<BlackHoleResolutionHit> hits,
        int? tether)
    {
        if (tether is null)
        {
            return [];
        }

        return hits
            .Where(hit => GetTetherForHit(assignments, wave, hit) == tether.Value)
            .OrderBy(hit => hit.PartyIndex)
            .ToList();
    }

    private static int? GetTetherForHit(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        BlackHoleWave wave,
        BlackHoleResolutionHit hit)
    {
        var assignment = assignments.FirstOrDefault(assignment => assignment.MemberKey == hit.MemberKey);
        if (assignment is null)
        {
            return null;
        }

        return BlackHoleStrategy.Instructions
            .Where(instruction => instruction.IsForWave(wave) && instruction.Role.Matches(assignment))
            .OrderBy(instruction => instruction.Tether)
            .Select(instruction => (int?)instruction.Tether)
            .FirstOrDefault();
    }

    private static void DrawResolutionGroup(BlackHoleResolutionGroup resolution)
    {
        var expectedHits = resolution.ExpectedHits;
        var unexpectedHits = resolution.UnexpectedHits;
        var actionLabel = FormatAction(resolution.ActionId, resolution.ActionName);
        ImGui.TextUnformatted($"{FormatResolutionTetherLabel(resolution)} - {actionLabel}");
        ImGui.Indent();

        if (expectedHits.Count == 0)
        {
            ImGui.TextColored(ErrorTextColor, $"Expected tether player was not hit by {actionLabel}.");
        }
        else
        {
            var verb = expectedHits.Count == 1 ? "was" : "were";
            ImGui.TextColored(
                ActiveTextColor,
                $"{FormatHits(expectedHits)} {verb} hit with {actionLabel}, resolving the tether.");
        }

        if (unexpectedHits.Count > 0)
        {
            var label = unexpectedHits.Count == 1 ? "Unassigned player" : "Unassigned players";
            ImGui.TextColored(UnassignedHitTextColor, $"{label} hit by the blast: {FormatHits(unexpectedHits)}.");
        }

        ImGui.Unindent();
    }

    private static IReadOnlyList<LocalPlayerBlackHoleAssignment> GetExpectedAssignmentsForWave(
        IReadOnlyList<LocalPlayerBlackHoleAssignment> assignments,
        BlackHoleWave wave)
    {
        var waveInstructions = BlackHoleStrategy.Instructions
            .Where(instruction => instruction.IsForWave(wave))
            .ToList();

        return assignments
            .Where(assignment => waveInstructions.Any(instruction => instruction.Role.Matches(assignment)))
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();
    }

    private static string FormatResolutionTetherLabel(BlackHoleResolutionGroup resolution)
    {
        if (resolution.Tether is null)
        {
            return $"Unmatched blast ({resolution.Wave.Label})";
        }

        return $"{FormatShortTetherOrdinal(resolution.Tether.Value)} tether ({resolution.Wave.Label})";
    }

    private static string FormatShortTetherOrdinal(int tether)
    {
        return tether switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{tether}th",
        };
    }

    private static string FormatHits(IReadOnlyList<BlackHoleResolutionHit> hits)
    {
        return string.Join(", ", hits.Select(hit => $"{hit.MemberName} ({hit.ActualRole})"));
    }

    private static string FormatAction(uint actionId, string actionName)
    {
        return $"{actionName} ({actionId})";
    }

    private static string FormatDeathCause(PartyDeathRecord death)
    {
        var actionText = death.ActionId == 0
            ? death.ActionName
            : FormatAction(death.ActionId, death.ActionName);
        return death.Wave is null
            ? actionText
            : $"{actionText} during {death.Wave.Label}";
    }

    private static void DrawMechanicRules(IReadOnlyList<PartyStatusEntry> entries)
    {
        var crustCount = entries.Count(entry => entry.Kind == StatusKind.PrimordialCrust);
        ImGui.TextUnformatted("Resolution");
        ImGui.BulletText("Heal Accretion targets to full one at a time.");
        ImGui.BulletText("First in Line cleanses earliest, then Second, then Third.");
        ImGui.BulletText("Tethers must be taken manually by the assigned players.");
        ImGui.BulletText("Use the Black Hole set/wave instructions for tether assignment.");
        ImGui.TextWrapped($"Primordial Crust still detected on {crustCount} party member(s).");
    }

    private void DrawBlackHoleStrategy()
    {
        ImGui.TextUnformatted("Black Hole strategy");
        ImGui.TextWrapped("All tether assignments below are clockwise from Kefka.");

        if (!ImGui.BeginTable("##BlackHoleStrategyAllSets", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Set");
        ImGui.TableSetupColumn("Wave");
        ImGui.TableSetupColumn("Tether");
        ImGui.TableSetupColumn("Role");
        ImGui.TableSetupColumn("Instruction");
        ImGui.TableHeadersRow();

        foreach (var instruction in BlackHoleStrategy.Instructions)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Set.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Wave.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Tether.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Role.DisplayName);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(instruction.Action);
        }

        ImGui.EndTable();
    }

    private void DrawLocalAssignment()
    {
        ImGui.TextUnformatted("Your Black Hole assignment");
        var assignment = plugin.LocalAssignment;
        if (assignment is null)
        {
            ImGui.TextDisabled("Local player not found in the party list.");
            return;
        }

        if (!assignment.HasLine)
        {
            ImGui.TextDisabled("Waiting for your line debuff.");
            return;
        }

        ImGui.BulletText($"Role: {assignment.RoleName}");
        ImGui.BulletText($"Line: {assignment.LineName} - {FormatTime(assignment.LineRemainingTime)}");
        ImGui.BulletText($"Job group: {(assignment.IsDps ? "DPS" : "Support")}");
        ImGui.BulletText($"Accretion this pull: {(assignment.HadAccretion ? "yes" : "no")}");
    }

    private void DrawPersonalBlackHoleInstructions()
    {
        ImGui.TextUnformatted("Your Black Hole instructions");
        var assignment = plugin.LocalAssignment;
        if (assignment is null)
        {
            ImGui.TextDisabled("Local player not found in the party list.");
            return;
        }

        if (!assignment.HasLine)
        {
            ImGui.TextDisabled("Waiting for your line debuff.");
            return;
        }

        var instructions = BlackHoleStrategy.GetInstructionsFor(assignment);
        if (instructions.Count == 0)
        {
            ImGui.TextDisabled("No tether assignment matched your current role.");
            return;
        }

        if (!ImGui.BeginTable("##PersonalBlackHoleInstructions", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Set");
        ImGui.TableSetupColumn("Wave");
        ImGui.TableSetupColumn("Tether");
        ImGui.TableSetupColumn("Instruction");
        ImGui.TableHeadersRow();

        foreach (var instruction in instructions)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Set.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Wave.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Tether.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(instruction.Action);
        }

        ImGui.EndTable();
    }

    private static void DrawStatusGroup(IReadOnlyList<PartyStatusEntry> entries, uint statusId, string label)
    {
        var group = entries
            .Where(entry => entry.StatusId == statusId)
            .OrderBy(entry => entry.PartyIndex)
            .ToList();

        ImGui.TextUnformatted($"{label} ({group.Count})");
        if (group.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("none");
            return;
        }

        for (var i = 0; i < group.Count; i++)
        {
            ImGui.BulletText($"{GetLinePrefix(statusId)}{i + 1}: {group[i].MemberName} - {FormatTime(group[i].RemainingTime)}");
        }
    }

    private static string GetLineOrderForMember(IReadOnlyList<PartyStatusEntry> entries, string memberKey)
    {
        var line = entries.FirstOrDefault(entry => entry.MemberKey == memberKey && entry.Kind == StatusKind.LineOrder);
        return line?.StatusName ?? "no line order";
    }

    private static bool HasStatus(IReadOnlyList<PartyStatusEntry> entries, string memberKey, uint statusId)
    {
        return entries.Any(entry => entry.MemberKey == memberKey && entry.StatusId == statusId);
    }

    private static string GetLinePrefix(uint statusId)
    {
        return statusId switch
        {
            3004 => "F",
            3005 => "S",
            3006 => "T",
            _ => "?",
        };
    }

    private static string FormatTime(float remainingTime)
    {
        return $"{MathF.Max(0.0f, remainingTime):0.0}s";
    }

    private static string FormatCombatTimer(float elapsedSeconds)
    {
        var totalSeconds = (int)MathF.Max(0.0f, elapsedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

}
