namespace RingDeck.Core;

/// <summary>Encrypts secrets (API keys, secret headers) for storage in config.json. Stored values start with
/// "dpapi:"; anything else is treated as already plain.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Reveal(string stored);
}
