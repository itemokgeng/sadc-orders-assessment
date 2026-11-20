using Domain.Common;
using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Order
    {
        public const int MaxLineItems = 100;

        private readonly List<OrderLineItem> _lineItems = new();
        private Order()
        {
        }

        public Guid Id { get; private set; }
        public Guid CustomerId { get; private set; }
        public Customer? Customer { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;
        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }
        public string CurrencyCode { get; private set; } = string.Empty;
        public decimal TotalAmount { get; private set; }

        public IReadOnlyCollection<OrderLineItem> LineItems => _lineItems.AsReadOnly();

        
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        public static Order Create(Guid customerId, string currencyCode, IReadOnlyCollection<NewOrderLine> lines, DateTime createdAtUtc)
        {
            if (customerId == Guid.Empty)
            {
                throw new DomainRuleException("customerId", "Customer is required.");
            }

            if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            {
                throw new DomainRuleException("currencyCode", "Currency must be a 3-letter ISO 4217 code.");
            }

            if (lines.Count == 0)
            {
                throw new DomainRuleException("lineItems", "An order needs at least one line item.");
            }

            if (lines.Count > MaxLineItems)
            {
                throw new DomainRuleException("lineItems", $"An order can have at most {MaxLineItems} line items.");
            }

            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                CurrencyCode = SadcCurrencyRules.Normalize(currencyCode),
                CreatedAt = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
                Status = OrderStatus.Pending,
            };

            var index = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.ProductSku))
                {
                    throw new DomainRuleException($"lineItems[{index}].productSku", "Product SKU is required.");
                }

                if (line.Quantity <= 0)
                {
                    throw new DomainRuleException($"lineItems[{index}].quantity", "Quantity must be greater than 0.");
                }

                if (line.UnitPrice < 0)
                {
                    throw new DomainRuleException($"lineItems[{index}].unitPrice", "Unit price cannot be negative.");
                }

                if (decimal.Round(line.UnitPrice, 2) != line.UnitPrice)
                {
                    throw new DomainRuleException($"lineItems[{index}].unitPrice", "Unit price can have at most 2 decimal places.");
                }

                order._lineItems.Add(new OrderLineItem(order.Id, line.ProductSku.Trim().ToUpperInvariant(), line.Quantity, line.UnitPrice));
                index++;
            }

            order.TotalAmount = CalculateTotal(order._lineItems);
            return order;
        }
        public static decimal CalculateTotal(IEnumerable<OrderLineItem> lines) =>
            decimal.Round(lines.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);

        public void TransitionTo(OrderStatus next, DateTime nowUtc)
        {
            if (!OrderStatusTransitions.CanTransition(Status, next))
            {
                throw new InvalidStatusTransitionException(Status, next);
            }

            Status = next;
            UpdatedAt = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        }
    }
}
