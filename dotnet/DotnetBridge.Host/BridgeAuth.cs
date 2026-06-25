using System;
using System.Security.Cryptography;
using System.Text;

namespace DotnetBridge.Host;

/// <summary>
/// Optional request authentication for the loopback transport. The ephemeral loopback port is
/// reachable by any same-device process, so when a capability token is configured every request
/// must present it in the <c>X-DNI-Auth</c> header. The Swift host generates a fresh per-launch
/// token and hands it to the runtime out-of-band via the <c>DNI_AUTH_TOKEN</c> environment
/// variable (read once at initialize — nothing crosses the frozen C ABI). When no token is
/// configured, auth is disabled and the loopback server is open (the default for tests/samples).
/// </summary>
public static class BridgeAuth
{
    private static byte[]? _token;

    /// <summary>The environment variable the Swift host sets before <c>dni_initialize</c>.</summary>
    public const string EnvVarName = "DNI_AUTH_TOKEN";

    /// <summary>Whether a token is configured (auth is enforced).</summary>
    public static bool IsEnabled => _token is not null;

    /// <summary>
    /// Configure the required token. A null/empty value disables auth. Called once at initialize.
    /// </summary>
    /// <param name="token">The capability token, or null/empty to disable auth.</param>
    public static void Configure(string? token)
        => _token = string.IsNullOrEmpty(token) ? null : Encoding.UTF8.GetBytes(token);

    /// <summary>
    /// Constant-time check of a presented token against the configured one; returns
    /// <see langword="true"/> when auth is disabled. Uses
    /// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// so a wrong guess leaks no timing signal about how many bytes matched.
    /// </summary>
    /// <param name="presented">The token from the request's <c>X-DNI-Auth</c> header, if any.</param>
    public static bool IsAuthorized(string? presented)
    {
        var expected = _token;
        if (expected is null) return true;                  // auth disabled
        if (string.IsNullOrEmpty(presented)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), expected);
    }
}
