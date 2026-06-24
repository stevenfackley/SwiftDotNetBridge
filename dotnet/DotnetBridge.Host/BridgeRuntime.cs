using DotnetBridge.Abstractions;

namespace DotnetBridge.Host;

/// <summary>
/// Process-wide facade the C exports drive. Owns the single server + route table,
/// populated once from the registered modules at Initialize().
/// </summary>
public static class BridgeRuntime
{
    private static readonly object Gate = new();
    private static BridgeServer? _server;
    private static RouteTable? _routes;
    private static bool _initialized;

    /// <summary>Called once by the publish head with the user's modules.</summary>
    public static int Initialize(params IBridgeModule[] modules)
    {
        lock (Gate)
        {
            if (_initialized) return DniStatus.Ok;
            var routes = new RouteTable();
            foreach (var m in modules) m.Configure(routes);
            _routes = routes;
            _server = new BridgeServer(routes);
            _initialized = true;
            return DniStatus.Ok;
        }
    }

    /// <summary>Binds and starts the loopback server (idempotent).</summary>
    /// <returns>The bound port (&gt;0), or <see cref="DniStatus.NotInitialized"/> if not initialized.</returns>
    public static int HttpStart()
    {
        lock (Gate)
        {
            if (_server is null) return DniStatus.NotInitialized;
            return _server.Start();             // >0 port
        }
    }

    /// <summary>Stops the loopback server. Safe when not running.</summary>
    /// <returns><see cref="DniStatus.Ok"/>.</returns>
    public static int HttpStop()
    {
        lock (Gate) { _server?.Stop(); return DniStatus.Ok; }
    }

    /// <summary>Releases the server and resets the runtime to its uninitialized state.</summary>
    public static void Shutdown()
    {
        lock (Gate) { _server?.Dispose(); _server = null; _routes = null; _initialized = false; }
    }
}
