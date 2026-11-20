using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class OutboxMessage
    {
        public Guid Id { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public string? CorrelationId { get; set; }

        public DateTime OccurredAt { get; set; }

        public DateTime? ProcessedAt { get; set; }

        public int Attempts { get; set; }

        public string? LastError { get; set; }
    }
}
