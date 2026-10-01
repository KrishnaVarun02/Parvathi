// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Parvathi",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "ParvathiCore", targets: ["ParvathiCore"]),
        .executable(name: "Parvathi", targets: ["Parvathi"])
    ],
    targets: [
        .target(name: "ParvathiCore"),
        .executableTarget(name: "Parvathi", dependencies: ["ParvathiCore"]),
        .testTarget(name: "ParvathiCoreTests", dependencies: ["ParvathiCore"]),
        .testTarget(name: "ParvathiServicesTests", dependencies: ["Parvathi", "ParvathiCore"])
    ],
    swiftLanguageModes: [.v5]
)
