using Core.DTOs;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface ICustomerRepository
    {
        Task<bool> EmailExistsAsync(string email, CancellationToken ct);

        Task<CustomerDto?> GetAsync(Guid id, CancellationToken ct);

        Task<CustomerSummary?> GetSummaryAsync(Guid id, CancellationToken ct);

        Task<PagedResult<CustomerDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct);

        Task AddAsync(Customer customer, CancellationToken ct);
    }
}
