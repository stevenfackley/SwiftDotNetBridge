using System;
using System.Collections.Generic;
using System.Text;

namespace DotnetBridge.Abstractions;

/// <summary>
/// A response from a route handler: status code, content type, and a raw byte body.
/// The bridge does no serialization itself — use the <see cref="Json"/>/<see cref="Text"/>
/// factories or set <see cref="Body"/> directly.
/// </summary>
public sealed class BridgeResponse
{
    /// <summary>HTTP status code. Defaults to 200.</summary>
    public int StatusCode { get; set; } = 200;

    /// <summary>Response content type. Defaults to <c>application/octet-stream</c>.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Raw response body bytes. Defaults to an empty array.</summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Extra response headers (case-insensitive). The transport always writes Content-Type,
    /// Content-Length, and Connection itself; use this for additional headers such as a correlated
    /// <c>X-Request-ID</c>.
    /// </summary>
    public IDictionary<string, string> Headers { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Build a JSON response (UTF-8) with the given status.</summary>
    public static BridgeResponse Json(string json, int status = 200) => new()
    {
        StatusCode = status, ContentType = "application/json; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(json)
    };

    /// <summary>Build a plain-text response (UTF-8) with the given status.</summary>
    public static BridgeResponse Text(string text, int status = 200) => new()
    {
        StatusCode = status, ContentType = "text/plain; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(text)
    };

    /// <summary>A 404 Not Found text response.</summary>
    public static BridgeResponse NotFound() => Text("Not Found", 404);
}
