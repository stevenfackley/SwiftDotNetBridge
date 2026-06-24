namespace DotnetBridge.Abstractions;

/// <summary>An incoming request handed to a route handler. Immutable.</summary>
public sealed class BridgeRequest
{
    /// <summary>HTTP method (e.g. "GET", "POST").</summary>
    public string Method { get; }

    /// <summary>Decoded request path, without the query string.</summary>
    public string Path { get; }                       // decoded, no query string

    /// <summary>Raw query string, e.g. "q=foo&amp;n=2" ("" if none).</summary>
    public string RawQuery { get; }                   // e.g. "q=foo&n=2" ("" if none)

    /// <summary>Request headers (case-insensitive keys).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Raw request body bytes.</summary>
    public byte[] Body { get; }

    /// <summary>Values captured from "{name}" route segments.</summary>
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    /// <summary>Construct a request with all of its parts.</summary>
    public BridgeRequest(string method, string path, string rawQuery,
        IReadOnlyDictionary<string, string> headers, byte[] body,
        IReadOnlyDictionary<string, string> routeValues)
    {
        Method = method; Path = path; RawQuery = rawQuery;
        Headers = headers; Body = body; RouteValues = routeValues;
    }
}
