using AgriERP.Application.Common.Exceptions;
using AgriERP.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using ApplicationValidationException = AgriERP.Application.Common.Exceptions.ValidationException;

namespace AgriERP.API.Middleware;

/// <summary>
/// Turns every exception into the same ApiResponse envelope the successful
/// paths use, so the frontend never has to guess whether a failure arrived as
/// ProblemDetails, a bare string, or an HTML error page.
///
/// Expected failures (not found, validation, business rules) are logged at
/// Information or Warning - they are the system working. Only genuine faults
/// are logged as errors, which keeps the log usable: if everything is an
/// error, nothing is.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var (status, message, errors) = exception switch
        {
            ApplicationValidationException ve =>
                (HttpStatusCode.BadRequest, ve.Message, ve.Errors),

            NotFoundException nf =>
                (HttpStatusCode.NotFound, nf.Message, null),

            ConflictException ce =>
                (HttpStatusCode.Conflict, ce.Message, null),

            BusinessRuleException br =>
                (HttpStatusCode.UnprocessableEntity, br.Message, null),

            UnauthorizedException ue =>
                (HttpStatusCode.Unauthorized, ue.Message, null),

            ForbiddenException fe =>
                (HttpStatusCode.Forbidden, fe.Message, null),

            // Two users edited the same row. Surfaced as 409 with a plain
            // instruction, because "reload and try again" is genuinely the fix.
            DbUpdateConcurrencyException =>
                (HttpStatusCode.Conflict,
                 "This record was changed by someone else while you were editing it. Reload and try again.",
                 (IDictionary<string, string[]>?)null),

            DbUpdateException dbe =>
                (HttpStatusCode.Conflict, DescribeDatabaseFailure(dbe), null),

            OperationCanceledException =>
                (HttpStatusCode.RequestTimeout, "The request was cancelled.", null),

            _ => (HttpStatusCode.InternalServerError,
                  // Never leak an exception message to a client in itemion:
                  // stack traces and connection strings end up in them.
                  _environment.IsDevelopment()
                      ? exception.Message
                      : "An unexpected error occurred. Quote the trace id when reporting this.",
                  null)
        };

        if (status == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception on {Method} {Path} (trace {TraceId})",
                context.Request.Method, context.Request.Path, traceId);
        else if (status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            _logger.LogWarning("{Status} on {Method} {Path}: {Message}",
                (int)status, context.Request.Method, context.Request.Path, exception.Message);
        else
            _logger.LogInformation("{Status} on {Method} {Path}: {Message}",
                (int)status, context.Request.Method, context.Request.Path, exception.Message);

        context.Response.Clear();
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";

        var payload = ApiResponse.Fail(message, errors, traceId);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    /// <summary>
    /// Translates the SQL Server errors a user can actually cause into
    /// language they can act on. Everything else keeps a generic message, so
    /// schema details never reach the client.
    /// </summary>
    private static string DescribeDatabaseFailure(DbUpdateException exception)
        => exception.InnerException is SqlException sql
            ? sql.Number switch
            {
                2601 or 2627 => "A record with these details already exists.",
                547          => "This record is linked to other data and cannot be changed or removed.",
                // 50024 is raised by usp_PostStockTransaction. Its message
                // already names the item and the shortfall, so it is passed
                // through verbatim.
                50024        => sql.Message,
                _            => "The database rejected this change."
            }
            : "The database rejected this change.";
}
