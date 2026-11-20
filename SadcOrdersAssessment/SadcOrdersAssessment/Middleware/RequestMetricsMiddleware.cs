using LogicLayer.RepositoryService;
using System.Diagnostics;

namespace SadcOrdersAssessment.Middleware
{
    public sealed class RequestMetricsMiddleware
    {
        private const string RouteItemKey = "__MetricsRoute";

        private readonly RequestDelegate _next;
        private readonly RequestMetrics _metrics;

        public RequestMetricsMiddleware(RequestDelegate next, RequestMetrics metrics)
        {
            _next = next;
            _metrics = metrics;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await _next(context);
            }
            finally
            {
                // Use the route template, not the raw path, so IDs don't create one series per order.
                var route = context.Items[RouteItemKey] as string
                    ?? (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                    ?? "unmatched";
                _metrics.Record(context.Request.Method, route, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }

        /// <summary>Middleware to register after UseRouting.</summary>
        public static Task CaptureRoute(HttpContext context, RequestDelegate next)
        {
            if (context.GetEndpoint() is RouteEndpoint endpoint)
            {
                context.Items[RouteItemKey] = endpoint.RoutePattern.RawText;
            }

            return next(context);
        }
    }
}