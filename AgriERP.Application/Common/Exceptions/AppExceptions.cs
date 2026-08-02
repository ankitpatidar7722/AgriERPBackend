namespace AgriERP.Application.Common.Exceptions;

/*
 * Services throw these; the API's exception middleware maps each to a status
 * code. Services therefore never reference ActionResult or HttpContext, which
 * is what keeps the Application layer testable without a web host.
 */

/// <summary>404. A requested record does not exist, or is soft-deleted.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string entity, object key)
        : base($"{entity} with id '{key}' was not found.") { }
}

/// <summary>400. Field-level input failures, from FluentValidation or a service.</summary>
public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException()
        : base("One or more validation errors occurred.")
        => Errors = new Dictionary<string, string[]>();

    public ValidationException(IDictionary<string, string[]> errors)
        : this() => Errors = errors;

    public ValidationException(string field, string message)
        : this() => Errors = new Dictionary<string, string[]> { [field] = new[] { message } };
}

/// <summary>
/// 409. The request is well-formed but collides with existing data - a
/// duplicate item code, a supplier bill already entered.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>
/// 422. Structurally valid but not allowed by a business rule: selling below
/// the minimum rate, billing past a credit limit, posting into a closed year.
/// Distinct from ValidationException because the fix is a decision, not a typo.
/// </summary>
public class BusinessRuleException : Exception
{
    public string? Code { get; }

    public BusinessRuleException(string message, string? code = null) : base(message)
        => Code = code;
}

/// <summary>403. Authenticated, but lacking the required permission.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message) { }
}

/// <summary>401.</summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Authentication failed.") : base(message) { }
}
