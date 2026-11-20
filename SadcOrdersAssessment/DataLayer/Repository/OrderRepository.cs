using Core.DTOs;
using Core.Exceptions;
using Core.Interface;
using DataLayer.Context;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Repository;

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrdersDbContext _db;

    public OrderRepository(OrdersDbContext db)
    {
        _db = db;
    }

    public async Task AddWithOutboxAsync(Order order, OutboxMessage outboxMessage, CancellationToken ct)
    {
        // One SaveChanges is one transaction: the order never exists without its event.
        _db.Orders.Add(order);
        _db.OutboxMessages.Add(outboxMessage);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OrderReadModel?> GetAsync(Guid id, CancellationToken ct)
    {
        // One SQL query: a projection with a nested collection becomes a single JOIN, so there is
        // no N+1 and no change tracking.
        var row = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                CustomerName = o.Customer!.Name,
                o.Status,
                o.CreatedAt,
                o.UpdatedAt,
                o.CurrencyCode,
                o.TotalAmount,
                o.RowVersion,
                LineItems = o.LineItems
                    .Select(li => new OrderLineItemDto(li.Id, li.ProductSku, li.Quantity, li.UnitPrice, li.Quantity * li.UnitPrice))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new OrderReadModel(
                row.Id,
                row.CustomerId,
                row.CustomerName,
                row.Status,
                row.CreatedAt,
                row.UpdatedAt,
                row.CurrencyCode,
                row.TotalAmount,
                row.LineItems.OrderBy(li => li.ProductSku, StringComparer.Ordinal).ToList(),
                row.RowVersion);
    }

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(Guid? customerId, OrderStatus? status, string sort, int page, int pageSize, CancellationToken ct)
    {
        var orders = _db.Orders.AsNoTracking();
        if (customerId is { } cid)
        {
            orders = orders.Where(o => o.CustomerId == cid);
        }

        if (status is { } s)
        {
            orders = orders.Where(o => o.Status == s);
        }

        var total = await orders.CountAsync(ct);

        // Order by Id last so rows with equal sort values page in a stable order.
        orders = sort switch
        {
            OrderSort.CreatedAtAsc => orders.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id),
            OrderSort.TotalAmountAsc => orders.OrderBy(o => o.TotalAmount).ThenBy(o => o.Id),
            OrderSort.TotalAmountDesc => orders.OrderByDescending(o => o.TotalAmount).ThenBy(o => o.Id),
            _ => orders.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id),
        };

        var rows = await orders
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                CustomerName = o.Customer!.Name,
                o.Status,
                o.CreatedAt,
                o.CurrencyCode,
                o.TotalAmount,
                LineItemCount = o.LineItems.Count,
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new OrderSummaryDto(r.Id, r.CustomerId, r.CustomerName, r.Status.ToString(), r.CreatedAt, r.CurrencyCode, r.TotalAmount, r.LineItemCount))
            .ToList();

        return new PagedResult<OrderSummaryDto>(items, page, pageSize, total);
    }

    public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
        _db.Orders
            .Include(o => o.LineItems)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task SaveStatusChangeAsync(Order order, IdempotencyRecord idempotencyRecord, CancellationToken ct)
    {
        // The order is already tracked (GetForUpdateAsync), so only the new record needs adding.
        // Both are saved in one transaction.
        _db.IdempotencyRecords.Add(idempotencyRecord);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // RowVersion no longer matched: someone else changed the order after it was read.
            _db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex)
        {
            // Usually a primary-key clash on IdempotencyRecords from a concurrent request with the same key.
            _db.ChangeTracker.Clear();
            throw new WriteConflictException(ex);
        }
    }
}
