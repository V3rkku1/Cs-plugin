using System.Reflection;
using System.Text.Json;

namespace LanSkinTester.Launcher.Host;

/// <summary>Marks an <see cref="Api"/> method the page can call as <c>api("name", args)</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CallAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>An error message meant for the person using the launcher (shown as a toast, not logged).</summary>
public sealed class ApiException(string message) : Exception(message);

/// <summary>
/// Everything the UI can ask the launcher to do. Split by page into partial files (Api.Setup.cs, ...).
/// A [Call] method takes nothing or one argument (deserialized from the JSON body) and may be async;
/// its return value goes back to the page as JSON.
/// </summary>
public partial class Api
{
    private readonly Dictionary<string, Func<JsonElement, Task<object?>>> _calls = new();
    private readonly SynchronizationContext _ui;

    public AppState App { get; }

    /// <summary>Set by <see cref="WebHost"/>: sends an event to the open page.</summary>
    public Action<string, object?> Push { get; set; } = (_, _) => { };

    /// <summary>The window, for native dialogs (folder / file pickers).</summary>
    public Form Window { get; }

    public Api(AppState app, Form window)
    {
        App = app;
        Window = window;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the API on the UI thread.");
        foreach (var method in GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (method.GetCustomAttribute<CallAttribute>() is { } call)
                _calls.Add(call.Name, Bind(method));

        app.Changed += () => Push("changed", null);
        app.Logged += line =>
        {
            lock (_log)
            {
                _log.Add(line);
                if (_log.Count > 600) _log.RemoveRange(0, _log.Count - 600);
            }
            Push("log", line);
        };
        Init();
    }

    private readonly List<string> _log = new();

    /// <summary>The session's log so far (a page opened later still shows the start).</summary>
    [Call("app.log")]
    private List<string> LogSoFar()
    {
        lock (_log) return [.. _log];
    }

    /// <summary>Each partial file can hook up its own events here.</summary>
    partial void Init();

    /// <summary>
    /// Inventory and loadout calls one at a time: they share one in-memory list and loadouts.json, and pages call
    /// them in parallel (a save while another call enumerates threw, and two saves lost an equip).
    /// </summary>
    internal readonly SemaphoreSlim InventoryGate = new(1, 1);

    public async Task<object?> CallAsync(string name, JsonElement args)
    {
        if (!_calls.TryGetValue(name, out var call)) throw new ApiException($"Unknown call: {name}");
        if (!name.StartsWith("cases.") && !name.StartsWith("inventory.") && !name.StartsWith("tradeup.")) return await call(args);
        await InventoryGate.WaitAsync();
        try { return await call(args); }
        finally { InventoryGate.Release(); }
    }

    /// <summary>Shows a toast in the window: kind is "info", "good", "warning" or "bad".</summary>
    public void Toast(string text, string kind = "info") => Push("toast", new { text, kind });

    /// <summary>Runs on the window's thread (dialogs, anything WinForms) and returns its result.</summary>
    public Task<T> OnUi<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }, null);
        return done.Task;
    }

    public Task OnUi(Action work) => OnUi(() => { work(); return true; });

    private Func<JsonElement, Task<object?>> Bind(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length > 1) throw new InvalidOperationException($"{method.Name}: [Call] methods take at most one argument");
        var argType = parameters.Length == 1 ? parameters[0].ParameterType : null;

        return async json =>
        {
            object?[] args = argType == null ? [] :
                [json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? (argType.IsValueType ? Activator.CreateInstance(argType) : null)
                    : json.Deserialize(argType, WebHost.Json)];
            object? result;
            try
            {
                result = method.Invoke(this, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            if (result is Task task)
            {
                await task;
                var type = task.GetType();
                return type.IsGenericType && type.GetGenericArguments()[0].Name != "VoidTaskResult"
                    ? type.GetProperty("Result")!.GetValue(task)
                    : null;
            }
            return result;
        };
    }
}
