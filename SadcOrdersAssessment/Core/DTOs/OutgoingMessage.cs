using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.DTOs
{
    public sealed record OutgoingMessage(Guid MessageId, string Type, string RoutingKey, string Body, string? CorrelationId);
}
