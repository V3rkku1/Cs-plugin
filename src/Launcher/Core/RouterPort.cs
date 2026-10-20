using System.Net;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// The router found with UPnP and the server port opened on it, shared by every way of starting the
/// server (Play page, F5, quick starts and the Friends page), so "Internet" friends mode works the same
/// whichever button starts it.
/// </summary>
public static class RouterPort
{
    public static UpnpRouter? Router { get; private set; }
    public static IPAddress? External { get; private set; }

    /// <summary>A search has finished (whether or not a router answered).</summary>
    public static bool Checked { get; private set; }
    public static bool Searching { get; private set; }

    /// <summary>Search started or finished, or the port was opened / closed.</summary>
    public static event Action? Changed;

    /// <summary>Look for the router again next time (e.g. the user switched to Internet mode).</summary>
    public static void ForgetSearch() => Checked = false;

    private static Task? _search;

    /// <summary>Looks for the router; while a search runs, callers wait for that one instead of seeing no router yet.</summary>
    public static Task FindAsync(Action<string> log) => Searching && _search != null ? _search : _search = SearchAsync(log);

    private static async Task SearchAsync(Action<string> log)
    {
        Searching = true;
        Changed?.Invoke();
        try
        {
            Router = await Task.Run(() => Upnp.DiscoverAsync());
            External = Router == null ? null : await Upnp.ExternalAddressAsync(Router);
        }
        catch (Exception ex)
        {
            log("UPnP: " + (ex is UpnpException u ? u.Friendly : ex.Message));
        }
        Searching = false;
        Checked = true;
        if (Router != null) log($"UPnP router: {Router.Name}, internet address {External?.ToString() ?? "unknown"}.");
        Changed?.Invoke();
    }

    /// <summary>Opens the server port on the router. Returns an error message, or null when it's open.</summary>
    public static async Task<string?> OpenAsync(Action<string> log)
    {
        if (Upnp.Active != null) return null; // already open
        if (Router == null || External == null) await FindAsync(log);
        if (Router == null) return "No UPnP router answered. Turn on UPnP in your router's settings, or use Radmin.";
        if (External != null && Upnp.IsBehindAnotherNat(External))
            return $"Your router's internet address ({External}) is a shared/private one (CGNAT or a second router), so friends can't reach it. Use Radmin instead.";
        try
        {
            await Upnp.AddMappingAsync(Router, ServerSettings.Port);
            log($"UPnP: opened UDP port {ServerSettings.Port} on {Router.Name}. It closes again when you press Stop.");
            return null;
        }
        catch (UpnpException ex)
        {
            return ex.Friendly;
        }
        catch (Exception ex)
        {
            return "The router didn't answer: " + ex.Message;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    public static async Task CloseAsync(Action<string> log)
    {
        try
        {
            await Upnp.RemoveMappingAsync();
            log("UPnP: closed the router port.");
        }
        catch (Exception)
        {
            // It expires by itself.
        }
        Changed?.Invoke();
    }
}
