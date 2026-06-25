using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Abstractions;
using DotnetBridge.Host;
using Xunit;

public class FramingAndLimitTests
{
    private static Task<BridgeRequest?> Parse(string raw) =>
        HttpRequestParser.ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes(raw)),
            maxLineBytes: 8192, maxHeaderCount: 100, maxBodyBytes: 1024, CancellationToken.None);

    // ---------- ambiguous-framing rejection ----------

    [Fact]
    public async Task Parser_rejects_transfer_encoding_with_400()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            Parse("POST /x HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_duplicate_content_length_with_400()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            Parse("POST /x HTTP/1.1\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nhello"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Parser_rejects_malformed_content_length_with_400()
    {
        var ex = await Assert.ThrowsAsync<BridgeProtocolException>(() =>
            Parse("POST /x HTTP/1.1\r\nContent-Length: notanumber\r\n\r\n"));
        Assert.Equal(400, ex.StatusCode);
    }

    // ---------- Expect: 100-continue ----------

    [Fact]
    public async Task Parser_sends_100_continue_then_reads_body()
    {
        var s = new DuplexStream("POST /x HTTP/1.1\r\nContent-Length: 5\r\nExpect: 100-continue\r\n\r\nhello");

        var req = await HttpRequestParser.ReadAsync(s, CancellationToken.None);

        Assert.Equal("hello", Encoding.UTF8.GetString(req!.Body));
        Assert.Contains("100 Continue", Encoding.ASCII.GetString(s.Written.ToArray()));
    }

    // ---------- connection cap / load shedding ----------

    [Fact]
    public async Task Server_sheds_excess_connections_with_503()
    {
        var original = BridgeLimits.MaxConcurrentConnections;
        BridgeLimits.MaxConcurrentConnections = 1;
        var started = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        try
        {
            var routes = new RouteTable();
            routes.MapGet("/hold", async (_, handlerCt) =>
            {
                started.Release();                       // signal the slot is occupied
                await release.WaitAsync(handlerCt);      // hold it until the test releases
                return BridgeResponse.Text("ok");
            });
            using var server = new BridgeServer(routes);
            var port = server.Start();

            using var http = new HttpClient();
            var first = http.GetAsync($"http://127.0.0.1:{port}/hold");   // takes the only slot
            await started.WaitAsync();                                    // ensure it's held

            using var resp2 = await http.GetAsync($"http://127.0.0.1:{port}/hold");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp2.StatusCode);   // 503

            release.Release();
            using var resp1 = await first;
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        }
        finally
        {
            release.Release();   // guarantee the handler can't hang the run if an assert failed
            BridgeLimits.MaxConcurrentConnections = original;
        }
    }

    /// <summary>A read/write stream: serves a fixed request for reads, captures writes separately,
    /// so a test can both feed the parser and observe the interim 100-continue it emits.</summary>
    private sealed class DuplexStream : Stream
    {
        private readonly MemoryStream _input;
        public MemoryStream Written { get; } = new();

        public DuplexStream(string request) => _input = new MemoryStream(Encoding.ASCII.GetBytes(request));

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => _input.Length;
        public override long Position { get => _input.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _input.ReadAsync(buffer, ct);

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            Written.WriteAsync(buffer, ct);

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
