using System.Security.Cryptography;
using System.Text;

namespace Hoshi.Ogs.Auth;

/// <summary>Proof Key for Code Exchange (RFC 7636), S256 method.</summary>
public static class Pkce
{
    /// <summary>A 43-character base64url verifier from 32 random bytes.</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeFor(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class OgsAuthException : Exception
{
    public OgsAuthException()
    {
    }

    public OgsAuthException(string message)
        : base(message)
    {
    }

    public OgsAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Where the OAuth refresh token is kept (the OS secure store in the app, in-memory in tests).</summary>
public interface ITokenStore
{
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);

    Task WriteAsync(string key, string value, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Adds authentication to REST requests (Bearer token, or session cookie + CSRF in password mode).</summary>
public interface IOgsCredentials
{
    Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
