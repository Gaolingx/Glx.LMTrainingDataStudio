using System.Security.Cryptography;
using System.Text;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Encrypts API keys before they are written to settings.json.
/// </summary>
public interface IApiKeyProtector
{
    /// <summary>Whether the current platform can protect keys at rest.</summary>
    bool IsAvailable { get; }

    bool IsProtected(string value);

    /// <summary>Encrypts a plaintext key into the value written to disk.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Restores a plaintext key from a stored value. Returns false when an encrypted
    /// value cannot be decrypted (wrong user, corrupted payload, unsupported platform).
    /// </summary>
    bool TryUnprotect(string storedValue, out string? plaintext);
}

/// <summary>
/// Protects API keys with Windows DPAPI scoped to the current user. Other platforms
/// cannot protect keys, so settings are persisted without them and environment
/// variables remain the only key source.
/// </summary>
public sealed class DpapiApiKeyProtector : IApiKeyProtector
{
    public const string Prefix = "dpapi:v1:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LMTrainingDataStudio.ApiKey.v1");

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        var payload = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(payload);
    }

    public bool TryUnprotect(string storedValue, out string? plaintext)
    {
        plaintext = null;
        if (!IsAvailable)
        {
            return false;
        }

        try
        {
            var payload = Convert.FromBase64String(storedValue[Prefix.Length..]);
            var bytes = ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            return !string.IsNullOrWhiteSpace(plaintext);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
