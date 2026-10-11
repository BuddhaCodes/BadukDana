using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using Hoshi.Ogs.Auth;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace Hoshi.Droid;

/// <summary>
/// OGS tokens, encrypted with an AES key that never leaves the Android Keystore (AES-GCM); each value is a small
/// file in the app's private folder. No passwords are ever stored.
/// </summary>
internal sealed class KeystoreTokenStore(string directory) : ITokenStore
{
    private const string KeyAlias = "hoshi-tokens";
    private const string Transformation = "AES/GCM/NoPadding";

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        byte[] data = File.ReadAllBytes(path);
        if (data.Length <= 12)
        {
            return Task.FromResult<string?>(null);
        }

        Cipher cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, data[..12]));
        byte[] plain = cipher.DoFinal(data[12..])!;
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
    }

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        Cipher cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(CipherMode.EncryptMode, Key());
        byte[] iv = cipher.GetIV()!;
        byte[] sealedData = cipher.DoFinal(Encoding.UTF8.GetBytes(value))!;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(PathFor(key), [.. iv, .. sealedData]);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    private string PathFor(string key) =>
        Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".bin");

    private static IKey Key()
    {
        KeyStore store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null as KeyStore.ILoadStoreParameter);
        if (store.GetKey(KeyAlias, null) is { } existing)
        {
            return existing;
        }

        KeyGenerator generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .Build());
        return generator.GenerateKey()!;
    }
}
