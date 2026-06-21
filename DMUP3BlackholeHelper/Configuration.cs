using Dalamud.Configuration;
using System;

namespace DMUP3BlackholeHelper;

public enum AssignmentChatChannel
{
    Say,
    Party,
    Alliance,
    FreeCompany,
    CrossWorldLinkshell1,
    CrossWorldLinkshell2,
    CrossWorldLinkshell3,
    CrossWorldLinkshell4,
    CrossWorldLinkshell5,
    CrossWorldLinkshell6,
    CrossWorldLinkshell7,
    CrossWorldLinkshell8,
}

public sealed record ChatChannelOption(AssignmentChatChannel Channel, string Label, string Command);

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool ShowHelper { get; set; } = true;

    public bool DebugChat { get; set; }

    public bool PostInstructionsToChat { get; set; }

    public int NowSoundEffectId { get; set; } = 1;

    public bool ShowPreviewWhenInactive { get; set; }

    public float HelperFontScale { get; set; } = 1.0f;

    public float HelperIconScale { get; set; } = 1.0f;

    public float HelperBackgroundOpacity { get; set; } = 1.0f;

    public AssignmentChatChannel AssignmentChatChannel { get; set; } = AssignmentChatChannel.Party;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
