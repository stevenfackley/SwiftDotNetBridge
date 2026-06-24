using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;

namespace DotnetBridge.Host;

/// <summary>
/// Serializes a <see cref="BridgeResponse"/> as an HTTP/1.1 response onto the connection
/// stream: status line, Content-Type/Content-Length, <c>Connection: close</c>, then the body.
/// </summary>
public static class HttpResponseWriter
{
    /// <summary>Writes the response (head + body) to the stream and flushes.</summary>
    public static async Task WriteAsync(Stream stream, BridgeResponse resp, CancellationToken ct)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(resp.StatusCode).Append(' ')
            .Append(ReasonPhrase(resp.StatusCode)).Append("\r\n");
        head.Append("Content-Type: ").Append(resp.ContentType).Append("\r\n");
        head.Append("Content-Length: ").Append(resp.Body.Length).Append("\r\n");
        head.Append("Connection: close\r\n");
        head.Append("\r\n");

        var headBytes = Encoding.ASCII.GetBytes(head.ToString());
        await stream.WriteAsync(headBytes.AsMemory(), ct).ConfigureAwait(false);
        if (resp.Body.Length > 0)
            await stream.WriteAsync(resp.Body.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string ReasonPhrase(int code) => code switch
    {
        200 => "OK", 201 => "Created", 204 => "No Content",
        400 => "Bad Request", 404 => "Not Found", 405 => "Method Not Allowed",
        413 => "Payload Too Large", 431 => "Request Header Fields Too Large",
        500 => "Internal Server Error", _ => "Status"
    };
}
