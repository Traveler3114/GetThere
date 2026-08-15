using Microsoft.AspNetCore.DataProtection;

namespace TransitInfoAPI.Services;

/// <summary>
/// Encrypts the operator credentials this service stores, so a database read is not a credential
/// read.
/// <para>
/// <c>CustomSource.AuthConfig</c> holds whatever an operator's endpoint needs — a bearer token, a
/// basic-auth username and password, or an arbitrary API-key header (see
/// <see cref="CustomSourceEngine"/>'s <c>ApplyAuth</c>). It was written to the column verbatim, so
/// anyone who could read the table, a backup, or a query log held every operator's live credential.
/// </para>
/// <para>
/// Deliberately applied in the manager rather than as an EF value converter. A converter would run
/// inside <c>OnModelCreating</c>, which means handing the DbContext an <see cref="IDataProtector"/>
/// and changing how it is constructed — and it would silently apply to <em>every</em> read,
/// including ones that only want to know whether a credential exists. Protecting at the two places
/// that write it and unprotecting at the two that spend it keeps the blast radius to four call
/// sites.
/// </para>
/// <para>
/// <b>Key management matters.</b> The default key ring is stored on the local filesystem and is not
/// shared between machines, so a deployment that scales out or rebuilds its filesystem must
/// configure <c>PersistKeysToX</c> and <c>ProtectKeysWithY</c> — otherwise each instance mints its
/// own keys and cannot read what the others wrote. See docs/secrets-rotation.md.
/// </para>
/// </summary>
public sealed class SecretProtector
{
    /// <summary>
    /// Marks a value this class produced. Without it there is no way to tell ciphertext from a row
    /// written before protection existed, and <see cref="Unprotect"/> would have to guess.
    /// </summary>
    private const string Prefix = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<SecretProtector> _logger;

    public SecretProtector(IDataProtectionProvider provider, ILogger<SecretProtector> logger)
    {
        // The purpose string scopes the key: a payload protected for this purpose cannot be
        // unprotected by any other consumer of the same key ring.
        _protector = provider.CreateProtector("TransitInfoAPI.CustomSource.AuthConfig");
        _logger = logger;
    }

    /// <summary>
    /// Encrypts a credential for storage. Null and blank pass through unchanged — "no credential"
    /// is a legitimate state and encrypting it would only make it indistinguishable from one.
    /// </summary>
    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return plaintext;

        // Already protected — re-protecting would double-wrap, which Unprotect would then only
        // half-undo. This happens whenever a caller round-trips a value it read back.
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext;

        return Prefix + _protector.Protect(plaintext);
    }

    /// <summary>
    /// Decrypts a stored credential.
    /// <para>
    /// A value without the marker is returned as-is: rows written before this existed hold
    /// plaintext, and failing them would break every custom source that already has a credential.
    /// They are re-protected the next time the source is saved. Remove this fallback once no
    /// unprefixed row remains.
    /// </para>
    /// </summary>
    public string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;

        try
        {
            return _protector.Unprotect(stored[Prefix.Length..]);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // The key ring that wrote this is gone or unreadable. Returning null degrades the source
            // to unauthenticated — which fails at the operator's endpoint with a clear 401 — rather
            // than throwing and taking down an unrelated import that happened to run first.
            _logger.LogError(ex,
                "A stored credential could not be decrypted. The data-protection key ring that wrote it is "
                + "unavailable; re-enter the credential on the source. Requests will be sent unauthenticated.");
            return null;
        }
    }
}
