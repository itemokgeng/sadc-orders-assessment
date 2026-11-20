using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace UnitTest.DomainTests;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);

    private static Order NewOrder(params NewOrderLine[] lines) =>
        Order.Create(Guid.NewGuid(), "zar", lines.Length == 0 ? new[] { new NewOrderLine("SKU-1", 1, 10m) } : lines, Now);

    [Fact]
    public void Total_is_the_sum_of_quantity_times_unit_price()
    {
        var order = NewOrder(
            new NewOrderLine("MAIZE-25KG", 3, 289.99m),
            new NewOrderLine("SEED-PACK", 10, 59.99m),
            new NewOrderLine("FREEBIE", 1, 0m));

        order.TotalAmount.Should().Be(3 * 289.99m + 10 * 59.99m);
        order.TotalAmount.Should().Be(1469.87m);
    }

    [Fact]
    public void New_orders_are_pending_with_normalised_codes()
    {
        var order = NewOrder(new NewOrderLine(" sku-a ", 1, 1m));

        order.Status.Should().Be(OrderStatus.Pending);
        order.CurrencyCode.Should().Be("ZAR");
        order.LineItems.Single().ProductSku.Should().Be("SKU-A");
        order.LineItems.Single().OrderId.Should().Be(order.Id);
        order.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(0, 10, "lineItems[0].quantity")]
    [InlineData(-1, 10, "lineItems[0].quantity")]
    [InlineData(1, -0.01, "lineItems[0].unitPrice")]
    [InlineData(1, 1.005, "lineItems[0].unitPrice")]
    public void Rejects_invalid_lines_with_the_offending_field(int quantity, double unitPrice, string expectedField)
    {
        var act = () => NewOrder(new NewOrderLine("SKU", quantity, (decimal)unitPrice));

        act.Should().Throw<DomainRuleException>().Which.Field.Should().Be(expectedField);
    }

    [Fact]
    public void Rejects_orders_without_lines()
    {
        var act = () => Order.Create(Guid.NewGuid(), "ZAR", Array.Empty<NewOrderLine>(), Now);

        act.Should().Throw<DomainRuleException>().Which.Field.Should().Be("lineItems");
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Paid)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Paid, OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled)]
    public void Allows_valid_transitions(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransitions.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid, OrderStatus.Pending)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid)]
    public void Rejects_invalid_transitions(OrderStatus from, OrderStatus to)
    {
        OrderStatusTransitions.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void TransitionTo_updates_status_and_timestamp()
    {
        var order = NewOrder();
        var later = Now.AddMinutes(5);

        order.TransitionTo(OrderStatus.Paid, later);

        order.Status.Should().Be(OrderStatus.Paid);
        order.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void TransitionTo_throws_and_leaves_state_unchanged_for_invalid_transition()
    {
        var order = NewOrder();

        var act = () => order.TransitionTo(OrderStatus.Fulfilled, Now);

        var ex = act.Should().Throw<InvalidStatusTransitionException>().Which;
        ex.Current.Should().Be(OrderStatus.Pending);
        ex.Allowed.Should().BeEquivalentTo(new[] { OrderStatus.Paid, OrderStatus.Cancelled });
        order.Status.Should().Be(OrderStatus.Pending);
        order.UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Cancelled)]
    public void Terminal_states_have_no_next_status(OrderStatus status)
    {
        OrderStatusTransitions.IsTerminal(status).Should().BeTrue();
    }
}
