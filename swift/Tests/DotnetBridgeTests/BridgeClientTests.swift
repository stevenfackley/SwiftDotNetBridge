import XCTest
@testable import DotnetBridge

final class BridgeClientTests: XCTestCase {
    func testHealth() async throws {
        let client = BridgeClient()
        let data = try await client.get("/health")
        XCTAssertEqual(String(data: data, encoding: .utf8), "ok")
        await client.shutdown()
    }

    func testListSeededCustomers() async throws {
        let client = BridgeClient()
        let data = try await client.get("/api/customers")
        XCTAssertTrue((String(data: data, encoding: .utf8) ?? "").contains("Ada Lovelace"))
        await client.shutdown()
    }

    func testCreateCustomer() async throws {
        let client = BridgeClient()
        let body = Data(#"{"name":"Nikola Tesla","email":"nikola@example.com"}"#.utf8)
        let out = try await client.post("/api/customers", body: body)
        XCTAssertTrue((String(data: out, encoding: .utf8) ?? "").contains("Nikola Tesla"))
        await client.shutdown()
    }
}
