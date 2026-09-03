using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StudyHud.Storage;
using Xunit;

namespace StudyHud.Tests;

// ─── DPAPI credential store (spec §46) ───────────────────────────────────────
// DPAPI is Windows-only; the test project targets net8.0-windows and CI runs on windows-latest.

public sealed class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly DpapiCredentialStore _store;

    public DpapiCredentialStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyHudTests", Guid.NewGuid().ToString("N"));
        _store = new DpapiCredentialStore(_dir, Mock.Of<ILogger<DpapiCredentialStore>>());
    }

    [Fact]
    public async Task StoreThenRetrieve_RoundTripsSecret()
    {
        await _store.StoreAsync("StudyHud.NotionToken", "secret_ntn_abc123");
        var retrieved = await _store.RetrieveAsync("StudyHud.NotionToken");

        retrieved.Should().Be("secret_ntn_abc123");
    }

    [Fact]
    public async Task Retrieve_MissingKey_ReturnsNull()
    {
        (await _store.RetrieveAsync("does-not-exist")).Should().BeNull();
    }

    [Fact]
    public async Task Store_OverwritesExistingSecret()
    {
        await _store.StoreAsync("k", "first");
        await _store.StoreAsync("k", "second");

        (await _store.RetrieveAsync("k")).Should().Be("second");
    }

    [Fact]
    public async Task Delete_RemovesSecret()
    {
        await _store.StoreAsync("k", "value");
        await _store.DeleteAsync("k");

        (await _store.RetrieveAsync("k")).Should().BeNull();
    }

    [Fact]
    public async Task StoredFile_DoesNotContainPlaintextSecret()
    {
        const string secret = "PLAINTEXT_TOKEN_MARKER";
        await _store.StoreAsync("k", secret);

        var file = Directory.EnumerateFiles(_dir, "*.bin").Single();
        var bytes = await File.ReadAllBytesAsync(file);
        var asText = System.Text.Encoding.UTF8.GetString(bytes);

        asText.Should().NotContain(secret, "the token must be encrypted at rest (spec §46)");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
