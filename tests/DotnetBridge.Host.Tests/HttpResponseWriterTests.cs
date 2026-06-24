using System.IO;
using System.Text;
using System.Threading;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class HttpResponseWriterTests
{
    [Fact]
    public async System.Threading.Tasks.Task Writes_status_headers_body()
    {
        var resp = BridgeResponse.Json("{\"ok\":true}", 200);
        using var ms = new MemoryStream();

        await HttpResponseWriter.WriteAsync(ms, resp, CancellationToken.None);

        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", text);
        Assert.Contains("Content-Length: 11\r\n", text);
        Assert.Contains("Content-Type: application/json; charset=utf-8\r\n", text);
        Assert.EndsWith("\r\n\r\n{\"ok\":true}", text);
    }
}
