using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public static class OrderStatusTransitions
    {
        private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed = new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = new[] { OrderStatus.Paid, OrderStatus.Cancelled },
            [OrderStatus.Paid] = new[] { OrderStatus.Fulfilled, OrderStatus.Cancelled },
            [OrderStatus.Fulfilled] = Array.Empty<OrderStatus>(),
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>(),
        };

        public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from) => Allowed[from];
        public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);
        public static bool IsTerminal(OrderStatus status) => Allowed[status].Length == 0;
    }
}
