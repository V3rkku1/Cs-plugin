using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace LanSkinTester.Launcher.Core;

/// <summary>A router found with UPnP: where to send commands and which of our addresses it sees.</summary>
public record UpnpRouter(Uri ControlUrl, string ServiceType, IPAddress LocalAddress, string Name);

/// <summary>
/// Opens the server port on the home router with UPnP (IGD), so friends can join over the internet
/// without Radmin. Plain SSDP + SOAP, no Windows COM, so it behaves the same everywhere.
/// </summary>
public static class Upnp
{
    public const string Description = "LAN Skin Tester (CS2)";
    private const int LeaseSeconds = 7200;

    private static readonly string[] ServiceTypes =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    ];

    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>The mapping this launcher made (removed again on Stop / close).</summary>
    public static (UpnpRouter Router, int Port, DateTime Added)? Active { get; private set; }

    /// <summary>Finds the router, or null if none answers (UPnP off, or no router).</summary>
    public static async Task<UpnpRouter?> DiscoverAsync(TimeSpan? wait = null)
    {
        var locations = await SearchAsync(wait ?? TimeSpan.FromSeconds(3));
        foreach (var (location, local) in locations)
        {
            try
            {
                var router = await ReadDescriptionAsync(location, local);
                if (router != null) return router;
            }
            catch (Exception)
            {
                // Not an internet gateway, or it answered badly: try the next one.
            }
        }
        return null;
    }

    public static async Task<IPAddress?> ExternalAddressAsync(UpnpRouter router)
    {
        var response = await SoapAsync(router, "GetExternalIPAddress", []);
        return IPAddress.TryParse(Value(response, "NewExternalIPAddress"), out var ip) ? ip : null;
    }

    /// <summary>Forwards UDP <paramref name="port"/> on the router to this PC.</summary>
    public static async Task AddMappingAsync(UpnpRouter router, int port)
    {
        (string, string)[] Args(int lease) =>
        [
            ("NewRemoteHost", ""), ("NewExternalPort", port.ToString()), ("NewProtocol", "UDP"),
            ("NewInternalPort", port.ToString()), ("NewInternalClient", router.LocalAddress.ToString()),
            ("NewEnabled", "1"), ("NewPortMappingDescription", Description), ("NewLeaseDuration", lease.ToString()),
        ];

        try
        {
            await SoapAsync(router, "AddPortMapping", Args(LeaseSeconds));
        }
        catch (UpnpException ex) when (ex.Code is 725 or 402 or 501)
        {
            // Some routers only take permanent mappings (we remove it on Stop).
            await SoapAsync(router, "AddPortMapping", Args(0));
        }
        Active = (router, port, DateTime.UtcNow);
    }

    /// <summary>Re-adds the mapping before its lease runs out. Call now and then while hosting.</summary>
    public static async Task RenewIfNeededAsync()
    {
        if (Active is { } a && DateTime.UtcNow - a.Added > TimeSpan.FromSeconds(LeaseSeconds / 2))
            await AddMappingAsync(a.Router, a.Port);
    }

    public static async Task RemoveMappingAsync()
    {
        if (Active is not { } a) return;
        Active = null;
        await SoapAsync(a.Router, "DeletePortMapping",
            [("NewRemoteHost", ""), ("NewExternalPort", a.Port.ToString()), ("NewProtocol", "UDP")]);
    }

    /// <summary>Best effort, for Stop and closing the launcher.</summary>
    public static void RemoveMapping()
    {
        try
        {
            Task.Run(RemoveMappingAsync).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // The lease runs out by itself.
        }
    }

    /// <summary>
    /// The router's "internet" address is itself private: there's another router or the provider's
    /// shared address (CGNAT) in front, and friends can't reach it.
    /// </summary>
    public static bool IsBehindAnotherNat(IPAddress external)
    {
        var b = external.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] is >= 16 and < 32)
               || (b[0] == 100 && b[1] is >= 64 and < 128) || (b[0] == 169 && b[1] == 254);
    }

    // ---------------------------------------------------------------------------------------------
    // SSDP
    // ---------------------------------------------------------------------------------------------

    private static async Task<List<(Uri Location, IPAddress Local)>> SearchAsync(TimeSpan wait)
    {
        var found = new List<(Uri, IPAddress)>();
        var sockets = new List<(UdpClient Udp, IPAddress Local)>();
        try
        {
            foreach (var (local, gateways) in Network.LanAddresses())
            {
                UdpClient udp;
                try { udp = new UdpClient(new IPEndPoint(local, 0)); }
                catch (SocketException) { continue; }
                sockets.Add((udp, local));

                foreach (var st in new[] { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "upnp:rootdevice" })
                {
                    var request = Encoding.ASCII.GetBytes(
                        "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                        $"ST: {st}\r\n\r\n");
                    // Multicast, plus straight to the gateway (some routers only answer that).
                    await TrySend(udp, request, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));
                    foreach (var gateway in gateways) await TrySend(udp, request, new IPEndPoint(gateway, 1900));
                }
            }

            var deadline = DateTime.UtcNow + wait;
            var receives = sockets.ToDictionary(s => s, s => s.Udp.ReceiveAsync());
            while (receives.Count > 0)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) break;
                var done = await Task.WhenAny(receives.Values.Append(Task.Delay(left)));
                var entry = receives.FirstOrDefault(r => r.Value == done);
                if (entry.Value == null) break; // timed out

                try
                {
                    var text = Encoding.ASCII.GetString(entry.Value.Result.Buffer);
                    var location = Header(text, "LOCATION");
                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && found.All(f => f.Item1 != uri))
                        found.Add((uri, entry.Key.Local));
                    receives[entry.Key] = entry.Key.Udp.ReceiveAsync();
                }
                catch (Exception)
                {
                    receives.Remove(entry.Key);
                }
            }
        }
        finally
        {
            foreach (var (udp, _) in sockets) udp.Dispose();
        }
        // Gateways (the answers that came from the router itself) are usually listed first anyway.
        return found;
    }

    private static async Task TrySend(UdpClient udp, byte[] data, IPEndPoint to)
    {
        try { await udp.SendAsync(data, data.Length, to); }
        catch (SocketException) { /* no route on this adapter */ }
    }

    private static string? Header(string response, string name)
    {
        foreach (var line in response.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line[(colon + 1)..].Trim();
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Device description + SOAP
    // ---------------------------------------------------------------------------------------------

    private static async Task<UpnpRouter?> ReadDescriptionAsync(Uri location, IPAddress local)
    {
        var xml = XDocument.Parse(await Http.GetStringAsync(location));
        var baseText = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
        var baseUri = Uri.TryCreate(baseText, UriKind.Absolute, out var b) ? b : location;
        var name = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "friendlyName")?.Value.Trim() ?? location.Host;

        var services = xml.Descendants().Where(e => e.Name.LocalName == "service").ToList();
        foreach (var type in ServiceTypes)
        {
            var service = services.FirstOrDefault(s => Child(s, "serviceType") == type);
            var control = service == null ? null : Child(service, "controlURL");
            if (control != null && Uri.TryCreate(baseUri, control, out var url))
                return new UpnpRouter(url, type, local, name);
        }
        return null;
    }

    private static string? Child(XElement e, string name) =>
        e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value.Trim();

    private static async Task<XDocument> SoapAsync(UpnpRouter router, string action, (string Name, string Value)[] args)
    {
        var body = new StringBuilder();
        body.Append("<?xml version=\"1.0\"?>")
            .Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">")
            .Append($"<s:Body><u:{action} xmlns:u=\"{router.ServiceType}\">");
        foreach (var (name, value) in args) body.Append($"<{name}>{SecurityElement.Escape(value)}</{name}>");
        body.Append($"</u:{action}></s:Body></s:Envelope>");

        using var request = new HttpRequestMessage(HttpMethod.Post, router.ControlUrl)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{router.ServiceType}#{action}\"");

        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        XDocument doc;
        try { doc = XDocument.Parse(text); }
        catch (System.Xml.XmlException) { throw new UpnpException(0, $"{action}: HTTP {(int)response.StatusCode}"); }

        if (!response.IsSuccessStatusCode)
        {
            int.TryParse(Value(doc, "errorCode"), out var code);
            throw new UpnpException(code, $"{action}: {Value(doc, "errorDescription") ?? $"HTTP {(int)response.StatusCode}"} ({code})");
        }
        return doc;
    }

    private static string? Value(XDocument doc, string name) =>
        doc.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
}

public class UpnpException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    /// <summary>Plain-language explanation for the common router errors.</summary>
    public string Friendly => Code switch
    {
        718 => "Port 27015 is already forwarded to another PC on your network.",
        606 or 401 => "The router refused (UPnP may be read-only in its settings).",
        _ => Message,
    };
}
