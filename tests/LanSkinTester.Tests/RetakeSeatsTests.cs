using LanSkinTester.Launcher.Core;
using Seats = global::LanSkinTester.LanSkinTester;

namespace LanSkinTester.Tests;

/// <summary>
/// People joining plugin retakes. <see cref="RetakesServer"/> copies what B3none's cs2-retakes does with
/// players (Managers/QueueManager.cs, Events/PlayerEventHandlers.cs) and what CS2 does with bot_quota; the
/// seat rule is the plugin's own (RetakeSeats.cs).
/// </summary>
public class RetakeSeatsTests
{
    private sealed class Player(string name, bool bot)
    {
        public string Name { get; } = name;
        public bool Bot { get; } = bot;
        public override string ToString() => Name;
    }

    private sealed class RetakesServer
    {
        private readonly int _maxPlayers;
        private readonly int _botsWanted;
        private readonly bool _seatRule;
        private int _quota;
        private int _botNumber;
        private bool _pending;

        public List<Player> Players { get; } = [];
        public HashSet<Player> Active { get; } = [];
        public HashSet<Player> Queue { get; } = [];
        public bool Warmup { get; private set; } = true;
        public bool RoundRunning { get; private set; }

        public RetakesServer(int maxPlayers, int botsWanted, bool seatRule)
        {
            _maxPlayers = maxPlayers;
            _botsWanted = botsWanted;
            _seatRule = seatRule;
            // The launcher's cfg: one seat left for you, then lst_retake_seats applies at once.
            _quota = Math.Min(botsWanted, Math.Max(0, maxPlayers - 1));
            UpdateSeats(force: true);
            MaintainBotQuota();
            Spawn();
        }

        private int Humans => Players.Count(p => !p.Bot);

        public bool Plays(Player p) => Active.Contains(p);

        // CS2: the bot manager adds or kicks bots until the quota matches.
        private void MaintainBotQuota()
        {
            while (Players.Count(p => p.Bot) < _quota) Players.Add(new Player($"bot{++_botNumber}", bot: true));
            while (Players.Count(p => p.Bot) > _quota) Disconnect(Players.Last(p => p.Bot));
        }

        // cs2-retakes OnPlayerSpawn: a bot that isn't active is force-added (no seat check).
        private void Spawn()
        {
            foreach (var bot in Players.Where(p => p.Bot && !Active.Contains(p))) Active.Add(bot);
        }

        // RetakeSeats.UpdateSeats.
        private void UpdateSeats(bool force = false)
        {
            if (!_seatRule) return;
            if (!force && Seats.SeatChangeWaits(live: false, RoundRunning, Warmup))
            {
                _pending = true;
                return;
            }
            _pending = false;
            _quota = Seats.SeatQuota(_maxPlayers, _botsWanted, Humans);
            MaintainBotQuota();
        }

        public Player Connect(string name)
        {
            var player = new Player(name, bot: false);
            Players.Add(player);
            UpdateSeats(); // player_connect_full (+0.1 s)
            // cs2-retakes ShouldAutoJoinGame (+1 s): AddConnectingPlayer -> PlayerJoinedTeam(None -> CT).
            if (Warmup && Active.Count < _maxPlayers) Active.Add(player);
            else Queue.Add(player);
            return player;
        }

        public void Disconnect(Player player)
        {
            Players.Remove(player);
            Active.Remove(player); // RemovePlayerFromQueues
            Queue.Remove(player);
            if (!player.Bot) UpdateSeats();
        }

        public void EndWarmup() => NextRound();

        public void RoundStart()
        {
            RoundRunning = true;
        }

        public void RoundEnd()
        {
            RoundRunning = false;
            if (_pending) UpdateSeats();
        }

        /// <summary>Round pre-start: QueueManager.Update fills free seats from the queue; then everyone spawns.</summary>
        public void NextRound()
        {
            Warmup = false;
            var free = _maxPlayers - Active.Count;
            foreach (var p in Queue.Take(Math.Max(0, free)).ToList())
            {
                Queue.Remove(p);
                Active.Add(p);
            }
            MaintainBotQuota();
            Spawn();
            RoundStart();
        }

