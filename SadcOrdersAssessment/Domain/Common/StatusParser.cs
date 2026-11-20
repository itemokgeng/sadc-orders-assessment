using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public static class StatusParser
    {
        public static bool TryParser(string? value, out OrderStatus status)
        {
            status = default;
            if (string.IsNullOrWhiteSpace(value) || char.IsDigit(value.Trim()[0]) || value.Trim()[0] == '-')
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out status) && Enum.IsDefined(status);
        }
    }
}
