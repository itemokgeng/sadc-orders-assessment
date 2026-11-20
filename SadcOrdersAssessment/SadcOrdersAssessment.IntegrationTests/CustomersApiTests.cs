using System.Net;
using System.Net.Http.Json;
using Core.DTOs;
using SadcOrders.IntegrationTests.Infrastructure;

namespace SadcOrdersAssessment.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CustomersApiTests
{
    private readonly ApiFactory _factory;

    public CustomersApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_normalised_fields()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var response = await _factory.Writer().PostAsJsonAsync("/api/customers", new CreateCustomerRequest($"  Naledi {unique} ", $"Naledi.{unique}@Example.CO.ZA", "bw"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await response.Content.ReadFromJsonAsync<CustomerDto>();
        customer!.Name.Should().Be($"Naledi {unique}");
        customer.Email.Should().Be($"naledi.{unique}@example.co.za");
        customer.CountryCode.Should().Be("BW");
        response.Headers.Location!.ToString().Should().Be($"/api/customers/{customer.Id}");
        response.Headers.GetValues("X-Correlation-ID").Should().ContainSingle();

        var fetched = await _factory.Reader().GetFromJsonAsync<CustomerDto>($"/api/customers/{customer.Id}");
        fetched.Should().BeEquivalentTo(customer);
    }

    [Fact]
    public async Task Create_rejects_non_SADC_country_with_field_level_error()
    {
        var response = await _factory.Writer().PostAsJsonAsync("/api/customers", new CreateCustomerRequest("Amara", "amara@example.test", "KE"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await TestData.ProblemAsync(response);
        TestData.ErrorKeys(problem).Should().BeEquivalentTo("countryCode");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Create_rejects_duplicate_email_with_409_on_the_email_field()
    {
        var client = _factory.Writer();
        var existing = await TestData.CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest("Someone Else", existing.Email.ToUpperInvariant(), "ZA"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await TestData.ProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("duplicate_email");
        TestData.ErrorKeys(problem).Should().BeEquivalentTo("email");
    }

    [Fact]
    public async Task Get_unknown_customer_returns_404_problem_details()
    {
        var response = await _factory.Reader().GetAsync($"/api/customers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TestData.ProblemAsync(response)).GetProperty("code").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task List_searches_and_returns_pagination_metadata()
    {
        var client = _factory.Writer();
        var tag = $"Srch{Guid.NewGuid():N}"[..14];
        for (var i = 0; i < 3; i++)
        {
            await TestData.CreateCustomerAsync(client, name: $"{tag} {i}");
        }

        var page = await _factory.Reader().GetFromJsonAsync<PagedResult<CustomerDto>>($"/api/customers?search={tag}&page=1&pageSize=2");

        page!.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.TotalPages.Should().Be(2);
        page.Items.Select(c => c.Name).Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task List_enforces_paging_limits(string query, string field)
    {
        var response = await _factory.Reader().GetAsync($"/api/customers?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        TestData.ErrorKeys(await TestData.ProblemAsync(response)).Should().Contain(field);
    }

    [Fact]
    public async Task Requires_a_token_and_the_write_role_to_create()
    {
        var body = new CreateCustomerRequest("No Auth", "noauth@example.test", "ZA");

        (await _factory.CreateClient().PostAsJsonAsync("/api/customers", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _factory.Reader().PostAsJsonAsync("/api/customers", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