        public void PlayRound()
        {
            RoundEnd();
            NextRound();
        }
    }

    [Fact]
    public void TwoFriendsInWarmupBothPlay()
    {
        var server = new RetakesServer(maxPlayers: 9, botsWanted: 8, seatRule: true);
        var you = server.Connect("you");
        var friend = server.Connect("friend");
        Assert.True(server.Plays(you));
        Assert.True(server.Plays(friend));
        server.EndWarmup();
        Assert.True(server.Plays(you) && server.Plays(friend));
        Assert.Equal(9, server.Active.Count);
        Assert.Equal(7, server.Players.Count(p => p.Bot));
    }

    [Fact]
    public void WithoutTheSeatRuleTheSecondFriendWaitsForever()
    {
        // The bug: bots hold every seat and cs2-retakes never lets one go for a queued person.
        var server = new RetakesServer(maxPlayers: 9, botsWanted: 8, seatRule: false);
        server.Connect("you");
        var friend = server.Connect("friend");
        server.EndWarmup();
        for (var round = 0; round < 10; round++) server.PlayRound();
        Assert.False(server.Plays(friend));
        Assert.Contains(friend, server.Queue);
    }

    [Fact]
    public void FriendJoiningMidRoundPlaysFromTheNextRound()
    {
        var server = new RetakesServer(maxPlayers: 9, botsWanted: 8, seatRule: true);
        var you = server.Connect("you");
        server.EndWarmup();
        var friend = server.Connect("friend");
        // Mid-round: no bot is kicked, the friend waits in the queue.
        Assert.False(server.Plays(friend));
        Assert.Equal(8, server.Players.Count(p => p.Bot));
        server.PlayRound();
        Assert.True(server.Plays(you));
        Assert.True(server.Plays(friend));
        Assert.Equal(7, server.Players.Count(p => p.Bot));
    }

    [Fact]
    public void AFullServerOfFriendsHasNoBots()
    {
        var server = new RetakesServer(maxPlayers: 9, botsWanted: 8, seatRule: true);
        server.EndWarmup();
        var people = Enumerable.Range(1, 9).Select(i => server.Connect($"p{i}")).ToList();
        server.PlayRound();
        Assert.All(people, p => Assert.True(server.Plays(p), $"{p} has no seat"));
        Assert.DoesNotContain(server.Players, p => p.Bot);
        // A tenth person waits: there are only 9 seats.
        var tenth = server.Connect("p10");
        server.PlayRound();
        Assert.False(server.Plays(tenth));
    }

    [Fact]
    public void ABotComesBackWhenAFriendLeaves()
    {
        var server = new RetakesServer(maxPlayers: 9, botsWanted: 8, seatRule: true);
        var you = server.Connect("you");
        var friend = server.Connect("friend");
        server.EndWarmup();
        server.Disconnect(friend);
        Assert.Equal(7, server.Players.Count(p => p.Bot)); // not during the round
        server.PlayRound();
        Assert.Equal(8, server.Players.Count(p => p.Bot));
        Assert.True(server.Plays(you));
        Assert.Equal(9, server.Active.Count);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(5, 4)]
    [InlineData(10, 9)]
    [InlineData(9, 20)]
    public void EveryoneUpToTheSeatCountPlays(int maxPlayers, int bots)
    {
        var server = new RetakesServer(maxPlayers, bots, seatRule: true);
        var people = new List<Player>();
        server.EndWarmup();
        for (var i = 1; i <= maxPlayers; i++)
        {
            people.Add(server.Connect($"p{i}"));
            server.PlayRound();
            Assert.All(people, p => Assert.True(server.Plays(p), $"{p} has no seat after {i} joined"));
            Assert.True(server.Active.Count <= maxPlayers, "more players than seats");
        }
    }

    [Fact]
    public void QuotaNeverTakesAPersonsSeat()
    {
        for (var seats = 0; seats <= 12; seats++)
        for (var bots = 0; bots <= 31; bots++)
        for (var humans = 0; humans <= 14; humans++)
        {
            var quota = Seats.SeatQuota(seats, bots, humans);
            Assert.InRange(quota, 0, Math.Min(bots, 31));
            if (humans <= seats) Assert.Equal(Math.Min(bots, seats - humans), quota);
            else Assert.Equal(0, quota);
        }
    }

