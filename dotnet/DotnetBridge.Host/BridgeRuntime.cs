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
            if (_initialized) return 0;
            var routes = new RouteTable();
            foreach (var m in modules) m.Configure(routes);
            _routes = routes;
            _server = new BridgeServer(routes);
            _initialized = true;
            return 0;
        }
    }

    public static int HttpStart()
    {
        lock (Gate)
        {
            if (_server is null) return -1;     // DNI_NOT_INITIALIZED
            return _server.Start();             // >0 port
        }
    }

    public static int HttpStop()
    {
        lock (Gate) { _server?.Stop(); return 0; }
    }

    public static void Shutdown()
    {
        lock (Gate) { _server?.Stop(); _server = null; _routes = null; _initialized = false; }
    }
}
