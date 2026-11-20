using Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface ICustomerService
    {
        Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct);

        Task<CustomerDto> GetAsync(Guid id, CancellationToken ct);

        Task<PagedResult<CustomerDto>> ListAsync(CustomerListQuery query, CancellationToken ct);
    }
}
