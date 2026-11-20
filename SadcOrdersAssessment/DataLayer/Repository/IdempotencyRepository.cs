using Core.Interface;
using DataLayer.Context;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Repository;

public sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly OrdersDbContext _db;

    public IdempotencyRepository(OrdersDbContext db)
    {
        _db = db;
    }

    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct) =>
        _db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
}
