namespace DotnetBridge.Host;

/// <summary>
/// The C-ABI status codes from <c>dni.h</c>, surfaced as named constants so the managed
/// side has a single source of truth instead of magic numbers scattered across the
/// exports and runtime. Negative values are errors; <see cref="Ok"/> is success; a
/// positive value returned by <c>dni_http_start</c> is the bound TCP port.
/// </summary>
public static class DniStatus
{
    /// <summary>Success (<c>DNI_OK</c>).</summary>
    public const int Ok = 0;

    /// <summary>The engine has not been initialized (<c>DNI_NOT_INITIALIZED</c>).</summary>
    public const int NotInitialized = -1;

    /// <summary>An argument was invalid (<c>DNI_INVALID_ARGUMENT</c>).</summary>
    public const int InvalidArgument = -2;

    /// <summary>The server is already running (<c>DNI_ALREADY_RUNNING</c>).</summary>
    public const int AlreadyRunning = -4;

    /// <summary>An unexpected internal failure occurred (<c>DNI_INTERNAL</c>).</summary>
    public const int Internal = -5;
}
