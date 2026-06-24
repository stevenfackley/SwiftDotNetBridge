using System;
using System.Collections.Generic;

namespace DotnetBridge.Abstractions;

/// <summary>
/// Minimal, reflection-free router. Patterns support literal segments and "{name}" params,
/// e.g. "/feature/run/{id}". Trim-safe and netstandard2.0-safe by design.
/// </summary>
public sealed class RouteTable
{
    private readonly List<Route> _routes = new();

    /// <summary>Register a handler for an HTTP method + URL pattern.</summary>
    public void Map(string method, string pattern, RouteHandler handler) =>
        _routes.Add(new Route(method.ToUpperInvariant(), SplitSegments(pattern), handler));

    /// <summary>Register a GET handler for the pattern.</summary>
    public void MapGet(string pattern, RouteHandler handler) => Map("GET", pattern, handler);

    /// <summary>Register a POST handler for the pattern.</summary>
    public void MapPost(string pattern, RouteHandler handler) => Map("POST", pattern, handler);

    /// <summary>Register a PUT handler for the pattern.</summary>
    public void MapPut(string pattern, RouteHandler handler) => Map("PUT", pattern, handler);

    /// <summary>Register a DELETE handler for the pattern.</summary>
    public void MapDelete(string pattern, RouteHandler handler) => Map("DELETE", pattern, handler);

    /// <summary>Returns the handler + extracted route values, or null if no route matches.</summary>
    public (RouteHandler Handler, Dictionary<string, string> Values)? Match(string method, string path)
    {
        var segments = SplitSegments(path);
        foreach (var route in _routes)
        {
            if (!string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase))
                continue;
            var values = new Dictionary<string, string>();
            if (TryMatchSegments(route.Segments, segments, values))
                return (route.Handler, values);
        }
        return null;
    }

    /// <summary>
    /// True if some route matches the path shape regardless of HTTP method. Lets the server
    /// answer 405 (Method Not Allowed) for a known path instead of a misleading 404.
    /// </summary>
    public bool IsKnownPath(string path)
    {
        var segments = SplitSegments(path);
        foreach (var route in _routes)
            if (TryMatchSegments(route.Segments, segments, values: null))
                return true;
        return false;
    }

    /// <summary>
    /// Matches a path's segments against a route pattern. When <paramref name="values"/> is
    /// non-null, captured "{name}" parameters are written into it. Shared by <see cref="Match"/>
    /// and <see cref="IsKnownPath"/> so the matching rule lives in exactly one place.
    /// </summary>
    private static bool TryMatchSegments(string[] pattern, string[] segments, Dictionary<string, string>? values)
    {
        if (pattern.Length != segments.Length) return false;
        for (var i = 0; i < segments.Length; i++)
        {
            var pat = pattern[i];
            if (pat.Length > 1 && pat[0] == '{' && pat[pat.Length - 1] == '}')
            {
                if (values is not null) values[pat.Substring(1, pat.Length - 2)] = segments[i];
            }
            else if (!string.Equals(pat, segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string[] SplitSegments(string path) =>
        path.Trim('/').Length == 0
            ? Array.Empty<string>()
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
