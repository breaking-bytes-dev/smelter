using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Smelter.Plugins.Models;

namespace Smelter.Plugins.Services
{
    /// <summary>
    /// Converts raw string values from source files to the correct CRM attribute types
    /// based on the configured ColumnDataType.
    /// 
    /// Null or empty strings return null for all types (caller handles required field validation).
    /// Invalid formats throw FormatException with a descriptive message.
    /// </summary>
    public class TypeCoercer
    {
        private static readonly HashSet<string> TrueValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "true", "1", "yes" };

        private static readonly HashSet<string> FalseValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "false", "0", "no" };

        // Plausible Excel OADate range for business dates: ~1900-01-01 (2) to ~2100-12-31 (73415).
        // Numbers outside this range are treated as non-dates rather than converted to absurd dates.
        private const double MinPlausibleOaDate = 2;
        private const double MaxPlausibleOaDate = 73415;

        /// <summary>
        /// Coerce a raw string value to the target CRM type.
        /// </summary>
        /// <param name="value">Raw string value from the source file.</param>
        /// <param name="dataType">Target data type.</param>
        /// <returns>Coerced value (string, int, decimal, DateTime, bool, Money, Guid) or null.</returns>
        /// <exception cref="FormatException">Thrown when the value cannot be parsed to the target type.</exception>
        public object Coerce(string value, ColumnDataType dataType)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return null;

            switch (dataType)
            {
                case ColumnDataType.String:
                    return trimmed; // #13: trim edges for consistency with all other types

                case ColumnDataType.Integer:
                    if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
                        throw new FormatException($"Cannot convert '{value}' to Integer.");
                    return intResult;

                case ColumnDataType.Decimal:
                    if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var decResult))
                        throw new FormatException($"Cannot convert '{value}' to Decimal.");
                    return decResult;

                case ColumnDataType.DateTime:
                    if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dtResult))
                        return dtResult;
                        
                    // Fallback for Excel OADate serial numbers (e.g., 44941 for Jan 15, 2023), but ONLY
                    // within a plausible business-date range so stray numbers (IDs, codes) don't silently
                    // become absurd dates. #12.
                    if (double.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var oaDate)
                        && oaDate >= MinPlausibleOaDate && oaDate <= MaxPlausibleOaDate)
                        return DateTime.FromOADate(oaDate);

                    throw new FormatException($"Cannot convert '{value}' to DateTime.");

                case ColumnDataType.Boolean:
                    if (TrueValues.Contains(trimmed)) return true;
                    if (FalseValues.Contains(trimmed)) return false;
                    throw new FormatException(
                        $"Cannot convert '{value}' to Boolean. Expected: true/false, 1/0, yes/no.");

                case ColumnDataType.Money:
                    if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var moneyResult))
                        throw new FormatException($"Cannot convert '{value}' to Money.");
                    return new Money(moneyResult);

                case ColumnDataType.Guid:
                    if (!Guid.TryParse(trimmed, out var guidResult))
                        throw new FormatException($"Cannot convert '{value}' to GUID.");
                    return guidResult;

                case ColumnDataType.OptionSet:
                    if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var optResult))
                        return new OptionSetValue(optResult);
                    
                    throw new FormatException($"Cannot convert '{value}' to OptionSet. Use MAP transform if mapping is required.");

                case ColumnDataType.Lookup:
                    // Return the raw string regardless of whether it parses as a GUID.
                    // NormalizationEngine is responsible for wrapping it in an EntityReference
                    // (either by GUID or by alternate key via LookupMatchColumn). TypeCoercer
                    // has no knowledge of the target table — that context lives in ColumnMappingConfig.
                    if (Guid.TryParse(trimmed, out var lookupGuid))
                    {
                        return value;
                    }
                    return value;

                default:
                    throw new ArgumentOutOfRangeException(nameof(dataType), dataType,
                        $"Unsupported data type: {dataType}");
            }
        }
    }
}
