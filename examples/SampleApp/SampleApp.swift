import SwiftUI
import DotnetBridge

@main
struct SampleApp: App {
    var body: some Scene { WindowGroup { ContentView() } }
}

struct ContentView: View {
    @State private var output = "—"
    private let client = BridgeClient()

    var body: some View {
        VStack(spacing: 16) {
            ScrollView { Text(output).font(.body.monospaced()).textSelection(.enabled) }
            Button("GET /api/customers") {
                Task {
                    do { output = String(data: try await client.get("/api/customers"), encoding: .utf8) ?? "" }
                    catch { output = "error: \(error)" }
                }
            }
            Button("POST /api/customers (Nikola Tesla)") {
                Task {
                    do {
                        let body = Data(#"{"name":"Nikola Tesla","email":"nikola@example.com"}"#.utf8)
                        let d = try await client.post("/api/customers", body: body)
                        output = String(data: d, encoding: .utf8) ?? ""
                    } catch { output = "error: \(error)" }
                }
            }
        }
        .padding()
    }
}
