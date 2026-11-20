using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public static class SadcCurrencyRules
    {
        public const string SouthAfricanRand = "ZAR";

        public static readonly IReadOnlySet<string> CommonMonetaryAreaCurrencies =
            new HashSet<string>(StringComparer.Ordinal) { "ZAR", "NAD", "LSL", "SZL" };

        private static readonly IReadOnlySet<string> CommonMonetaryAreaCountries =
            new HashSet<string>(StringComparer.Ordinal) { "ZA", "NA", "LS", "SZ" };

        private static readonly IReadOnlyList<SadcCountry> Countries = new List<SadcCountry>
    {
        new("AO", "Angola", new[] { "AOA" }),
        new("BW", "Botswana", new[] { "BWP" }),
        new("KM", "Comoros", new[] { "KMF" }),
        new("CD", "DR Congo", new[] { "CDF" }),
        new("SZ", "Eswatini", new[] { "SZL" }),
        new("LS", "Lesotho", new[] { "LSL" }),
        new("MG", "Madagascar", new[] { "MGA" }),
        new("MW", "Malawi", new[] { "MWK" }),
        new("MU", "Mauritius", new[] { "MUR" }),
        new("MZ", "Mozambique", new[] { "MZN" }),
        new("NA", "Namibia", new[] { "NAD" }),
        new("SC", "Seychelles", new[] { "SCR" }),
        new("ZA", "South Africa", new[] { "ZAR" }),
        new("TZ", "Tanzania", new[] { "TZS" }),
        new("ZM", "Zambia", new[] { "ZMW" }),
        new("ZW", "Zimbabwe", new[] { "ZWL", "USD", "ZWG" }),
    };

        private static readonly IReadOnlyDictionary<string, SadcCountry> ByCode =
            Countries.ToDictionary(c => c.Code, StringComparer.Ordinal);

        public static bool IsSadcCountry(string? countryCode) =>
            countryCode is not null && ByCode.ContainsKey(Normalize(countryCode));

        public static bool IsCommonMonetaryAreaCountry(string countryCode) =>
            CommonMonetaryAreaCountries.Contains(Normalize(countryCode));

        /// <summary>All member states with the currencies allowed under the given policy.</summary>
        public static IReadOnlyList<SadcCountry> GetCountries(bool allowRandInCommonMonetaryArea) =>
            Countries
                .Select(c => c with { Currency = AllowedCurrencies(c.Code, allowRandInCommonMonetaryArea) })
                .ToList();

        /// <summary>Currencies allowed for a country; empty when the country is not an SADC member.</summary>
        public static IReadOnlyList<string> AllowedCurrencies(string countryCode, bool allowRandInCommonMonetaryArea)
        {
            if (!ByCode.TryGetValue(Normalize(countryCode), out var country))
            {
                return Array.Empty<string>();
            }

            if (allowRandInCommonMonetaryArea
                && IsCommonMonetaryAreaCountry(country.Code)
                && !country.Currency.Contains(SouthAfricanRand))
            {
                return country.Currency.Append(SouthAfricanRand).ToList();
            }

            return country.Currency;
        }

        public static bool IsCurrencyAllowed(string countryCode, string currencyCode, bool allowRandInCommonMonetaryArea) =>
            AllowedCurrencies(countryCode, allowRandInCommonMonetaryArea).Contains(Normalize(currencyCode));

        public static string Normalize(string code) => code.Trim().ToUpperInvariant();
    }
}
