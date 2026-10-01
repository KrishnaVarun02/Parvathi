import Foundation
import Security

enum KeychainStore {
    private static let service = "com.parvathi.assistant.credentials"

    static func load(account: String) throws -> String? {
        var query = baseQuery(account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw KeychainError(status: status) }
        guard let data = item as? Data, let secret = String(data: data, encoding: .utf8) else {
            throw KeychainError(status: errSecDecode)
        }
        return secret
    }

    static func save(_ secret: String, account: String) throws {
        guard !secret.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            try delete(account: account)
            return
        }
        let data = Data(secret.utf8)
        let query = baseQuery(account: account)
        let update: [String: Any] = [kSecValueData as String: data]
        let status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var item = query
            item[kSecValueData as String] = data
            item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            let insertStatus = SecItemAdd(item as CFDictionary, nil)
            guard insertStatus == errSecSuccess else { throw KeychainError(status: insertStatus) }
        } else if status != errSecSuccess { throw KeychainError(status: status) }
    }

    static func delete(account: String) throws {
        let status = SecItemDelete(baseQuery(account: account) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw KeychainError(status: status)
        }
    }

    private static func baseQuery(account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: account,
         kSecAttrSynchronizable as String: false]
    }
}

private struct KeychainError: LocalizedError {
    let status: OSStatus
    var errorDescription: String? {
        let message = SecCopyErrorMessageString(status, nil) as String? ?? "Unknown Keychain error"
        return "Could not access the macOS Keychain (\(status)): \(message). Unlock your login Keychain and retry."
    }
}
