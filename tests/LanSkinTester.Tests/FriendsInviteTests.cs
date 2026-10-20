using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Tests;

public class FriendsInviteTests
{
    [Fact]
    public void InviteLineDropsTheClosingBracket() =>
        Assert.Equal(new JoinTarget("1.2.3.4:27015", "abc"), FriendsInvite.Parse("(or type the address 1.2.3.4 and password abc)", ""));

    [Fact]
    public void ConnectLineKeepsThePassword() =>
        Assert.Equal(new JoinTarget("1.2.3.4:27016", "x)y"), FriendsInvite.Parse("connect 1.2.3.4:27016; password x)y", ""));

    [Fact]
    public void CodeRoundTrips() =>
        Assert.Equal(new JoinTarget("5.6.7.8:27015", "pw"), FriendsInvite.Parse(FriendsInvite.Code("5.6.7.8", "pw"), ""));
}
