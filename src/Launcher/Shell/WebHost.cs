using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// Serves the launcher's UI (the embedded web/ folder) and its API on 127.0.0.1 only. The window shows
/// it in WebView2. API calls, events and pictures need the random token from the page URL, and the Host
/// header must be our own address, so other programs and web pages can't drive the launcher.
/// </summary>
public sealed class WebHost : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Api _api;
    private readonly List<Channel<string>> _clients = new();
    private WebApplication? _app;

    public string Token { get; }
    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/?t={Token}";

    public WebHost(Api api, string? token = null)
    {
        _api = api;
        Token = token ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        api.Push = Push;
    }

    /// <param name="port">0 picks a free port.</param>
    public async Task StartAsync(int port = 0)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            // DNS rebinding: a page on another site that resolves to 127.0.0.1 still sends its own Host.
            if (context.Request.Host.Host != "127.0.0.1")
            {
                context.Response.StatusCode = 403;
                return;
            }
            var path = context.Request.Path.Value ?? "";
            var needsToken = path.StartsWith("/api/") || path == "/events" || path == "/img" || path.StartsWith("/theme/");
            var token = context.Request.Headers["X-Token"].FirstOrDefault() ?? context.Request.Query["t"].FirstOrDefault();
            if (needsToken && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token ?? ""), Encoding.ASCII.GetBytes(Token)))
            {
                context.Response.StatusCode = 401;
                return;
            }
            await next();
        });

        app.MapPost("/api/{name}", async (string name, HttpContext context) =>
        {
            JsonElement args;
            try
            {
                args = context.Request.ContentLength is > 0
                    ? await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body, Json)
                    : default;
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }
            try
            {
                return Results.Json(new { ok = true, result = await _api.CallAsync(name, args) }, Json);
            }
            catch (ApiException ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, Json);
            }
            catch (Exception ex)
            {
                _api.App.Log($"FAILED ({name}): {ex.Message}");
                return Results.Json(new { ok = false, error = ex.Message }, Json);
            }
        });

        app.MapGet("/events", async context =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
            lock (_clients) _clients.Add(channel);
            try
            {
                await context.Response.WriteAsync(": hello\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await foreach (var message in channel.Reader.ReadAllAsync(context.RequestAborted))
                {
                    await context.Response.WriteAsync($"data: {message}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // page closed or reloaded
            }
            finally
            {
                lock (_clients) _clients.Remove(channel);
            }
        });

        // Item pictures through the launcher's disk cache (fast, and they still show offline).
        app.MapGet("/img", async (string u, int? s, HttpContext context) =>
        {
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return Results.BadRequest();
            var bytes = await _api.App.Images.GetBytesAsync(u, s ?? 256);
            if (bytes == null) return Results.NotFound();
            context.Response.Headers.CacheControl = "max-age=86400";
            return Results.File(bytes, ContentType(bytes));
        });

        // Setup › Appearance: the background picture and custom.css from the launcher's data folder.
        app.MapGet("/theme/background", (HttpContext context) =>
        {
            var file = Api.BackgroundPath();
            if (file == null) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-cache";
            var type = Path.GetExtension(file) switch { ".webp" => "image/webp", ".png" => "image/png", _ => "image/jpeg" };
            return Results.File(File.ReadAllBytes(file), type);
        });
        app.MapGet("/theme/custom.css", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            var file = Api.CustomCssFile;
            return Results.Text(File.Exists(file) ? File.ReadAllText(file) : "", "text/css; charset=utf-8");
        });

        app.MapGet("/{**path}", (string? path) =>
        {
            var file = string.IsNullOrEmpty(path) ? "index.html" : path;
            var bytes = WebFiles.Read(file);
            return bytes == null ? Results.NotFound() : Results.File(bytes, WebFiles.ContentType(file));
        });

        await app.StartAsync();
        _app = app;
        Port = new Uri(app.Urls.First()).Port;
    }

    /// <summary>Sends an event to every open page: { type, data }.</summary>
    public void Push(string type, object? data)
    {
        var message = JsonSerializer.Serialize(new { type, data }, Json);
        lock (_clients)
            foreach (var client in _clients) client.Writer.TryWrite(message);
    }

    private static string ContentType(byte[] bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg"
        : bytes.Length > 4 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' ? "image/webp"
        : "image/png";

    public async ValueTask DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
    }
}

/// <summary>The UI files, embedded in the exe as web/... resources.</summary>
public static class WebFiles
{
    // Resource names use the build machine's path separator (web/js\\pages\\x.js on Windows).
    private static readonly Dictionary<string, string> Names = typeof(WebFiles).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith("web/"))
        .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase);

    /// <summary>--web-dir: serve the UI from this folder instead of the exe (edit, then reload the page).</summary>
    public static string? DevDir { get; set; }

    public static byte[]? Read(string path)
    {
        if (DevDir != null)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DevDir)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, path));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
        if (!Names.TryGetValue("web/" + path.Replace('\\', '/'), out var name)) return null;
        using var stream = typeof(WebFiles).Assembly.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };
}
