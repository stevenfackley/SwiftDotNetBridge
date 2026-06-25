using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotnetBridge.Host;
using Xunit;

public class ParserFuzzTests
{
    /// <summary>
    /// Feed thousands of random / HTTP-shaped-but-corrupted byte streams to the parser. The contract:
    /// it may return a request, return null, or throw <see cref="BridgeProtocolException"/> — but NEVER
    /// an unexpected exception (IndexOutOfRange, NRE, FormatException, …). The hand-rolled parser is the
    /// only place untrusted bytes enter, so a crash here is a real defect. Seeded for reproducibility.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(12345)]
    public async Task Parser_never_throws_unexpectedly_on_garbage(int seed)
    {
        var rng = new Random(seed);
        for (var i = 0; i < 2000; i++)
        {
            using var stream = new MemoryStream(RandomRequestish(rng));
            try
            {
                await HttpRequestParser.ReadAsync(stream, 4096, 64, 65536, CancellationToken.None);
            }
            catch (BridgeProtocolException) { /* expected: a deliberate 4xx rejection */ }
            // Any other exception escapes and fails the test — that's the bug we're hunting.
        }
    }

    private static byte[] RandomRequestish(Random rng)
    {
        // Half pure-random bytes (exercise the tokenizer), half HTTP-shaped-but-corrupted
        // (exercise the request-line / header / Content-Length logic).
        if (rng.Next(2) == 0)
        {
            var buf = new byte[rng.Next(0, 512)];
            rng.NextBytes(buf);
            return buf;
        }

        var sb = new StringBuilder();
        var methods = new[] { "GET", "POST", "PUT", "DELETE", "CONNECT", "\0BAD", "" };
        sb.Append(methods[rng.Next(methods.Length)]).Append(' ')
          .Append(RandomToken(rng)).Append(' ').Append("HTTP/1.1\r\n");

        var headerCount = rng.Next(0, 12);
        for (var h = 0; h < headerCount; h++)
            sb.Append(RandomToken(rng)).Append(rng.Next(2) == 0 ? ":" : "").Append(RandomToken(rng)).Append("\r\n");

        if (rng.Next(2) == 0) sb.Append("Content-Length: ").Append(RandomToken(rng)).Append("\r\n");
        sb.Append("\r\n");
        if (rng.Next(2) == 0) sb.Append(RandomToken(rng));   // sometimes a body

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string RandomToken(Random rng)
    {
        var len = rng.Next(0, 20);
        var chars = new char[len];
        for (var i = 0; i < len; i++) chars[i] = (char)rng.Next(32, 127);   // printable ASCII incl. space, %, etc.
        return new string(chars);
    }
}
