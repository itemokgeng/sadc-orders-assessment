using Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface IOrderService
    {
        Task<OrderWithETag> CreateAsync(CreateOrderRequest request, CancellationToken ct);

        Task<OrderWithETag> GetAsync(Guid id, CancellationToken ct);

        Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListQuery query, CancellationToken ct);

        Task<UpdateOrderStatusResult> UpdateStatusAsync(UpdateOrderStatusCommand command, CancellationToken ct);
    }
}
