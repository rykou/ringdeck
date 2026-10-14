using System.Security.Cryptography;
using System.Text;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Encrypts secrets with Windows DPAPI for the current user: only this Windows account can read them back.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    const string Prefix = "dpapi:";
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RingDeck.AiConnection");

    public string Protect(string plain) =>
        plain.Length == 0 ? "" : Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string Reveal(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return ""; }   // copied from another account or PC
    }
}
