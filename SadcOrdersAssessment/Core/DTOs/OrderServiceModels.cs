using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain.Enums;

namespace Core.DTOs
{
    public sealed record OrderWithETag(OrderDto Order, string ETag);

    public sealed record UpdateOrderStatusCommand(
        Guid OrderId,
        string Status,
        string IdempotencyKey,
        string? IfMatch,
        bool CallerIsAdmin);

    /// <param name="ResponseBody">The serialised OrderDto. A replay returns exactly the same bytes.</param>
    public sealed record UpdateOrderStatusResult(int StatusCode, string ResponseBody, string? ETag, bool Replayed);

    /// <summary>Read model for one order, projected by the repository in a single query.</summary>
    public sealed record OrderReadModel(
        Guid Id,
        Guid CustomerId,
        string CustomerName,
        OrderStatus Status,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        string CurrencyCode,
        decimal TotalAmount,
        IReadOnlyList<OrderLineItemDto> LineItems,
        byte[] RowVersion);

    /// <summary>The fields of a customer that order creation needs.</summary>
    public sealed record CustomerSummary(Guid Id, string Name, string CountryCode);
}
