using Microsoft.OpenApi;

namespace TaskManager.Api.Extensions;

public static class SwaggerExtensions
{
    // Swashbuckle 10 uses Microsoft.OpenApi 2.x (namespace Microsoft.OpenApi, scheme references by id).
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = "Task Manager API", Version = "v1" });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the token only (without the word Bearer)."
            });
            o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", doc)] = []
            });
        });
        return services;
    }
}
