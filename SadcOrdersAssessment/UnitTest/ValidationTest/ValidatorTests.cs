using Core.DTOs;
using Domain.Common;
using SadcOrdersAssessment.Helpers;

namespace UnitTest.ValidationTests;

public class ValidatorTests
{
    [Fact]
    public void Customer_validator_requires_an_SADC_country_and_valid_email()
    {
        var result = new CreateCustomerRequestValidator().Validate(new CreateCustomerRequest("Thabo", "not-an-email", "KE"));

        result.IsValid.Should().BeFalse();
        result.ToDictionary().Keys.Should().BeEquivalentTo("email", "countryCode");
    }

    [Fact]
    public void Customer_validator_accepts_a_valid_request()
    {
        new CreateCustomerRequestValidator()
            .Validate(new CreateCustomerRequest("Naledi Dlamini", "naledi@example.co.za", "za"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Order_validator_reports_camelCase_paths_for_line_items()
    {
        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            "ZAR",
            new[]
            {
                new CreateOrderLineRequest("OK-1", 1, 10m),
                new CreateOrderLineRequest("", 0, -5m),
            });

        var errors = new CreateOrderRequestValidator().Validate(request).ToDictionary();

        errors.Keys.Should().BeEquivalentTo("lineItems[1].productSku", "lineItems[1].quantity", "lineItems[1].unitPrice");
    }

    [Fact]
    public void Order_validator_requires_customer_currency_and_lines()
    {
        var errors = new CreateOrderRequestValidator().Validate(new CreateOrderRequest(null, "ZA", null)).ToDictionary();

        errors.Keys.Should().BeEquivalentTo("customerId", "currencyCode", "lineItems");
    }

    [Fact]
    public void Order_validator_rejects_more_than_two_decimal_places()
    {
        var request = new CreateOrderRequest(Guid.NewGuid(), "ZAR", new[] { new CreateOrderLineRequest("SKU", 1, 9.999m) });

        new CreateOrderRequestValidator().Validate(request).ToDictionary().Keys.Should().Contain("lineItems[0].unitPrice");
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(0, false)]
    public void Order_list_enforces_page_size_limit(int pageSize, bool valid)
    {
        new OrderListQueryValidator().Validate(new OrderListQuery { PageSize = pageSize }).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("-createdAt", true)]
    [InlineData("totalAmount", true)]
    [InlineData("name", false)]
    public void Order_list_validates_sort(string sort, bool valid)
    {
        new OrderListQueryValidator().Validate(new OrderListQuery { Sort = sort }).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("paid", true)]
    [InlineData("Fulfilled", true)]
    [InlineData("1", false)]
    [InlineData("Shipped", false)]
    [InlineData("", false)]
    public void Status_parser_accepts_names_only(string value, bool valid)
    {
        StatusParser.TryParser(value, out _).Should().Be(valid);
    }
}
