using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StudyHud.Core.Services;

namespace StudyHud.Storage;

/// <summary>
/// DPAPI-backed implementation of <see cref="ICredentialStore"/> (spec §46). Each secret is encrypted
/// with Windows DPAPI (per-user scope) and written to a file under
/// <c>%LOCALAPPDATA%\StudyHud\creds\</c>. The secret is never written in plaintext and never logged.
///
/// DPAPI ties the ciphertext to the current Windows user account, so the file is useless if copied to
/// another machine or user — appropriate for a local-first study tool holding a Notion token.
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _dir;
    private readonly ILogger<DpapiCredentialStore> _logger;

    // Extra entropy mixed into the DPAPI blob so another app running as the same user cannot trivially
    // unprotect it without also knowing this value.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StudyHud.Credential.v1");

    public DpapiCredentialStore(string directory, ILogger<DpapiCredentialStore> logger)
    {
        _dir = directory;
        _logger = logger;
    }

    public async Task StoreAsync(string key, string secret, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_dir);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            var path = PathFor(key);
            var tempPath = path + ".tmp";
            await File.WriteAllBytesAsync(tempPath, encrypted, ct).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            _logger.LogDebug("Credential stored for '{Key}'.", key); // never log the secret (spec §46)
        }
        finally
        {
            Array.Clear(plaintext); // scrub the plaintext copy
        }
    }

    public async Task<string?> RetrieveAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;

        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                Array.Clear(plaintext);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            // A corrupt blob, or one written by a different user/machine, cannot be recovered — treat
            // it as "no credential" rather than crashing (spec §69). The user can re-enter the token.
            _logger.LogWarning(ex, "Could not read credential '{Key}'; treating as absent.", key);
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete credential '{Key}'.", key);
        }
        return Task.CompletedTask;
    }

    private string PathFor(string key) => Path.Combine(_dir, Sanitize(key) + ".bin");

    private static string Sanitize(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = key.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars);
        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }
}
