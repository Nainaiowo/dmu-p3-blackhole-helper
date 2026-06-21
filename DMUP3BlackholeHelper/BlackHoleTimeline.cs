using System;
using System.Collections.Generic;
using System.Linq;

namespace DMUP3BlackholeHelper;

public static class BlackHoleTimeline
{
    public const float EndAtSeconds = 134.0f;

    public static readonly IReadOnlyList<BlackHoleWave> Waves =
    [
        new(1, 1, 0.0f, 6.2f, 4.2f),
        new(1, 2, 6.2f, 11.2f, 8.1f),

        new(2, 1, 51.9f, 58.4f, 55.9f),
        new(2, 2, 58.4f, 63.5f, 61.0f),
        new(2, 3, 63.5f, 69.1f, 66.1f),

        new(3, 1, 86.2f, 92.7f, 90.2f),
        new(3, 2, 92.7f, 97.8f, 95.2f),
        new(3, 3, 97.8f, 103.4f, 100.4f),

        new(4, 1, 119.6f, 127.2f, 123.6f),
        new(4, 2, 127.2f, 133.7f, 130.7f),
    ];

    public static BlackHoleWave? GetCurrentWave(float elapsedSeconds)
    {
        return Waves.FirstOrDefault(wave => elapsedSeconds >= wave.StartsAtSeconds && elapsedSeconds < wave.EndsAtSeconds);
    }

    public static BlackHoleWave? GetNextWave(float elapsedSeconds)
    {
        return Waves.FirstOrDefault(wave => elapsedSeconds < wave.StartsAtSeconds);
    }

    public static BlackHoleWave? GetWaveByPulseCount(int pulseCount)
    {
        if (pulseCount <= 0 || pulseCount > Waves.Count)
        {
            return null;
        }

        return Waves[pulseCount - 1];
    }

    public static BlackHoleWave? GetNearestWaveByMarkerTime(float elapsedSeconds, float toleranceSeconds = 3.0f)
    {
        return Waves
            .Select(wave => new
            {
                Wave = wave,
                Distance = MathF.Abs(elapsedSeconds - wave.MarkerAtSeconds),
            })
            .Where(entry => entry.Distance <= toleranceSeconds)
            .OrderBy(entry => entry.Distance)
            .Select(entry => entry.Wave)
            .FirstOrDefault();
    }
}

public sealed record BlackHoleWave(
    int Set,
    int Wave,
    float StartsAtSeconds,
    float EndsAtSeconds,
    float MarkerAtSeconds)
{
    public int Key => Set * 10 + Wave;

    public string Label => $"Black Hole Set {Set}, Wave {Wave}";
}

public sealed record BlackHoleMechanicState(
    bool IsActive,
    float ElapsedSeconds,
    BlackHoleWave? CurrentWave,
    BlackHoleWave? NextWave,
    BlackHoleWave? LastResolvedWave,
    int EarthPulseCount)
{
    public static BlackHoleMechanicState Inactive { get; } = new(false, 0.0f, null, null, null, 0);
}
