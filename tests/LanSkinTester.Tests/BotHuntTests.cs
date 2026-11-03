using static LanSkinTester.LanSkinTester;

namespace LanSkinTester.Tests;

/// <summary>The bot aimbot's math: where to look, how far to turn, and who can see whom.</summary>
public class BotHuntTests
{
    [Fact]
    public void AimsStraightAtATargetOnEachSide()
    {
        Assert.Equal((0f, 0f), AimAngles((0, 0, 64), (100, 0, 64)));
        Assert.Equal(90f, AimAngles((0, 0, 0), (0, 50, 0)).Yaw, 3);
        Assert.Equal(180f, MathF.Abs(AimAngles((0, 0, 0), (-50, 0, 0)).Yaw), 3);
        // Up is a negative pitch in CS2, down positive.
        Assert.Equal(-45f, AimAngles((0, 0, 0), (100, 0, 100)).Pitch, 3);
        Assert.Equal(45f, AimAngles((0, 0, 100), (100, 0, 0)).Pitch, 3);
    }

    [Fact]
    public void TurnsPartWayTheShortWayRound()
    {
        Assert.Equal(90f, Approach(0, 90, 1), 3);       // 100 = snap
        Assert.Equal(45f, Approach(0, 90, 0.5f), 3);
        Assert.Equal(0f, Approach(0, 90, 0), 3);
        // 170 to -170 is 20 degrees across the back, not 340 the long way.
        Assert.Equal(190f, Approach(170, -170, 1), 3);
        Assert.Equal(180f, Approach(170, -170, 0.5f), 3);
    }

    [Fact]
    public void SpottedMaskHasABitPerSlot()
    {
        uint[] mask = [0b101, 1u << 3];
        Assert.True(SeenBy(mask, 0));
        Assert.False(SeenBy(mask, 1));
        Assert.True(SeenBy(mask, 2));
        Assert.True(SeenBy(mask, 35));
        Assert.False(SeenBy(mask, 64));
        Assert.False(SeenBy(mask, -1));
    }
}
