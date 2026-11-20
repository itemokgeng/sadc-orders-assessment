using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using SadcOrders.Services;
using SadcOrdersAssessment.Helpers;

namespace SadcOrdersAssessment.Services
{
    public static class AuthSetup
    {
        public const string ModeEntra = "Entra";
        public const string ModeMock = "Mock";

        public static bool IsMockMode(IConfiguration configuration) =>
            string.Equals(configuration["Auth:Mode"] ?? ModeMock, ModeMock, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Auth:Mode = "Entra" validates real Microsoft Entra ID tokens with Microsoft.Identity.Web
        /// (signature via OIDC metadata, issuer, audience and lifetime). Auth:Mode = "Mock" validates
        /// locally signed tokens with the same claim shape. Mock mode refuses to start in Production.
        /// </summary>
        public static IServiceCollection AddApiAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            if (IsMockMode(configuration))
            {
                if (environment.IsProduction())
                {
                    throw new InvalidOperationException("Auth:Mode 'Mock' is not allowed in Production. Configure Auth:Mode 'Entra' and the AzureAd section.");
                }

                var section = configuration.GetSection(MockAuthOptions.SectionName);
                var mock = section.Get<MockAuthOptions>() ?? new MockAuthOptions();

                services.Configure<MockAuthOptions>(section);
                services.TryAddSingleton(TimeProvider.System);
                services.AddSingleton<MockTokenIssuer>();

                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.MapInboundClaims = false;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = mock.Issuer,
                            ValidateAudience = true,
                            ValidAudience = mock.Audience,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = mock.CreateKey(),
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromSeconds(30),
                            NameClaimType = "name",
                            RoleClaimType = "roles",
                        };
                    });
            }
            else
            {
                // AzureAd section: Instance, TenantId, ClientId, Audience. Microsoft.Identity.Web sets
                // RoleClaimType = "roles", so RequireRole below works with Entra app roles.
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
            }

            services.AddAuthorizationBuilder()
                .AddPolicy(AuthPolicies.OrdersRead, p => p.RequireRole(AuthPolicies.OrdersRead, AuthPolicies.OrdersWrite, AuthPolicies.OrdersAdmin))
                .AddPolicy(AuthPolicies.OrdersWrite, p => p.RequireRole(AuthPolicies.OrdersWrite, AuthPolicies.OrdersAdmin))
                .AddPolicy(AuthPolicies.OrdersAdmin, p => p.RequireRole(AuthPolicies.OrdersAdmin))
                // Secure by default: an endpoint with no policy still needs an authenticated caller.
                // Public endpoints must opt out explicitly with AllowAnonymous().
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

            return services;
        }
    }
}