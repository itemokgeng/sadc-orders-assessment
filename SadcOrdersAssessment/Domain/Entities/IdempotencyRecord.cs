using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class IdempotencyRecord
    {
        public const int KeyMaxLength = 200;

        public string Key { get; set; } = string.Empty;

        public string RequestHash { get; set; } = string.Empty;

        public int ResponseStatusCode { get; set; }

        public string ResponseBody { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
