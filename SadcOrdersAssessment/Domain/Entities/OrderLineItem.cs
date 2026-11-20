using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class OrderLineItem
    {
        public const int ProductSkuMaxLength = 64;

        private OrderLineItem()
        {
        }

        internal OrderLineItem(Guid orderId, string productSku, int quantity, decimal unitPrice)
        {
            Id = Guid.NewGuid();
            OrderId = orderId;
            ProductSku = productSku;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public Guid Id { get; private set; }
        public Guid OrderId { get; private set; }
        public string ProductSku { get; private set; } = string.Empty;
        public int Quantity { get; private set; }
        public decimal UnitPrice { get; private set; }
        public decimal LineTotal => Quantity * UnitPrice;
    }
}
