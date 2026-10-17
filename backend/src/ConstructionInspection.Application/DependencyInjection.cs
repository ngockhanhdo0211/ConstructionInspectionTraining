using ConstructionInspection.Application.Common.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Application.FormTemplates.Validation;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Validation;

namespace ConstructionInspection.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddSingleton<IFormIoSchemaValidator, FormIoSchemaValidator>();
        services.AddSingleton<IFormIoDataValidator, FormIoDataValidator>();

        return services;
    }
}
