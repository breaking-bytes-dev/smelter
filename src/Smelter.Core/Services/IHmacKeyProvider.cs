namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Resolves the secret key bytes used by the <c>HMAC</c> DSL function. The DSL only ever carries
    /// the key NAME (a label, e.g. <c>"cm-pan"</c>); the key material is resolved here, at runtime,
    /// and never travels in a template, a CSV, a log or a trace.
    /// <para>
    /// Implementations live outside <c>Smelter.Core</c> (the core has no Dataverse dependency):
    /// <c>DataverseHmacKeyProvider</c> is the DEV-only one; PROD must use a managed-identity +
    /// Azure Key Vault implementation.
    /// </para>
    /// </summary>
    public interface IHmacKeyProvider
    {
        /// <summary>
        /// Returns the key bytes registered under <paramref name="keyName"/>, or <c>null</c> when no
        /// such key exists. Implementations must never include key material in exception messages.
        /// </summary>
        /// <param name="keyName">Key label as written in the expression (not the key itself).</param>
        byte[] GetKey(string keyName);
    }
}
