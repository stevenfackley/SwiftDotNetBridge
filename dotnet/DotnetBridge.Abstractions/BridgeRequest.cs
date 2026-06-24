namespace DotnetBridge.Abstractions;

public sealed class BridgeRequest
{
    public string Method { get; }
    public string Path { get; }                       // decoded, no query string
    public string RawQuery { get; }                   // e.g. "q=foo&n=2" ("" if none)
    public IReadOnlyDictionary<string, string> Headers { get; }
    public byte[] Body { get; }
    public IReadOnlyDictionary<string, string> RouteValues { get; }

    public BridgeRequest(string method, string path, string rawQuery,
        IReadOnlyDictionary<string, string> headers, byte[] body,
        IReadOnlyDictionary<string, string> routeValues)
    {
        Method = method; Path = path; RawQuery = rawQuery;
        Headers = headers; Body = body; RouteValues = routeValues;
    }
}
