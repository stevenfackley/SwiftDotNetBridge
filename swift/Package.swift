// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DotnetBridge",
    platforms: [.iOS(.v15), .macOS(.v12), .macCatalyst(.v15)],
    products: [
        .library(name: "DotnetBridge", targets: ["DotnetBridge"]),
    ],
    targets: [
        // The xcframework's module is "CDni" (from Modules/module.modulemap).
        // Build it first on macOS: ./build/build-xcframework.sh
        .binaryTarget(name: "CDni", path: "Frameworks/CDni.xcframework"),
        // For remote distribution instead, use:
        // .binaryTarget(name: "CDni",
        //   url: "https://.../CDni-1.0.0.xcframework.zip",
        //   checksum: "<swift package compute-checksum CDni-1.0.0.xcframework.zip>"),
        .target(name: "DotnetBridge", dependencies: ["CDni"]),
        .testTarget(name: "DotnetBridgeTests", dependencies: ["DotnetBridge"]),
    ]
)
