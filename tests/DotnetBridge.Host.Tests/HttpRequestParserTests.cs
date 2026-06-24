using System.IO;
using System.Text;
using System.Threading;
using DotnetBridge.Host;
using Xunit;

public class HttpRequestParserTests
{
    [Fact]
    public async System.Threading.Tasks.Task Parses_post_with_body()
    {
        var raw = "POST /invoke?x=1 HTTP/1.1\r\n" +
                  "Host: 127.0.0.1\r\n" +
                  "Content-Length: 5\r\n" +
                  "Content-Type: application/json\r\n" +
                  "\r\n" +
                  "hello";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

        var req = await HttpRequestParser.ReadAsync(stream, CancellationToken.None);

        Assert.Equal("POST", req!.Method);
        Assert.Equal("/invoke", req.Path);
        Assert.Equal("x=1", req.RawQuery);
        Assert.Equal("5", req.Headers["Content-Length"]);
        Assert.Equal("hello", Encoding.UTF8.GetString(req.Body));
    }

    [Fact]
    public async System.Threading.Tasks.Task Returns_null_on_empty_stream()
    {
        using var stream = new MemoryStream(System.Array.Empty<byte>());
        Assert.Null(await HttpRequestParser.ReadAsync(stream, CancellationToken.None));
    }
}
