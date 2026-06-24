using System.Collections.Generic;

namespace DotnetBridge.Abstractions;

/// <summary>
/// Minimal, reflection-free router. Patterns support literal segments and "{name}" params,
/// e.g. "/feature/run/{id}". Trim-safe and netstandard2.0-safe by design.
/// </summary>
public sealed class RouteTable
{
    private readonly List<Route> _routes = new();

    public void Map(string method, string pattern, RouteHandler handler) =>
        _routes.Add(new Route(method.ToUpperInvariant(), SplitSegments(pattern), handler));

    public void MapGet(string pattern, RouteHandler handler) => Map("GET", pattern, handler);
    public void MapPost(string pattern, RouteHandler handler) => Map("POST", pattern, handler);
    public void MapPut(string pattern, RouteHandler handler) => Map("PUT", pattern, handler);
    public void MapDelete(string pattern, RouteHandler handler) => Map("DELETE", pattern, handler);

    /// <summary>Returns the handler + extracted route values, or null if no match.</summary>
    public (RouteHandler Handler, Dictionary<string, string> Values)? Match(string method, string path)
    {
        var segments = SplitSegments(path);
        foreach (var route in _routes)
        {
            if (!string.Equals(route.Method, method, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (route.Segments.Length != segments.Length) continue;

            var values = new Dictionary<string, string>();
            var ok = true;
            for (var i = 0; i < segments.Length; i++)
            {
                var pat = route.Segments[i];
                if (pat.Length > 1 && pat[0] == '{' && pat[pat.Length - 1] == '}')
                    values[pat.Substring(1, pat.Length - 2)] = segments[i];
                else if (!string.Equals(pat, segments[i], System.StringComparison.Ordinal))
                { ok = false; break; }
            }
            if (ok) return (route.Handler, values);
        }
        return null;
    }

    private static string[] SplitSegments(string path) =>
        path.Trim('/').Length == 0
            ? System.Array.Empty<string>()
            : path.Trim('/').Split('/');

    private sealed class Route
    {
        public string Method { get; }
        public string[] Segments { get; }
        public RouteHandler Handler { get; }
        public Route(string method, string[] segments, RouteHandler handler)
        { Method = method; Segments = segments; Handler = handler; }
    }
}
