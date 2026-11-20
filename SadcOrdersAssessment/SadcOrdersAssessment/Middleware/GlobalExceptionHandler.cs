using Core.Exceptions;
using Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SadcOrdersAssessment.Middleware
{
    public sealed class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetails;
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
        {
            _problemDetails = problemDetails;
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var problem = Map(exception);
            var status = problem.Status ?? StatusCodes.Status500InternalServerError;

            if (status >= 500)
            {
                _logger.LogError(exception, "Unhandled exception");
            }
            else
            {
                _logger.LogInformation("Request failed with {StatusCode}: {Reason}", status, exception.Message);
            }

            problem.Instance = httpContext.Request.Path;
            httpContext.Response.StatusCode = status;
            return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            });
        }

        internal static ProblemDetails Map(Exception exception) => exception switch
        {
            RequestValidationException e => Validation(e.Errors),
            DomainRuleException e => Validation(new Dictionary<string, string[]> { [e.Field] = new[] { e.Message } }),
            BadHttpRequestException e => Problem(e.StatusCode, "Bad request", e.Message, "bad_request"),
            NotFoundException e => Problem(StatusCodes.Status404NotFound, "Not found", e.Message, "not_found"),
            InvalidStatusTransitionException e => WithExtensions(
                Problem(StatusCodes.Status409Conflict, "Invalid status transition", e.Message, "invalid_status_transition"),
                ("currentStatus", e.Current.ToString()),
                ("requestedStatus", e.Requested.ToString()),
                ("allowedTransitions", e.Allowed.Select(s => s.ToString()).ToArray())),
            ConflictException e => Conflict(e),
            ConcurrencyConflictException e => Problem(StatusCodes.Status409Conflict, "Conflict", e.Message, "concurrency_conflict"),
            IdempotencyKeyReuseException e => Problem(StatusCodes.Status422UnprocessableEntity, "Idempotency key reused", e.Message, "idempotency_key_reused"),
            PreconditionFailedException e => Problem(StatusCodes.Status412PreconditionFailed, "Precondition failed", e.Message, "precondition_failed"),
            ForbiddenException e => Problem(StatusCodes.Status403Forbidden, "Forbidden", e.Message, "forbidden"),
            OperationCanceledException => Problem(499, "Client closed request", "The request was cancelled.", "cancelled"),
            _ => Problem(StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred. Quote the traceId when reporting it.", "internal_error"),
        };

        private static ProblemDetails Validation(IDictionary<string, string[]> errors) =>
            new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Extensions = { ["code"] = "validation_failed" },
            };

        private static ProblemDetails Conflict(ConflictException e)
        {
            ProblemDetails problem = e.Field is null
                ? new ProblemDetails()
                : new ValidationProblemDetails(new Dictionary<string, string[]> { [e.Field] = new[] { e.Message } });

            problem.Status = StatusCodes.Status409Conflict;
            problem.Title = "Conflict";
            problem.Detail = e.Message;
            problem.Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
            problem.Extensions["code"] = e.Code;
            if (e.Current is not null)
            {
                problem.Extensions["current"] = e.Current;
            }

            return problem;
        }

        private static ProblemDetails Problem(int status, string title, string detail, string code) =>
            new()
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["code"] = code },
            };

        private static ProblemDetails WithExtensions(ProblemDetails problem, params (string Key, object Value)[] extensions)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }

            return problem;
        }
    }

}