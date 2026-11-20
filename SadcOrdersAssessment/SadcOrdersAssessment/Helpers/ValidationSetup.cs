using FluentValidation;
using Microsoft.Extensions.Options;
using SadcOrdersAssessment.Middleware;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SadcOrdersAssessment.Helpers
{
    internal static class ValidationSetup
    {
        /// <summary>
        /// Report error keys in the same camelCase as the JSON contract ("lineItems[0].unitPrice" rather
        /// than "LineItems[0].UnitPrice"), so the frontend can map errors straight onto form fields.
        /// A module initializer makes this apply everywhere the assembly loads, including unit tests.
        /// </summary>
        [ModuleInitializer]
        internal static void Configure()
        {
            ValidatorOptions.Global.PropertyNameResolver = (_, member, _) =>
                member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);
        }
    }

    /// <summary>
    /// Runs the FluentValidation validator for the endpoint argument of type <typeparamref name="T"/>
    /// before the handler. Invalid input never reaches the handler and comes back as a 400 with
    /// field-level errors. A missing validator is a startup bug, so it throws rather than skipping.
    /// </summary>
    public sealed class ValidationFilter<T> : IEndpointFilter
        where T : class
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var validator = http.RequestServices.GetRequiredService<IValidator<T>>();
            var argument = context.Arguments.OfType<T>().FirstOrDefault();
            if (argument is null)
            {
                return TypedResults.ValidationProblem(
                    new Dictionary<string, string[]> { [string.Empty] = new[] { "A request body is required." } },
                    title: "One or more validation errors occurred.",
                    instance: http.Request.Path,
                    extensions: Extensions(http));
            }

            var result = await validator.ValidateAsync(argument, http.RequestAborted);
            if (!result.IsValid)
            {
                return TypedResults.ValidationProblem(
                    result.ToDictionary(),
                    title: "One or more validation errors occurred.",
                    instance: http.Request.Path,
                    extensions: Extensions(http));
            }

            return await next(context);
        }

        // Set here as well as in CustomizeProblemDetails, so validation errors have the same shape as
        // every other error even when this result is written without IProblemDetailsService.
        private static Dictionary<string, object?> Extensions(HttpContext http) => new()
        {
            ["code"] = "validation_failed",
            ["traceId"] = http.TraceIdentifier,
            ["correlationId"] = http.Items[CorrelationIdMiddleware.ItemKey] as string,
        };
    }

    public static class ValidationFilterExtensions
    {
        public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder)
            where T : class =>
            builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
    }
}