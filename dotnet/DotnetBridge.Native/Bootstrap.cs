using DotnetBridge.Abstractions;
using Sample.CrudModule;

namespace DotnetBridge.Native;

/// <summary>
/// The single seam you edit per app: list the IBridgeModule instances to host.
/// NativeAOT forbids runtime assembly loading, so modules are wired here at
/// compile time.
/// </summary>
internal static class Bootstrap
{
    public static IBridgeModule[] Modules() => new IBridgeModule[]
    {
        new CustomersModule(),
    };
}
