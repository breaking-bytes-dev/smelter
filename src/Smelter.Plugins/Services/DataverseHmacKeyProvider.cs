using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// <b>DEV ONLY.</b> Resolves HMAC keys from the protected Dataverse table
    /// <see cref="Table"/> (primary name = key name, e.g. <c>cm-pan</c>; value = the key as hex).
    /// <para>
    /// A key stored in Dataverse is only as protected as that table: it is not in any Smelter
    /// security role (only SYSTEM / System Administrator can read it), its value column is not
    /// auditable nor valid for Advanced Find, but it is still plain data in the environment.
    /// <b>PRODUCTION must NOT use this class.</b> PROD must use the Power Platform managed identity
    /// plus Azure Key Vault (<c>IManagedIdentityService</c>) behind an <see cref="IHmacKeyProvider"/>.
    /// The alternatives were ruled out on the platform: a Custom API plugin cannot receive secure
    /// configuration, and Key Vault–backed secret environment variables do not reach plugins.
    /// </para>
    /// <para>
    /// Must be built with the SYSTEM <see cref="IOrganizationService"/> (<c>CreateOrganizationService(null)</c>),
    /// the only principal that can read the table. Keys are cached per instance (one plugin
    /// execution), including "not found". This class never traces or puts in a message the key
    /// value or any part of it — only the key NAME, which is a label.
    /// </para>
    /// </summary>
    public class DataverseHmacKeyProvider : IHmacKeyProvider
    {
        /// <summary>Logical name of the HMAC Key table.</summary>
        public const string Table = "sanic_tbl_smlt_hmackey";

        /// <summary>Primary name column: the key name (label) referenced from the DSL.</summary>
        public const string NameColumn = "sanic_smlt_hk_name";

        /// <summary>Text column holding the key material as hex (64 chars for a 32-byte key).</summary>
        public const string ValueColumn = "sanic_smlt_hk_value";

        private readonly IOrganizationService _service;
        private readonly Dictionary<string, byte[]> _cache = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of <see cref="DataverseHmacKeyProvider"/>.
        /// </summary>
        /// <param name="service">Organization service used to read the key table (SYSTEM context).</param>
        public DataverseHmacKeyProvider(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Returns the key registered under <paramref name="keyName"/>, or <c>null</c> when there is no
        /// active row with that name (or its value is blank).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// More than one active row has the name, or the stored value is not valid hex. The message
        /// carries the key name only, never the stored value.
        /// </exception>
        public byte[] GetKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return null;

            if (_cache.TryGetValue(keyName, out var cached)) return cached;

            var key = Load(keyName);
            _cache[keyName] = key;
            return key;
        }

        private byte[] Load(string keyName)
        {
            var query = new QueryExpression(Table)
            {
                ColumnSet = new ColumnSet(ValueColumn),
                // 2 rows are enough to detect an ambiguous name without scanning the table.
                TopCount = 2,
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(NameColumn, ConditionOperator.Equal, keyName),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };

            var rows = _service.RetrieveMultiple(query).Entities;
            if (rows.Count == 0) return null;

            // A silently-picked key would yield wrong hashes without any error: fail loudly instead.
            if (rows.Count > 1)
                throw new InvalidOperationException($"HMAC: more than one active key named '{keyName}'.");

            var hex = rows[0].GetAttributeValue<string>(ValueColumn);
            if (string.IsNullOrWhiteSpace(hex)) return null;

            return ParseHex(hex.Trim(), keyName);
        }

        /// <summary>Decodes a hex string (either case). The error names the key, never the content.</summary>
        private static byte[] ParseHex(string hex, string keyName)
        {
            if (hex.Length % 2 != 0)
                throw new InvalidOperationException($"HMAC: key '{keyName}' is not valid hex.");

            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var hi = HexValue(hex[2 * i]);
                var lo = HexValue(hex[2 * i + 1]);
                if (hi < 0 || lo < 0)
                    throw new InvalidOperationException($"HMAC: key '{keyName}' is not valid hex.");
                bytes[i] = (byte)((hi << 4) | lo);
            }

            return bytes;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
