using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Enums
{
    public enum OrderStatus
    {
        // Stored as text in the database (HasConversion<string>), so the order here only matters in code.
        // Pending is first so default(OrderStatus) is Pending, not Paid.
        Pending,
        Paid,
        Fulfilled,
        Cancelled,
    }
}
