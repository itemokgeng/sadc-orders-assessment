using Core.DTOs;
using Domain.Common;
using LogicLayer.RepositoryService;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SadcOrders.Services;
using SadcOrdersAssessment.Helpers;
using SadcOrdersAssessment.Services;
using System.Xml.Linq;

namespace SadcOrdersAssessment.Controllers
{
    public static class SystemEndpoints
    {
        public const string ReadyTag = "ready";

        public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app, bool mockAuth)
        {
            // Liveness: the process is up. It runs no dependency checks, so a database outage doesn't
            // make the orchestrator restart healthy pods.
            app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

            // Readiness: SQL Server and RabbitMQ are reachable, so the instance can take traffic.
            app.MapHealthChecks("/readiness", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();

            // In production, restrict this at the network or gateway, or replace it with an OpenTelemetry/Prometheus exporter.
            app.MapGet("/metrics", (RequestMetrics metrics) => TypedResults.Ok(metrics.Snapshot()))
                .AllowAnonymous()
                .ExcludeFromDescription();

            app.MapGet("/api/reference/sadc-countries", GetCountries)
                .AllowAnonymous()
                .WithTags("Reference")
                .WithName("ListSadcCountries")
                .WithSummary("SADC member states and the ISO 4217 currencies allowed for each, under the current CMA setting.");

            if (mockAuth)
            {
                app.MapPost("/api/dev/token", IssueDevToken)
                    .AllowAnonymous()
                    .WithTags("Dev auth (mock mode only)")
                    .WithName("IssueDevToken")
                    .WithSummary("Issue a locally signed JWT with the requested roles. Only exists when Auth:Mode is Mock.")
                    .ProducesValidationProblem();
            }

            return app;
        }

        private static Ok<IReadOnlyList<SadcCountryDto>> GetCountries(IOptions<SadcOptions> options)
        {
            var allowRand = options.Value.AllowRandInCommonMonetaryArea;
            IReadOnlyList<SadcCountryDto> countries = SadcCurrencyRules.GetCountries(allowRand)
                .Select(c => new SadcCountryDto(c.Code, c.Name, c.Currency, SadcCurrencyRules.IsCommonMonetaryAreaCountry(c.Code)))
                .ToList();
            return TypedResults.Ok(countries);
        }

        private static Results<Ok<DevTokenResponse>, ValidationProblem> IssueDevToken(DevTokenRequest? request, MockTokenIssuer issuer)
        {
            var roles = request?.Roles is { Count: > 0 } requested
                ? requested.Distinct(StringComparer.Ordinal).ToList()
                : new List<string> { AuthPolicies.OrdersRead, AuthPolicies.OrdersWrite };

            var unknown = roles.Except(AuthPolicies.AllRoles, StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["roles"] = new[] { $"Unknown role(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", AuthPolicies.AllRoles)}." },
                });
            }

            var (token, expiresAt) = issuer.Issue(roles);
            return TypedResults.Ok(new DevTokenResponse(token, expiresAt, roles));
        }
    }

}