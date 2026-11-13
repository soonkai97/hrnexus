using FluentValidation;
using MediatR;

namespace HRNexus.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs any registered FluentValidation <see cref="IValidator{T}"/>
/// for a request before its handler. Failures surface as <see cref="ArgumentException"/> so the API's
/// ExceptionMiddleware maps them to HTTP 400 — matching the existing inline-validation convention.
///
/// Opt-in: a request is only validated if a validator is registered for it (none → no-op). Existing
/// commands keep their inline guards (which the handler-direct unit tests rely on); NEW commands can
/// add a `class XValidator : AbstractValidator&lt;XCommand&gt;` and get boundary validation for free.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct))))
                .SelectMany(result => result.Errors)
                .Where(failure => failure is not null)
                .ToList();

            if (failures.Count != 0)
                throw new ArgumentException(failures[0].ErrorMessage);
        }

        return await next(ct);
    }
}
