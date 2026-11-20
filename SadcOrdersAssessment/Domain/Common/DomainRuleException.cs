using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Common
{
    public class DomainRuleException : Exception
    {

        public DomainRuleException(string field, string message) : base(message)
        {
            Field = field;
        }
        public string Field { get; }
    }
}
