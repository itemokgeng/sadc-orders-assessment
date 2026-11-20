using Core.DTOs;
using Domain.Entities;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface IOrderRepository
    {
        Task AddWithOutboxAsync(Order order, OutboxMessage outboxMessage, CancellationToken ct);

        Task<OrderReadModel?> GetAsync(Guid id, CancellationToken ct);

        Task<PagedResult<OrderSummaryDto>> ListAsync(Guid? customerId, OrderStatus? status, string sort, int page, int pageSize, CancellationToken ct);

        Task<Order?> GetForUpdateAsync(Guid id, CancellationToken ct);

        Task SaveStatusChangeAsync(Order order, IdempotencyRecord idempotencyRecord, CancellationToken ct);
    }
}
