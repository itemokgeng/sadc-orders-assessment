using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public sealed record NewOrderLine(string ProductSku, int Quantity, decimal UnitPrice)
    {
    }
}
