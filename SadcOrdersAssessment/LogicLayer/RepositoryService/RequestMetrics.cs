using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogicLayer.RepositoryService
{
    public sealed class RequestMetrics
    {
        public const string MeterName = "SadcOrders";

        private readonly Counter<long> _requests;
        private readonly Histogram<double> _duration;
        private readonly ConcurrentDictionary<string, RouteStats> _routes = new();

        public RequestMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(MeterName);
            _requests = meter.CreateCounter<long>("sadc_orders.http.requests", unit: "{request}", description: "HTTP requests handled");
            _duration = meter.CreateHistogram<double>("sadc_orders.http.request.duration", unit: "ms", description: "HTTP request duration");
        }

        public void Record(string method, string route, int statusCode, double elapsedMs)
        {
            var tags = new TagList
        {
            { "http.method", method },
            { "http.route", route },
            { "http.status_code", statusCode },
        };
            _requests.Add(1, tags);
            _duration.Record(elapsedMs, tags);

            _routes.GetOrAdd($"{method} {route}", _ => new RouteStats()).Record(statusCode, elapsedMs);
        }

        public object Snapshot() => new
        {
            generatedAt = DateTime.UtcNow,
            totalRequests = _routes.Values.Sum(r => r.Count),
            routes = _routes
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .ToDictionary(r => r.Key, r => r.Value.ToSummary()),
        };

        private sealed class RouteStats
        {
            private readonly object _sync = new();
            private long _count;
            private long _clientErrors;
            private long _serverErrors;
            private double _totalMs;
            private double _maxMs;

            public long Count
            {
                get
                {
                    lock (_sync)
                    {
                        return _count;
                    }
                }
            }

            public void Record(int statusCode, double elapsedMs)
            {
                lock (_sync)
                {
                    _count++;
                    _totalMs += elapsedMs;
                    _maxMs = Math.Max(_maxMs, elapsedMs);
                    if (statusCode >= 500)
                    {
                        _serverErrors++;
                    }
                    else if (statusCode >= 400)
                    {
                        _clientErrors++;
                    }
                }
            }

            public object ToSummary()
            {
                lock (_sync)
                {
                    return new
                    {
                        count = _count,
                        clientErrors = _clientErrors,
                        serverErrors = _serverErrors,
                        avgMs = _count == 0 ? 0 : Math.Round(_totalMs / _count, 2),
                        maxMs = Math.Round(_maxMs, 2),
                    };
                }
            }
        }
    }
}
