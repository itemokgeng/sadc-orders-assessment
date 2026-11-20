using Domain.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Customer
    {
        public const int NameMaxLength = 200;
        public const int EmailMaxLength = 320;

        private Customer()
        { }

        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public string CountryCode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public static Customer Create(string name, string email, string countryCode, DateTime createAtUtc)
        {

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainRuleException("Name", "Name is required");
            }
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new DomainRuleException("Email", "Email is required");
            }
            if (!SadcCurrencyRules.IsSadcCountry(countryCode))
            {
                throw new DomainRuleException("counttryCode", $"'{countryCode}' is not an SADC memebr satate");
            }
            return new Customer
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                Email = email.Trim().ToLowerInvariant(),
                CountryCode = SadcCurrencyRules.Normalize(countryCode),
                CreatedAt = DateTime.SpecifyKind(createAtUtc, DateTimeKind.Utc)
            };
        }
    }
}
