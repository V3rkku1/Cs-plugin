using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Hacker mode's triggerbot on this PC: the server can't press fire for a game client (CS2 rebuilds the
/// buttons from the client's own input), so the plugin sends "fire" to this launcher over localhost and
/// the launcher clicks the left mouse button, like a real shot. Only while the launcher's VAC-off test
/// session runs and the CS2 game window is in front.
/// </summary>
public static class FireRelay
{
    /// <summary>The plugin sends to 127.0.0.1 on this port (lst_fire_relay in the server config).</summary>
    public const int Port = 27099;

    private const string Message = "LST fire";
    private static Thread? _thread;

    public static void Start(Func<bool> allowed, Action<string> log)
    {
        if (_thread != null) return;
        _thread = new Thread(() => Listen(allowed, log)) { IsBackground = true, Name = "FireRelay" };
        _thread.Start();
    }

    private static void Listen(Func<bool> allowed, Action<string> log)
    {
        UdpClient udp;
        try { udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port)); }
        catch (SocketException ex)
        {
            log($"Triggerbot: port {Port} is in use, so it can't press fire in CS2 ({ex.Message}).");
            return;
        }
        var last = DateTime.MinValue;
        while (true)
        {
            try
            {
                IPEndPoint? from = null;
                var data = udp.Receive(ref from);
                if (Encoding.ASCII.GetString(data) != Message || !IPAddress.IsLoopback(from.Address)) continue;
                // The plugin paces shots by the gun's fire rate; this only stops a flood.
                if (DateTime.UtcNow - last < TimeSpan.FromMilliseconds(35) || !allowed() || !GameInFront()) continue;
                last = DateTime.UtcNow;
                Send(MouseLeftDown);
                Thread.Sleep(18); // long enough for the game to see the press
                Send(MouseLeftUp);
            }
            catch (Exception)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>The CS2 game window (not the server console) has focus.</summary>
    private static bool GameInFront()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("cs2", StringComparison.OrdinalIgnoreCase) && TestSession.FindServer()?.Id != p.Id;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const uint InputMouse = 0, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;

    private static void Send(uint flags) =>
        SendInput(1, [new INPUT { type = InputMouse, mi = new MOUSEINPUT { dwFlags = flags } }], Marshal.SizeOf<INPUT>());

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
