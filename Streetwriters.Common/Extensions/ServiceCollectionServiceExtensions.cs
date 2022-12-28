namespace Microsoft.Extensions.DependencyInjection.CorsServiceCollectionExtensions
{
    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("notesnook", (b) =>
                {
#if DEBUG
                    b.AllowAnyOrigin();
#else
                    b.WithOrigins("http://localhost:3000", "http://192.168.10.29:3000", "https://app.notesnook.com", "https://beta.notesnook.com", "https://budi.streetwriters.co", "http://localhost:9876");
#endif
                    b.AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
                });
            });
            return services;
        }
    }
}
