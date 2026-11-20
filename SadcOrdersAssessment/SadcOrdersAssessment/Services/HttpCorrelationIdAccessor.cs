using Core.Interface;
using SadcOrdersAssessment.Middleware;

namespace SadcOrdersAssessment.Services
{
    public sealed class HttpCorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string? CorrelationId =>
            _httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;
    }
}