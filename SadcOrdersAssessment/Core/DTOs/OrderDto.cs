using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.DTOs
{
    public sealed record CreateCustomerRequest(string? Name, string? Email, string? CountryCode);

    public sealed record CustomerDto(Guid Id, string Name, string Email, string CountryCode, DateTime CreatedAt);

    public sealed record CreateOrderLineRequest(string? ProductSku, int Quantity, decimal UnitPrice);

    public sealed record CreateOrderRequest(Guid? CustomerId, string? CurrencyCode, IReadOnlyList<CreateOrderLineRequest>? LineItems);

    public sealed record UpdateOrderStatusRequest(string? Status);

    public sealed record OrderLineItemDto(Guid Id, string ProductSku, int Quantity, decimal UnitPrice, decimal LineTotal);

    public sealed record OrderDto(
        Guid Id,
        Guid CustomerId,
        string CustomerName,
        string Status,
        DateTime CreatedAt,
        DateTime? UpdatedAt,
        string CurrencyCode,
        decimal TotalAmount,
        IReadOnlyList<OrderLineItemDto> LineItems,
        IReadOnlyList<string> AllowedTransitions);

    public sealed record OrderSummaryDto(
        Guid Id,
        Guid CustomerId,
        string CustomerName,
        string Status,
        DateTime CreatedAt,
        string CurrencyCode,
        decimal TotalAmount,
        int LineItemCount);

    public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
    {
        public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public sealed record SadcCountryDto(string Code, string Name, IReadOnlyList<string> Currencies, bool IsCommonMonetaryArea);

    public sealed record DevTokenRequest(IReadOnlyList<string>? Roles);

    public sealed record DevTokenResponse(string AccessToken, DateTime ExpiresAt, IReadOnlyList<string> Roles);

    public sealed class CustomerListQuery
    {
        public string? Search { get; set; }

        public int? Page { get; set; }

        public int? PageSize { get; set; }
    }

    public sealed class OrderListQuery
    {
        public Guid? CustomerId { get; set; }

        public string? Status { get; set; }

        public int? Page { get; set; }

        public int? PageSize { get; set; }

        public string? Sort { get; set; }
    }

    public static class Paging
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;
    }

    public static class OrderSort
    {
        public const string CreatedAtAsc = "createdAt";
        public const string CreatedAtDesc = "-createdAt";
        public const string TotalAmountAsc = "totalAmount";
        public const string TotalAmountDesc = "-totalAmount";
        public const string Default = CreatedAtDesc;

        public static readonly IReadOnlySet<string> All =
            new HashSet<string>(StringComparer.Ordinal) { CreatedAtAsc, CreatedAtDesc, TotalAmountAsc, TotalAmountDesc };
    }

}
