using Core.DTOs;
using Core.Interface;
using Microsoft.AspNetCore.Http.HttpResults;
using SadcOrdersAssessment.Helpers;

namespace SadcOrdersAssessment.Controllers
{
    public static class CustomerEndpoints
    {
        public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/customers").WithTags("Customers");

            group.MapPost(string.Empty, CreateAsync)
                .RequireAuthorization(AuthPolicies.OrdersWrite)
                .Validate<CreateCustomerRequest>()
                .WithName("CreateCustomer")
                .WithSummary("Create a customer in an SADC member state.")
                .ProducesProblem(StatusCodes.Status409Conflict);

            group.MapGet("/{id:guid}", GetAsync)
                .RequireAuthorization(AuthPolicies.OrdersRead)
                .WithName("GetCustomer")
                .ProducesProblem(StatusCodes.Status404NotFound);

            group.MapGet(string.Empty, ListAsync)
                .RequireAuthorization(AuthPolicies.OrdersRead)
                .Validate<CustomerListQuery>()
                .WithName("ListCustomers")
                .WithSummary("Search customers by name or email (pageSize ≤ 100).");

            return app;
        }

        private static async Task<Created<CustomerDto>> CreateAsync(CreateCustomerRequest request, ICustomerService customers, CancellationToken ct)
        {
            var customer = await customers.CreateAsync(request, ct);
            return TypedResults.Created($"/api/customers/{customer.Id}", customer);
        }

        private static async Task<Ok<CustomerDto>> GetAsync(Guid id, ICustomerService customers, CancellationToken ct) =>
            TypedResults.Ok(await customers.GetAsync(id, ct));

        private static async Task<Ok<PagedResult<CustomerDto>>> ListAsync([AsParameters] CustomerListQuery query, ICustomerService customers, CancellationToken ct) =>
            TypedResults.Ok(await customers.ListAsync(query, ct));
    }
}