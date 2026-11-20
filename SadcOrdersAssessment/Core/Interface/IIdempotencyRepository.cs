using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interface
{
    public interface IIdempotencyRepository
    {
        Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct);
    }
}
