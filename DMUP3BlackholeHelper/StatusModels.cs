namespace DMUP3BlackholeHelper;

using System;
using System.Collections.Generic;

public enum StatusKind
{
    LineOrder,
    Accretion,
    EarthResistance,
    PrimordialCrust,
    BlackHole,
}

public enum LineGroup
{
    None,
    First,
    Second,
    Third,
}

public sealed record WatchedStatus(uint Id, string Name, StatusKind Kind, int SortOrder);

public sealed record SoundEffectOption(int Id, string Label);

public sealed record PartyMemberSnapshot(
    string MemberKey,
    string MemberName,
    int PartyIndex,
    bool IsDps,
    ulong ContentId,
    uint EntityId);

public sealed record PartyStatusEntry(
    string Key,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    bool IsDps,
    uint StatusId,
    string StatusName,
    StatusKind Kind,
    float RemainingTime,
    int SortOrder);

public sealed record BlackHoleResolutionHit(
    string MemberKey,
    string MemberName,
    int PartyIndex,
    string ActualRole,
    int HitOrder,
    bool WasExpected);

public sealed record BlackHoleResolutionRecord(
    DateTime SeenAtUtc,
    BlackHoleWave Wave,
    uint ActionId,
    string GlobalSequence,
    string ActionName,
    IReadOnlyList<BlackHoleResolutionHit> Hits);

public sealed record PartyDeathRecord(
    DateTime SeenAtUtc,
    float CombatElapsedSeconds,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint ActionId,
    string ActionName,
    string GlobalSequence,
    BlackHoleWave? Wave);

public sealed record BlackHolePullSnapshot(
    DateTime CapturedAtUtc,
    string Reason,
    float CombatElapsedSeconds,
    IReadOnlyList<PartyStatusEntry> Entries,
    IReadOnlyList<PartyMemberSnapshot> Members,
    IReadOnlyList<LocalPlayerBlackHoleAssignment> Assignments,
    IReadOnlyList<BlackHoleResolutionRecord> Resolutions,
    BlackHoleMechanicState MechanicState,
    IReadOnlyList<PartyDeathRecord> Deaths);

public sealed record LocalPlayerBlackHoleAssignment(
    string MemberKey,
    string MemberName,
    int PartyIndex,
    bool IsDps,
    bool HadAccretion,
    LineGroup LineGroup,
    uint LineStatusId,
    string LineName,
    float LineRemainingTime)
{
    public bool HasLine => LineGroup != LineGroup.None;

    public string RoleName => HadAccretion
        ? $"{LineShortName} Accretion"
        : $"{LineShortName} {(IsDps ? "DPS" : "Support")}";

    public string FullRoleName => HadAccretion
        ? $"{LineName} Accretion"
        : $"{LineName} {(IsDps ? "DPS" : "Support")}";

    public string LineShortName => LineGroup switch
    {
        LineGroup.First => "FIL",
        LineGroup.Second => "SIL",
        LineGroup.Third => "TIL",
        _ => "No line",
    };
}
