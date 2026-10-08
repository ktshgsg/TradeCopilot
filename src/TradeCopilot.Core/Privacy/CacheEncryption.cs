using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradeCopilot.Core.Privacy;

/// <summary>
/// DPAPI (CurrentUser scope) encryption for cached answers. Degrades to
/// plaintext rather than failing the cache when DPAPI is unavailable
/// (e.g. LocalSystem / service-account contexts).
/// </summary>
public static class CacheEncryption
{
    private static bool _warned;
    private static ILogger _log = NullLogger.Instance;

    /// <summary>Sets the logger used for the one-time degradation warning. Test hook.</summary>
    /// <param name="log">Logger.</param>
    internal static void SetLogger(ILogger log) => _log = log ?? NullLogger.Instance;

    /// <summary>Resets the one-time warning guard. Test hook.</summary>
    internal static void ResetWarning() => Volatile.Write(ref _warned, false);

    /// <summary>Encrypts plaintext with DPAPI; empty passes through unchanged.</summary>
    /// <param name="plaintext">Answer text to protect.</param>
    /// <returns>Base64 ciphertext, or the input unchanged when DPAPI is unavailable.</returns>
    public static string Encrypt(string plaintext)
    {
        if (plaintext.Length == 0)
        {
            return plaintext;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            WarnOnce($"Cache encryption unavailable, storing plaintext: {ex.Message}");
            return plaintext;
        }
    }

    /// <summary>Decrypts DPAPI ciphertext; plaintext input passes through unchanged.</summary>
    /// <param name="ciphertext">Base64 ciphertext, or plaintext from the degraded path.</param>
    /// <returns>Decrypted answer text.</returns>
    public static string Decrypt(string ciphertext)
    {
        if (ciphertext.Length == 0)
        {
            return ciphertext;
        }

        try
        {
            byte[] protectedBytes = Convert.FromBase64String(ciphertext);
            byte[] bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            // Not base64 at all: must be a degraded-path plaintext row.
            return ciphertext;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            WarnOnce($"Cache decryption failed, returning stored value: {ex.Message}");
            return ciphertext;
        }
    }

    private static void WarnOnce(string message)
    {
        if (Volatile.Read(ref _warned))
        {
            return;
        }

        Volatile.Write(ref _warned, true);
        _log.LogWarning("{Message}", message);
    }
}
