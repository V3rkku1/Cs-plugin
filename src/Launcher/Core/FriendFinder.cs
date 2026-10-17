using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>A friend hosting on the same network (LAN or Radmin), heard from their launcher.</summary>
public record LanHost(string Name, string Address, string Map, int Players, int MaxPlayers, bool Password, DateTime Seen);

/// <summary>What a server says about itself (Steam's A2S_INFO query).</summary>
public record ServerInfo(string Name, string Map, int Players, int MaxPlayers, int Bots, bool Password);

/// <summary>
/// Friends list helpers. While hosting, the launcher announces the server on every local network
/// (LAN and Radmin, UDP broadcast); other launchers hear it and show "hosting now". Saved friends are
/// checked with Steam's standard server query, which works over the internet too.
/// </summary>
public static class FriendFinder
{
    public const int BeaconPort = 27098;
    private const string Magic = "LSTHOST1";
    private static readonly Dictionary<string, LanHost> Hosts = new();
    private static Thread? _listener;
    private static System.Threading.Timer? _beacon;

    private record Beacon(string Name, int Port, string Map, int Players, int Max, bool Password);

    /// <summary>Friends hosting on this PC's networks, heard in the last 10 seconds.</summary>
    public static List<LanHost> LanHosts()
    {
        var fresh = DateTime.UtcNow - TimeSpan.FromSeconds(10);
        lock (Hosts) return Hosts.Values.Where(h => h.Seen > fresh).OrderBy(h => h.Name).ToList();
    }

    public static void StartListening(Action<string> log)
    {
        if (_listener != null) return;
        _listener = new Thread(() => Listen(log)) { IsBackground = true, Name = "FriendFinder" };
        _listener.Start();
    }

    private static void Listen(Action<string> log)
    {
        UdpClient udp;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));
        }
        catch (SocketException ex)
        {
            log($"Friends list: can't listen for friends hosting on your network ({ex.Message}).");
            return;
        }
        var own = Network.OwnAddresses();
        while (true)
        {
            try
            {
                IPEndPoint? from = null;
                var text = Encoding.UTF8.GetString(udp.Receive(ref from));
                if (own.Contains(from.Address.ToString()) || !text.StartsWith(Magic)) continue; // our own announcement, or noise
                if (JsonSerializer.Deserialize<Beacon>(text[Magic.Length..]) is not { } b) continue;
                var address = $"{from.Address}:{b.Port}";
                lock (Hosts) Hosts[address] = new LanHost(Clean(b.Name), address, Clean(b.Map), b.Players, b.Max, b.Password, DateTime.UtcNow);
            }
            catch (Exception)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Announces this server every 3 seconds while <paramref name="current"/> returns one.</summary>
    public static void StartBeacon(Func<(string Name, string Map, int Players, int Max, bool Password)?> current)
    {
        if (_beacon != null) return;
        var udp = new UdpClient { EnableBroadcast = true };
        _beacon = new System.Threading.Timer(_ =>
        {
            try
            {
                if (current() is not { } s) return;
                var bytes = Encoding.UTF8.GetBytes(Magic + JsonSerializer.Serialize(new Beacon(s.Name, ServerSettings.Port, s.Map, s.Players, s.Max, s.Password)));
                foreach (var target in Network.BroadcastAddresses())
                {
                    try { udp.Send(bytes, bytes.Length, new IPEndPoint(target, BeaconPort)); }
                    catch (SocketException) { /* that network is down */ }
                }
            }
            catch (Exception) { /* next time */ }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    private static string Clean(string? text) => new((text ?? "").Where(c => !char.IsControl(c)).Take(40).ToArray());

    /// <summary>Asks a server for its name, map and players (A2S_INFO). Null when it doesn't answer.</summary>
    public static async Task<ServerInfo?> QueryAsync(string address, int timeoutMs = 1500)
    {
        var parts = address.Split(':');
        if (!int.TryParse(parts.Length > 1 ? parts[1] : ServerSettings.Port.ToString(), out var port)) return null;
        try
        {
            var ip = IPAddress.TryParse(parts[0], out var parsed) ? parsed
                : (await Dns.GetHostAddressesAsync(parts[0])).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip == null) return null;
            using var udp = new UdpClient();
            udp.Connect(ip, port);
            var request = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
            request.AddRange(Encoding.ASCII.GetBytes("Source Engine Query\0"));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await udp.SendAsync(request.ToArray());
                using var cts = new CancellationTokenSource(timeoutMs);
                var reply = (await udp.ReceiveAsync(cts.Token)).Buffer;
                if (reply.Length < 5) return null;
                if (reply[4] == 0x41 && reply.Length >= 9) // challenge: send again with it
                {
                    request.AddRange(reply.Skip(5).Take(4));
                    continue;
                }
                return reply[4] == 0x49 ? ParseInfo(reply) : null;
            }
        }
        catch (Exception) { /* offline, or not a game server */ }
        return null;
    }

    /// <summary>The A2S_INFO reply: header, protocol, then name, map, folder and game strings, app id and counts.</summary>
    private static ServerInfo? ParseInfo(byte[] reply)
    {
        var i = 6;
        string Str()
        {
            var start = i;
            while (i < reply.Length && reply[i] != 0) i++;
            return Encoding.UTF8.GetString(reply, start, i++ - start);
        }
        var name = Str();
        var map = Str();
        Str(); // folder
        Str(); // game
        i += 2; // app id
        if (i + 7 > reply.Length) return null;
        int players = reply[i], max = reply[i + 1], bots = reply[i + 2];
        return new ServerInfo(name, map, Math.Max(0, players - bots), max, bots, reply[i + 5] == 1);
    }
}
