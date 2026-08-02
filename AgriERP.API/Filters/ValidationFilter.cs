using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgriERP.API.Filters;

/// <summary>
/// Runs the FluentValidation validator for every action argument that has one.
///
/// FluentValidation.AspNetCore's automatic integration was deprecated, so this
/// does the wiring explicitly. That is arguably better anyway: validation runs
/// where you can see it, and the failure is thrown as the application's own
/// ValidationException so it comes out of the exception middleware in the same
/// ApiResponse shape as every other error - rather than as ASP.NET's separate
/// ModelState format that the frontend would have to special-case.
/// </summary>
public class ValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var failures = new Dictionary<string, List<string>>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (_services.GetService(validatorType) is not IValidator validator)
                continue;

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (result.IsValid) continue;

            foreach (var error in result.Errors)
            {
                if (!failures.TryGetValue(error.PropertyName, out var messages))
                    failures[error.PropertyName] = messages = new List<string>();

                messages.Add(error.ErrorMessage);
            }
        }

        if (failures.Count > 0)
            throw new Application.Common.Exceptions.ValidationException(
                failures.ToDictionary(f => f.Key, f => f.Value.ToArray()));

        await next();
    }
}
