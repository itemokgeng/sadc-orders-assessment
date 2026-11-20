using Core.Interface;
using LogicLayer.RepositoryService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogicLayer
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddLogicLayer(this IServiceCollection services, IConfiguration configuration)
        {
            IServiceCollection serviceCollection = services.Configure<SadcOptions>(configuration.GetSection(SadcOptions.SectionName));
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<IOrderService, OrderService>();
            return services;
        }
    }
}
