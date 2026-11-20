using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public sealed record SadcCountry(string Code, string Name, IReadOnlyList<string> Currency)
    {
    }
}
