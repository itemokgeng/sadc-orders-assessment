using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class ProcessedMessage
    {
        public Guid MessageId { get; set; }
        public string MessageType { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
    }
}
