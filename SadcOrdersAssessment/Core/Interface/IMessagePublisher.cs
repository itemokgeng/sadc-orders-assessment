using Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface IMessagePublisher
    {
        Task PublishAsync(OutgoingMessage message, CancellationToken ct);
    }
}
