using Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public sealed class InvalidStatusTransitionException : Exception
    {
        public InvalidStatusTransitionException(OrderStatus current, OrderStatus requested) : base
            ($"Invalid status transition from {current} to {requested}")
        {
            Current = current;
            Requested = requested;
        }

        public OrderStatus Current { get; }
        public OrderStatus Requested { get; }
        public IReadOnlyList<OrderStatus> Allowed => OrderStatusTransitions.NextStatuses(Current);
    }
}
