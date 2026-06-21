using System.Collections.Generic;
using System.Linq;

namespace DMUP3BlackholeHelper;

public static class BlackHoleStrategy
{
    public static readonly IReadOnlyList<BlackHoleInstruction> Instructions =
    [
        new(1, 1, 1, Role(LineGroup.First, BlackHoleRoleKind.Dps), "Grab the first and only tether clockwise from Kefka."),
        new(1, 2, 1, Role(LineGroup.First, BlackHoleRoleKind.Dps), "Grab the new tether clockwise from Kefka."),
        new(1, 2, 2, Role(LineGroup.First, BlackHoleRoleKind.Support), "Grab the second and last tether clockwise from Kefka."),

        new(2, 1, 1, Role(LineGroup.First, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(2, 1, 2, Role(LineGroup.First, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(2, 1, 3, Accretion(LineGroup.First), "Grab the third tether clockwise from Kefka."),
        new(2, 2, 1, Role(LineGroup.Second, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(2, 2, 2, Role(LineGroup.First, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(2, 2, 3, Accretion(LineGroup.First), "Grab the third tether clockwise from Kefka."),
        new(2, 3, 1, Role(LineGroup.Second, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(2, 3, 2, Role(LineGroup.Second, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(2, 3, 3, Accretion(LineGroup.First), "Grab the third tether clockwise from Kefka."),

        new(3, 1, 1, Role(LineGroup.Second, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(3, 1, 2, Role(LineGroup.Second, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(3, 1, 3, Accretion(LineGroup.Second), "Grab the third tether clockwise from Kefka."),
        new(3, 2, 1, Role(LineGroup.Third, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(3, 2, 2, Role(LineGroup.Second, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(3, 2, 3, Accretion(LineGroup.Second), "Grab the third tether clockwise from Kefka."),
        new(3, 3, 1, Role(LineGroup.Third, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(3, 3, 2, Role(LineGroup.Third, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(3, 3, 3, Accretion(LineGroup.Second), "Grab the third tether clockwise from Kefka."),

        new(4, 1, 1, Role(LineGroup.Third, BlackHoleRoleKind.Dps), "Grab the first tether clockwise from Kefka."),
        new(4, 1, 2, Role(LineGroup.Third, BlackHoleRoleKind.Support), "Grab the second tether clockwise from Kefka."),
        new(4, 2, 1, Role(LineGroup.Third, BlackHoleRoleKind.Support), "Grab the only tether clockwise from Kefka."),
    ];

    public static IReadOnlyList<BlackHoleInstruction> GetInstructionsFor(LocalPlayerBlackHoleAssignment? assignment)
    {
        if (assignment is null || !assignment.HasLine)
        {
            return [];
        }

        return Instructions
            .Where(instruction => instruction.Role.Matches(assignment))
            .OrderBy(instruction => instruction.Set)
            .ThenBy(instruction => instruction.Wave)
            .ThenBy(instruction => instruction.Tether)
            .ToList();
    }

    private static BlackHoleAssignmentRole Role(LineGroup lineGroup, BlackHoleRoleKind roleKind)
    {
        return new BlackHoleAssignmentRole(lineGroup, roleKind, RequiresAccretion: false);
    }

    private static BlackHoleAssignmentRole Accretion(LineGroup lineGroup)
    {
        return new BlackHoleAssignmentRole(lineGroup, BlackHoleRoleKind.Accretion, RequiresAccretion: true);
    }
}

public enum BlackHoleRoleKind
{
    Dps,
    Support,
    Accretion,
}

public sealed record BlackHoleAssignmentRole(LineGroup LineGroup, BlackHoleRoleKind RoleKind, bool RequiresAccretion)
{
    public string DisplayName => $"{LineShortName} {RoleText}";

    public string LineShortName => LineGroup switch
    {
        LineGroup.First => "FIL",
        LineGroup.Second => "SIL",
        LineGroup.Third => "TIL",
        _ => "No line",
    };

    private string RoleText => RoleKind switch
    {
        BlackHoleRoleKind.Dps => "DPS",
        BlackHoleRoleKind.Support => "Support",
        BlackHoleRoleKind.Accretion => "Accretion",
        _ => "Unknown",
    };

    public bool Matches(LocalPlayerBlackHoleAssignment assignment)
    {
        if (!assignment.HasLine || assignment.LineGroup != LineGroup)
        {
            return false;
        }

        if (RequiresAccretion)
        {
            return assignment.HadAccretion;
        }

        if (assignment.HadAccretion)
        {
            return false;
        }

        return RoleKind switch
        {
            BlackHoleRoleKind.Dps => assignment.IsDps,
            BlackHoleRoleKind.Support => !assignment.IsDps,
            _ => false,
        };
    }
}

public sealed record BlackHoleInstruction(
    int Set,
    int Wave,
    int Tether,
    BlackHoleAssignmentRole Role,
    string Action)
{
    public bool IsForWave(BlackHoleWave wave)
    {
        return Set == wave.Set && Wave == wave.Wave;
    }
}
