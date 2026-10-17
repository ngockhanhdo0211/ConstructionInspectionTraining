using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.Drawings;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Api.Drawings;
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
            BadRequestException badRequest => CreateProblem(
                StatusCodes.Status400BadRequest,
                badRequest.Code.StartsWith("inspection.", StringComparison.Ordinal)
                    ? "Inspection validation failed"
                    : "Form Template validation failed",
                badRequest.Message,
                badRequest.Code),
            PayloadTooLargeException payload => CreateProblem(
                413,
                payload.Code.StartsWith("inspection.", StringComparison.Ordinal)
                    ? "Inspection data too large"
                    : "Upload too large",
                payload.Message,
                payload.Code),
            UnsupportedMediaTypeException => CreateProblem(415, "Unsupported PDF", exception.Message, "drawing.unsupported_media_type"),
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
                "Resource conflict",
                conflict.Message,
                conflict.Code),
            ConstructionObjectDomainException domain => CreateProblem(
                StatusCodes.Status409Conflict,
                "Construction Object conflict",
                domain.Message,
                domain.Code),
            DrawingDocumentDomainException drawing => CreateProblem(409, "Drawing conflict", drawing.Message, drawing.Code),
            DrawingObjectMappingDomainException mapping when mapping.Code is
                "drawing_mapping.invalid_geometry" or "drawing_mapping.invalid_page" or "drawing_mapping.invalid_shape_type"
                => CreateProblem(400, "Drawing mapping validation failed", mapping.Message, mapping.Code),
            DrawingObjectMappingDomainException mapping => CreateProblem(409, "Drawing mapping conflict", mapping.Message, mapping.Code),
            FormTemplateDomainException formTemplate when formTemplate.Code is
                "form_template.invalid_code" or "form_template.invalid_name" or
                "form_template.invalid_description" or "form_template.invalid_identity" or
                "form_template.invalid_actor" or "form_template.invalid_version_number" or
                "form_template.schema_invalid"
                => CreateProblem(400, "Form Template validation failed", formTemplate.Message, formTemplate.Code),
            FormTemplateDomainException formTemplate => CreateProblem(
                StatusCodes.Status409Conflict,
                "Form Template conflict",
                formTemplate.Message,
                formTemplate.Code),
            InspectionDomainException inspection when inspection.Code is
                "inspection.invalid_identity" or "inspection.invalid_number" or
                "inspection.invalid_title" or "inspection.invalid_description" or
                "inspection.invalid_submission" or "inspection.form_data_invalid"
                => CreateProblem(400, "Inspection validation failed", inspection.Message, inspection.Code),
            InspectionDomainException inspection => CreateProblem(
                StatusCodes.Status409Conflict,
                "Inspection conflict",
                inspection.Message,
                inspection.Code),
            RangeNotSatisfiableException range => CreateRangeProblem(httpContext, range),
            ObjectStorageException storage => CreateProblem(
                StatusCodes.Status502BadGateway,
                "Drawing storage unavailable",
                storage.Message,
                storage.Code),
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = "The server could not complete the request."
            }
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (exception is ObjectStorageException storageException)
        {
            logger.LogWarning(
                "Drawing storage request failed. Code={Code}, CauseType={CauseType}, TraceId={TraceId}.",
                storageException.Code,
                storageException.InnerException?.GetType().Name ?? "None",
                httpContext.TraceIdentifier);
        }
        else if (problem.Status >= StatusCodes.Status500InternalServerError)
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

    private static ProblemDetails CreateRangeProblem(HttpContext context, RangeNotSatisfiableException exception)
    {
        context.Response.Headers.ContentRange = $"bytes */{exception.ResourceLength}";
        return CreateProblem(
            StatusCodes.Status416RangeNotSatisfiable,
            "Range not satisfiable",
            exception.Message,
            exception.Code);
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
