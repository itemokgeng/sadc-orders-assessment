using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SadcOrdersAssessment.Middleware
{
    public sealed partial class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-ID";
        public const string ItemKey = "CorrelationId";

        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var incoming = context.Request.Headers[HeaderName].ToString();

            // Only accept short, plain values from clients; this stops log injection and huge headers.
            var correlationId = !string.IsNullOrEmpty(incoming) && SafeId().IsMatch(incoming)
                ? incoming
                : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

            context.Items[ItemKey] = correlationId;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            using (_logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
            {
                await _next(context);
            }
        }

        [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
        private static partial Regex SafeId();
    }
}