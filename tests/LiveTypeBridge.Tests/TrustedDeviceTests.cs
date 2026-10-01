using System.Text.Json;
using LiveTypeBridge.Core.Pairing;
using LiveTypeBridge.Core.Settings;
using Xunit;

namespace LiveTypeBridge.Tests;

public sealed class TrustedDeviceTests
{
    [Fact]
    public void First_pairing_issues_256_bit_token_but_persists_only_hash()
    {
        var settings = new AppSettings();
        var saves = 0;
        var store = new TrustedDeviceStore(settings, () => saves++);

        var credential = store.Enroll("android_9a8b7c6d5e4f3a2b", "Android · Samsung S24");

        Assert.Equal(64, credential.Token.Length);
        Assert.All(credential.Token, c => Assert.True(Uri.IsHexDigit(c)));
        var saved = Assert.Single(settings.TrustedDevices);
        Assert.Equal(credential.ClientId, saved.ClientId);
        Assert.Equal("Android · Samsung S24", saved.DeviceName);
        Assert.Equal(64, saved.TokenHash.Length);
        Assert.NotEqual(credential.Token, saved.TokenHash);
        Assert.DoesNotContain(credential.Token, JsonSerializer.Serialize(settings), StringComparison.Ordinal);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void Trusted_reconnect_requires_exact_client_id_and_token()
    {
        var settings = new AppSettings();
        var store = new TrustedDeviceStore(settings, () => { });
        var credential = store.Enroll("android_phone", "Phone");

        Assert.True(store.TryAuthenticate(credential.ClientId, credential.Token, "Phone renamed"));
        Assert.False(store.TryAuthenticate("another_client", credential.Token, "Phone"));
        Assert.False(store.TryAuthenticate(credential.ClientId, FlipLastHex(credential.Token), "Phone"));
        Assert.False(store.TryAuthenticate(credential.ClientId, null, "Phone"));
        Assert.Equal("Phone renamed", Assert.Single(settings.TrustedDevices).DeviceName);
    }

    [Fact]
    public void Re_pairing_rotates_token_and_revocation_is_immediate()
    {
        var settings = new AppSettings();
        var store = new TrustedDeviceStore(settings, () => { });
        var first = store.Enroll("android_phone", "Phone");
        var second = store.Enroll("android_phone", "Phone");

        Assert.NotEqual(first.Token, second.Token);
        Assert.False(store.TryAuthenticate(first.ClientId, first.Token, "Phone"));
        Assert.True(store.TryAuthenticate(second.ClientId, second.Token, "Phone"));
        Assert.True(store.Revoke(second.ClientId));
        Assert.False(store.TryAuthenticate(second.ClientId, second.Token, "Phone"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Trusted_device_list_is_capped_and_can_be_cleared()
    {
        var settings = new AppSettings();
        var store = new TrustedDeviceStore(settings, () => { });
        for (var i = 0; i < TrustedDeviceStore.MaxTrustedDevices + 5; i++)
            store.Enroll($"android_{i}", $"Phone {i}");

        Assert.Equal(TrustedDeviceStore.MaxTrustedDevices, store.Count);
        store.RevokeAll();
        Assert.Empty(store.Snapshot());
    }

    private static string FlipLastHex(string token)
        => token[..^1] + (token[^1] == '0' ? '1' : '0');
}
