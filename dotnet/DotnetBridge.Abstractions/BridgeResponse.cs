using System.Text;

namespace DotnetBridge.Abstractions;

public sealed class BridgeResponse
{
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Body { get; set; } = System.Array.Empty<byte>();

    public static BridgeResponse Json(string json, int status = 200) => new()
    {
        StatusCode = status, ContentType = "application/json; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(json)
    };

    public static BridgeResponse Text(string text, int status = 200) => new()
    {
        StatusCode = status, ContentType = "text/plain; charset=utf-8",
        Body = Encoding.UTF8.GetBytes(text)
    };

    public static BridgeResponse NotFound() => Text("Not Found", 404);
}