    [Theory]
    [InlineData(false, true, false, true)]  // plugin retakes, live round: wait for the round end
    [InlineData(false, true, true, false)]  // warmup: now
    [InlineData(false, false, false, false)] // between rounds: now
    [InlineData(true, true, false, false)]  // Valve retakes: always now
    public void PluginRetakesChangesBotsOnlyBetweenRounds(bool live, bool roundRunning, bool warmup, bool waits) =>
        Assert.Equal(waits, Seats.SeatChangeWaits(live, roundRunning, warmup));

    [Fact]
    public void LauncherAndPluginAgreeOnTheFirstQuota()
    {
        // The cfg's bot_quota is what the plugin computes for one person (you).
        foreach (var maxPlayers in Enumerable.Range(2, 9))
        foreach (var bots in new[] { 0, 1, 6, 8, 20 })
        {
            var s = new ServerSettings { Mode = ServerSettings.RetakesMode, RetakeStyle = 1, RetakeMaxPlayers = maxPlayers, Bots = bots };
            Assert.Contains($"bot_quota {Seats.SeatQuota(maxPlayers, Math.Min(bots, 31), 1)}\n", s.ToCfg().Replace("\r", ""));
        }
    }

    [Theory]
    [InlineData(3, 3, 3, 4, true, Seats.RoomMove.MoveBot)]   // T full of bots, CT has a free seat: a T bot goes to CT
    [InlineData(3, 3, 4, 4, true, Seats.RoomMove.KickBot)]   // both full: a T bot leaves, bot_quota re-adds one where there is room
    [InlineData(2, 3, 4, 4, true, Seats.RoomMove.None)]      // a free seat already
    [InlineData(3, 3, 3, 4, false, Seats.RoomMove.None)]     // full of people: nothing to make room with
    public void ValveRetakesMakesRoomForPeople(int onWanted, int wantedSeats, int onOther, int otherSeats, bool bot, Seats.RoomMove expected) =>
        Assert.Equal(expected, Seats.MakeRoom(onWanted, wantedSeats, onOther, otherSeats, bot));
}

public class OwnSkinsTests
{
    [Fact]
    public void LoadoutSurvivesTheTripThroughConsoleChunks()
    {
        var loadout = new PlayerLoadout { Knife = 507, Gloves = 5030, Pin = 6101 };
        loadout.Skins[7] = new SkinSettings { Paint = 44, Wear = 0.0712f, Seed = 661, StatTrak = true,
            Stickers = [new StickerSettings { Slot = 2, Id = 4321, OffsetX = 0.25f, OffsetY = -0.1f }] };
        for (ushort d = 1; d < 40; d++) loadout.SkinsT[d] = new SkinSettings { Paint = 300 + d, Nametag = "long name tag " + d };

        var chunks = LoadoutCodec.Encode(loadout);
        Assert.True(chunks.Count > 1);
        // Only characters a console argument keeps as they are.
        Assert.All(chunks, c => Assert.Matches("^[A-Za-z0-9_=-]{1,200}$", c));
        var back = LoadoutCodec.Decode(chunks)!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(loadout), System.Text.Json.JsonSerializer.Serialize(back));
        Assert.Null(LoadoutCodec.Decode(["not a loadout!"]));
    }

    [Theory]
    [InlineData("{\"Skins\":null}")]
    [InlineData("{\"Skins\":{\"7\":null}}")]
    [InlineData("{\"Skins\":{\"7\":{\"Paint\":12,\"Stickers\":[null]}}}")]
    [InlineData("{\"SkinsT\":{\"7\":{\"Paint\":12,\"Wear\":5}}}")]
    public void BrokenLoadoutsFromAnotherPcAreRefused(string json)
    {
        var text = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).Replace('+', '-').Replace('/', '_');
        Assert.Null(LoadoutCodec.Decode([text]));
    }
}
