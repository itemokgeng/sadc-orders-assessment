using Core.DTOs;
using Core.Exceptions;
using Core.Interface;
using DataLayer.Context;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Repository;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly OrdersDbContext _db;

    public CustomerRepository(OrdersDbContext db)
    {
        _db = db;
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        _db.Customers.AsNoTracking().AnyAsync(c => c.Email == email, ct);

    public Task<CustomerDto?> GetAsync(Guid id, CancellationToken ct) =>
        _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Email, c.CountryCode, c.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public Task<CustomerSummary?> GetSummaryAsync(Guid id, CancellationToken ct) =>
        _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerSummary(c.Id, c.Name, c.CountryCode))
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<CustomerDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var customers = _db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Contains becomes LIKE '%term%', which can't seek an index. That is fine for a customer
            // table of this size. At scale use a prefix search or full-text index (see ANSWERS.md, Q6).
            var term = search.Trim();
            customers = customers.Where(c => c.Name.Contains(term) || c.Email.Contains(term));
        }

        var total = await customers.CountAsync(ct);
        var items = await customers
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Email, c.CountryCode, c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<CustomerDto>(items, page, pageSize, total);
    }

    public async Task AddAsync(Customer customer, CancellationToken ct)
    {
        _db.Customers.Add(customer);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            throw new WriteConflictException(ex);
        }
    }
}
