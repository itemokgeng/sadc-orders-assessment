using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Exceptions
{
    public sealed class RequestValidationException : Exception
    {
        public RequestValidationException(string field, string message)
            : this(new Dictionary<string, string[]> { [field] = new[] { message } })
        {
        }

        public RequestValidationException(IDictionary<string, string[]> errors)
            : base("One or more validation errors occurred.")
        {
            Errors = errors;
        }

        public IDictionary<string, string[]> Errors { get; }
    }

    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string resource, object id)
            : base($"{resource} '{id}' was not found.")
        {
        }
    }

    public sealed class ConflictException : Exception
    {
        public ConflictException(string code, string message, string? field = null, object? current = null)
            : base(message)
        {
            Code = code;
            Field = field;
            Current = current;
        }

        public string Code { get; }

        public string? Field { get; }

        public object? Current { get; }
    }

    public sealed class IdempotencyKeyReuseException : Exception
    {
        public IdempotencyKeyReuseException()
            : base("This Idempotency-Key was already used with a different request. Use a new key for a new request.")
        {
        }
    }

    public sealed class PreconditionFailedException : Exception
    {
        public PreconditionFailedException(string message) : base(message)
        {
        }
    }

    public sealed class ForbiddenException : Exception
    {
        public ForbiddenException(string message) : base(message)
        {
        }
    }

    public sealed class ConcurrencyConflictException : Exception
    {
        public ConcurrencyConflictException(Exception? inner = null)
            : base("The record was changed by another process after it was read.", inner)
        {
        }
    }
    public sealed class WriteConflictException : Exception
    {
        public WriteConflictException(Exception inner)
            : base("The database rejected the write because of a constraint.", inner)
        {
        }
    }
}
