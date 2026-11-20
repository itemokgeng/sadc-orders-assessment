using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.DTOs
{
    public sealed record OrderCreated(
     Guid MessageId,
     Guid OrderId,
     Guid CustomerId,
     string CurrencyCode,
     decimal TotalAmount,
     DateTime CreatedAt,
     string? CorrelationId)
    {
        public const string MessageType = "OrderCreated";
    }
}
