using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DMUP3BlackholeHelper.Windows;

public sealed class HelperWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Random previewRandom = new();
    private LocalPlayerBlackHoleAssignment? previewAssignment;
    private BlackHoleMechanicState previewState = BlackHoleMechanicState.Inactive;
    private int selectedPreviewRoleIndex;
    private bool wasShowingPreview;

    private static readonly Vector4 ActiveWaveColor = new(0.25f, 1.0f, 0.35f, 1.0f);
    private static readonly Vector4 DpsColor = new(0.95f, 0.22f, 0.18f, 1.0f);
    private static readonly Vector4 SupportTankColor = new(0.2f, 0.45f, 1.0f, 1.0f);
    private static readonly Vector4 SupportHealerColor = new(0.2f, 0.8f, 0.35f, 1.0f);
    private static readonly Vector4 AccretionColor = new(0.75f, 0.45f, 1.0f, 1.0f);
    private static readonly IReadOnlyList<PreviewRole> PreviewRoles =
    [
        new(LineGroup.First, IsDps: true, HadAccretion: false, "FIL DPS", PreviewRoleKind.Dps),
        new(LineGroup.Second, IsDps: true, HadAccretion: false, "SIL DPS", PreviewRoleKind.Dps),
        new(LineGroup.Third, IsDps: true, HadAccretion: false, "TIL DPS", PreviewRoleKind.Dps),
        new(LineGroup.First, IsDps: false, HadAccretion: false, "FIL Support", PreviewRoleKind.Support),
        new(LineGroup.Second, IsDps: false, HadAccretion: false, "SIL Support", PreviewRoleKind.Support),
        new(LineGroup.Third, IsDps: false, HadAccretion: false, "TIL Support", PreviewRoleKind.Support),
        new(LineGroup.First, IsDps: true, HadAccretion: true, "FIL Accretion", PreviewRoleKind.Accretion),
        new(LineGroup.Second, IsDps: true, HadAccretion: true, "SIL Accretion", PreviewRoleKind.Accretion),
    ];

    public HelperWindow(Plugin plugin) : base("DMU P3 Blackhole Helper###DMUP3BlackholeHelper")
    {
        this.plugin = plugin;

        Size = new Vector2(380, 260);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
        BgAlpha = GetHelperBackgroundOpacity();
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        BgAlpha = GetHelperBackgroundOpacity();
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.HelperFontScale, 0.75f, 2.0f));
        DrawPreviewToggle();

        var showPreview = !plugin.MechanicState.IsActive && plugin.Configuration.ShowPreviewWhenInactive;
        if (showPreview)
        {
            if (!wasShowingPreview || previewAssignment is null)
            {
                RandomizePreview();
            }

            ImGui.TextDisabled("Preview");
            ImGui.SameLine();
            DrawPreviewControls();

            DrawHelperContents(previewAssignment, previewState, []);
            wasShowingPreview = true;
            return;
        }

        wasShowingPreview = false;
        if (!plugin.IsInDmu)
        {
            ImGui.TextDisabled("Waiting for DMU.");
            return;
        }

        var entries = plugin.CurrentEntries;
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No watched Black Hole statuses detected.");
            return;
        }

        DrawHelperContents(plugin.LocalAssignment, plugin.MechanicState, entries);
    }

    private float GetHelperBackgroundOpacity()
    {
        return Math.Clamp(plugin.Configuration.HelperBackgroundOpacity, 0.15f, 1.0f);
    }

    private void DrawPreviewToggle()
    {
        var showPreview = plugin.Configuration.ShowPreviewWhenInactive;
        if (ImGui.Checkbox("Preview when inactive", ref showPreview))
        {
            plugin.SetPreviewWhenInactive(showPreview);
        }

        ImGui.Separator();
    }

    private void DrawHelperContents(
        LocalPlayerBlackHoleAssignment? assignment,
        BlackHoleMechanicState mechanicState,
        IReadOnlyList<PartyStatusEntry> entries)
    {
        ImGui.TextColored(ActiveWaveColor, "Your active Black Hole assignment will show in green for visibility.");
        ImGui.Separator();
        DrawMechanicState(assignment, mechanicState);
        ImGui.Separator();
        DrawLocalAssignment(assignment);
        ImGui.Separator();
        DrawPersonalBlackHoleInstructions(assignment, mechanicState);

        if (entries.Count > 0)
        {
            ImGui.Separator();
            DrawSection("Line order", entries.Where(entry => entry.Kind == StatusKind.LineOrder));
            DrawSection("Accretion", entries.Where(entry => entry.Kind == StatusKind.Accretion));
            DrawSection("Black Hole markers", entries.Where(entry => entry.Kind == StatusKind.BlackHole));
            DrawSection("Primordial Crust", entries.Where(entry => entry.Kind == StatusKind.PrimordialCrust));
            DrawSection("Earth resistance", entries.Where(entry => entry.Kind == StatusKind.EarthResistance));
        }
    }

    private void RandomizePreview()
    {
        SetPreviewRole(previewRandom.Next(PreviewRoles.Count));
    }

    private void SetPreviewRole(int roleIndex)
    {
        selectedPreviewRoleIndex = Math.Clamp(roleIndex, 0, PreviewRoles.Count - 1);
        var assignment = CreatePreviewAssignment(PreviewRoles[selectedPreviewRoleIndex]);
        var instructions = BlackHoleStrategy.GetInstructionsFor(assignment, plugin.Configuration.SelectedStrategy);
        var activeInstruction = instructions[previewRandom.Next(instructions.Count)];
        var activeWave = BlackHoleTimeline.Waves.First(wave => wave.Set == activeInstruction.Set && wave.Wave == activeInstruction.Wave);
        var activeWaveIndex = Enumerable.Range(0, BlackHoleTimeline.Waves.Count)
            .First(index => BlackHoleTimeline.Waves[index] == activeWave);

        previewAssignment = assignment;
        previewState = new BlackHoleMechanicState(
            IsActive: true,
            ElapsedSeconds: activeWave.StartsAtSeconds + 1.0f,
            CurrentWave: activeWave,
            NextWave: activeWaveIndex + 1 < BlackHoleTimeline.Waves.Count ? BlackHoleTimeline.Waves[activeWaveIndex + 1] : null,
            LastResolvedWave: activeWaveIndex > 0 ? BlackHoleTimeline.Waves[activeWaveIndex - 1] : null,
            EarthPulseCount: Math.Max(0, activeWaveIndex));
    }

    private void DrawPreviewControls()
    {
        if (ImGui.SmallButton("Randomize preview"))
        {
            RandomizePreview();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("Role");
        ImGui.SameLine();

        var selectedRole = PreviewRoles[selectedPreviewRoleIndex];
        DrawRoleMarker(selectedRole.Kind);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(170.0f * Math.Clamp(plugin.Configuration.HelperFontScale, 0.75f, 2.0f));
        if (!ImGui.BeginCombo("##PreviewRole", selectedRole.Label))
        {
            return;
        }

        for (var i = 0; i < PreviewRoles.Count; i++)
        {
            var role = PreviewRoles[i];
            ImGui.PushID(i);
            DrawRoleMarker(role.Kind);
            ImGui.SameLine();

            if (ImGui.Selectable(role.Label, selectedPreviewRoleIndex == i))
            {
                SetPreviewRole(i);
            }

            if (selectedPreviewRoleIndex == i)
            {
                ImGui.SetItemDefaultFocus();
            }

            ImGui.PopID();
        }

        ImGui.EndCombo();
    }

    private static void DrawRoleMarker(PreviewRoleKind roleKind)
    {
        var size = new Vector2(ImGui.GetTextLineHeight() * 0.85f);
        var cursor = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var min = cursor;
        var max = cursor + size;
        var rounding = size.X * 0.2f;

        switch (roleKind)
        {
            case PreviewRoleKind.Support:
                drawList.AddRectFilled(min, new Vector2(cursor.X + size.X * 0.5f, max.Y), ImGui.GetColorU32(SupportTankColor), rounding, ImDrawFlags.RoundCornersLeft);
                drawList.AddRectFilled(new Vector2(cursor.X + size.X * 0.5f, min.Y), max, ImGui.GetColorU32(SupportHealerColor), rounding, ImDrawFlags.RoundCornersRight);
                break;
            case PreviewRoleKind.Accretion:
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(AccretionColor), rounding);
                break;
            default:
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(DpsColor), rounding);
                break;
        }

        ImGui.Dummy(size);
        if (ImGui.IsItemHovered())
        {
            var tooltip = roleKind switch
            {
                PreviewRoleKind.Dps => "DPS role",
                PreviewRoleKind.Support => "Support role",
                PreviewRoleKind.Accretion => "Accretion role",
                _ => "Role",
            };
            ImGui.SetTooltip(tooltip);
        }
    }

    private static LocalPlayerBlackHoleAssignment CreatePreviewAssignment(PreviewRole role)
    {
        var lineStatusId = GetLineStatusId(role.LineGroup);
        return new LocalPlayerBlackHoleAssignment(
            "preview",
            "You",
            0,
            role.IsDps,
            role.HadAccretion,
            role.LineGroup,
            lineStatusId,
            GetLineName(lineStatusId),
            30.0f);
    }

    private void DrawStatusIcon(uint statusId, string tooltip)
    {
        var iconId = plugin.GetStatusIconId(statusId);
        if (iconId == 0)
        {
            ImGui.TextUnformatted(tooltip);
            return;
        }

        var iconScale = Math.Clamp(plugin.Configuration.HelperIconScale, 0.75f, 3.0f);
        var iconSize = new Vector2(ImGui.GetTextLineHeight() * 1.45f * iconScale);
        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        var wrap = texture.GetWrapOrDefault();
        if (wrap is null)
        {
            ImGui.TextUnformatted(tooltip);
            return;
        }

        ImGui.Image(wrap.Handle, iconSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private void DrawMechanicState(
        LocalPlayerBlackHoleAssignment? assignment,
        BlackHoleMechanicState mechanicState)
    {
        ImGui.TextUnformatted("Current Black Hole wave");
        if (!mechanicState.IsActive)
        {
            ImGui.TextDisabled("Waiting for Black Hole.");
            return;
        }

        if (mechanicState.CurrentWave is not null)
        {
            if (HasAssignmentForWave(assignment, mechanicState.CurrentWave))
            {
                ImGui.TextColored(ActiveWaveColor, mechanicState.CurrentWave.Label);
            }
            else
            {
                ImGui.TextUnformatted(mechanicState.CurrentWave.Label);
            }
        }
        else if (mechanicState.NextWave is not null)
        {
            ImGui.TextDisabled("Between Black Hole waves.");
            ImGui.TextUnformatted($"Next: {mechanicState.NextWave.Label}");
        }
        else
        {
            ImGui.TextDisabled("Waiting for Black Hole cleanup.");
        }

        if (mechanicState.LastResolvedWave is not null)
        {
            ImGui.TextDisabled($"Last resolved wave: {mechanicState.LastResolvedWave.Label}");
        }
    }

    private bool HasAssignmentForWave(LocalPlayerBlackHoleAssignment? assignment, BlackHoleWave wave)
    {
        return BlackHoleStrategy.GetInstructionsFor(assignment, plugin.Configuration.SelectedStrategy)
            .Any(instruction => instruction.IsForWave(wave));
    }

    private void DrawLocalAssignment(LocalPlayerBlackHoleAssignment? assignment)
    {
        ImGui.TextUnformatted("Your Black Hole assignment");
        if (assignment is null)
        {
            ImGui.TextDisabled("Local player not found.");
            return;
        }

        if (!assignment.HasLine)
        {
            ImGui.TextDisabled("Waiting for your line debuff.");
            return;
        }

        DrawStatusIcon(assignment.LineStatusId, assignment.LineName);
        if (assignment.HadAccretion)
        {
            ImGui.SameLine();
            DrawStatusIcon(1604, "Accretion");
        }

    }

    private void DrawPersonalBlackHoleInstructions(
        LocalPlayerBlackHoleAssignment? assignment,
        BlackHoleMechanicState mechanicState)
    {
        ImGui.TextUnformatted("Your Black Hole instructions");
        if (assignment is null || !assignment.HasLine)
        {
            ImGui.TextDisabled("Waiting for your assignment.");
            return;
        }

        var instructions = BlackHoleStrategy.GetInstructionsFor(assignment, plugin.Configuration.SelectedStrategy);
        if (instructions.Count == 0)
        {
            ImGui.TextDisabled("No tether assignment matched.");
            return;
        }

        if (!ImGui.BeginTable("##HelperPersonalBlackHoleInstructions", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Set");
        ImGui.TableSetupColumn("Wave");
        ImGui.TableSetupColumn("Tether");
        ImGui.TableSetupColumn("Action");
        ImGui.TableHeadersRow();

        foreach (var instruction in instructions)
        {
            var isActiveWave = mechanicState.CurrentWave is not null && instruction.IsForWave(mechanicState.CurrentWave);
            if (isActiveWave)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ActiveWaveColor);
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Set.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Wave.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Tether.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(instruction.Action);

            if (isActiveWave)
            {
                ImGui.PopStyleColor();
            }
        }

        ImGui.EndTable();
    }

    private static void DrawSection(string label, IEnumerable<PartyStatusEntry> entries)
    {
        var rows = entries.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        ImGui.TextUnformatted(label);
        if (ImGui.BeginTable($"##{label}", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("Player");
            ImGui.TableSetupColumn("Time");
            ImGui.TableHeadersRow();

            foreach (var row in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.StatusName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.MemberName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{MathF.Max(0.0f, row.RemainingTime):0.0}s");
            }

            ImGui.EndTable();
        }
    }

    private static uint GetLineStatusId(LineGroup lineGroup)
    {
        return lineGroup switch
        {
            LineGroup.First => 3004,
            LineGroup.Second => 3005,
            LineGroup.Third => 3006,
            _ => 0,
        };
    }

    private static string GetLineName(uint statusId)
    {
        return statusId switch
        {
            3004 => "First in Line",
            3005 => "Second in Line",
            3006 => "Third in Line",
            _ => "No line debuff",
        };
    }

    private sealed record PreviewRole(LineGroup LineGroup, bool IsDps, bool HadAccretion, string Label, PreviewRoleKind Kind);

    private enum PreviewRoleKind
    {
        Dps,
        Support,
        Accretion,
    }
}
