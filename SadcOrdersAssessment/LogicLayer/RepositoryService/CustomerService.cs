using Core.DTOs;
using Core.Exceptions;
using Core.Interface;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace LogicLayer.RepositoryService
{
    public sealed class CustomerService : ICustomerService
    {
        private readonly ICustomerRepository _customers;
        private readonly TimeProvider _time;

        public CustomerService(ICustomerRepository customers, TimeProvider time)
        {
            _customers = customers;
            _time = time;
        }

        public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
        {
            // The validator has already checked these fields, so the null-forgiving operators are safe.
            var customer = Customer.Create(request.Name!, request.Email!, request.CountryCode!, _time.GetUtcNow().UtcDateTime);

            if (await _customers.EmailExistsAsync(customer.Email, ct))
            {
                throw DuplicateEmail();
            }

            try
            {
                await _customers.AddAsync(customer, ct);
            }
            catch (WriteConflictException)
            {
                // Two requests with the same email can both pass the check above. The unique index
                // stops the second one; give it the same 409 instead of a 500.
                if (await _customers.EmailExistsAsync(customer.Email, ct))
                {
                    throw DuplicateEmail();
                }

                throw;
            }

            return new CustomerDto(customer.Id, customer.Name, customer.Email, customer.CountryCode, customer.CreatedAt);
        }

        public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct) =>
            await _customers.GetAsync(id, ct) ?? throw new NotFoundException("Customer", id);

        public Task<PagedResult<CustomerDto>> ListAsync(CustomerListQuery query, CancellationToken ct) =>
            _customers.ListAsync(query.Search, query.Page ?? 1, query.PageSize ?? Paging.DefaultPageSize, ct);

        private static ConflictException DuplicateEmail() =>
            new("duplicate_email", "A customer with this email already exists.", field: "email");
    }

}
