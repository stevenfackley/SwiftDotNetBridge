using System.IO;
using System.Threading;
using DotnetBridge.Host;
using SharpFuzz;

// libFuzzer harness over the HTTP request parser — the one place untrusted bytes enter the process.
// Instrument with `sharpfuzz DotnetBridge.Host.dll`, then run this exe under libFuzzer. The parser may
// return a request, return null, or throw BridgeProtocolException; anything else is a crash the fuzzer
// will surface.
internal static class Program
{
    private static void Main()
    {
        Fuzzer.LibFuzzer.Run(data =>
        {
            try
            {
                using var stream = new MemoryStream(data.ToArray());
                HttpRequestParser.ReadAsync(stream, 4096, 64, 65536, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (BridgeProtocolException) { /* expected: a deliberate 4xx rejection */ }
        });
    }
}
