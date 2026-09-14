using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Domain.ConstructionObjects;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Errors;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validationException => CreateValidationProblem(validationException),
            AuthenticationFailedException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication failed",
                Detail = "Invalid email or password."
            },
            ForbiddenAccessException => CreateProblem(
                StatusCodes.Status403Forbidden,
                "Access forbidden",
                exception.Message,
                "authorization.forbidden"),
            ResourceNotFoundException notFound => CreateProblem(
                StatusCodes.Status404NotFound,
                "Resource not found",
                notFound.Message,
                notFound.Code),
            ConflictException conflict => CreateProblem(
                StatusCodes.Status409Conflict,
                "Construction Object conflict",
                conflict.Message,
                conflict.Code),
            ConstructionObjectDomainException domain => CreateProblem(
                StatusCodes.Status409Conflict,
                "Construction Object conflict",
                domain.Message,
                domain.Code),
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = "The server could not complete the request."
            }
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for trace {TraceId}.", httpContext.TraceIdentifier);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails CreateProblem(
        int status,
        string title,
        string detail,
        string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    private static HttpValidationProblemDetails CreateValidationProblem(
        ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

        return new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed"
        };
    }
}
