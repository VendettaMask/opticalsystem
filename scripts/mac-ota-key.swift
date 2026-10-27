import Foundation
import CryptoKit

// Read the domain-separated seed from a private file; only the public key is
// printed. Sparkle's private key file is created with owner-only permissions.
guard CommandLine.arguments.count == 3 else { fatalError("Expected seed and signing-key paths") }
let seed = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1]))
guard seed.count == 32 else { fatalError("Invalid signing seed") }
let key = try Curve25519.Signing.PrivateKey(rawRepresentation: seed)
let publicKey = key.publicKey.rawRepresentation
let secret = (seed + publicKey).base64EncodedString()
let path = CommandLine.arguments[2]
guard FileManager.default.createFile(atPath: path, contents: Data(secret.utf8),
    attributes: [.posixPermissions: 0o600]) else { fatalError("Cannot create signing file") }
print(publicKey.base64EncodedString())
