using FluentValidation;
using HRNexus.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HRNexus.Application.Tests;

/// <summary>
/// Tests the MediatR pipeline behaviors directly. They run on every request that goes through
/// IMediator.Send (the real app + WebApplicationFactory), wrapping handlers with cross-cutting
/// validation and logging.
/// </summary>
public class PipelineBehaviorTests
{
    private sealed record DemoRequest(string Name) : IRequest<string>;

    private sealed class DemoValidator : AbstractValidator<DemoRequest>
    {
        public DemoValidator() =>
            RuleFor(r => r.Name).NotEmpty().WithMessage("Name is required.");
    }

    // ── ValidationBehavior ────────────────────────────────────────────────────

    [Fact]
    public async Task ValidationBehavior_IsNoOp_WhenNoValidatorsRegistered()
    {
        var behavior = new ValidationBehavior<DemoRequest, string>(Array.Empty<IValidator<DemoRequest>>());

        var result = await behavior.Handle(new DemoRequest(""), () => Task.FromResult("handler-ran"), CancellationToken.None);

        Assert.Equal("handler-ran", result);   // no validator → request passes straight through
    }

    [Fact]
    public async Task ValidationBehavior_CallsHandler_WhenRequestIsValid()
    {
        var behavior = new ValidationBehavior<DemoRequest, string>([new DemoValidator()]);

        var result = await behavior.Handle(new DemoRequest("ok"), () => Task.FromResult("handler-ran"), CancellationToken.None);

        Assert.Equal("handler-ran", result);
    }

    [Fact]
    public async Task ValidationBehavior_ThrowsArgumentException_AndSkipsHandler_WhenInvalid()
    {
        var behavior = new ValidationBehavior<DemoRequest, string>([new DemoValidator()]);
        var handlerRan = false;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            behavior.Handle(new DemoRequest(""), () => { handlerRan = true; return Task.FromResult("x"); }, CancellationToken.None));

        Assert.Equal("Name is required.", ex.Message);   // surfaced for the ExceptionMiddleware → 400
        Assert.False(handlerRan);                          // handler never invoked
    }

    // ── LoggingBehavior ───────────────────────────────────────────────────────

    [Fact]
    public async Task LoggingBehavior_ReturnsHandlerResult_OnSuccess()
    {
        var behavior = new LoggingBehavior<DemoRequest, string>(NullLogger<LoggingBehavior<DemoRequest, string>>.Instance);

        var result = await behavior.Handle(new DemoRequest("ok"), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task LoggingBehavior_RethrowsHandlerException()
    {
        var behavior = new LoggingBehavior<DemoRequest, string>(NullLogger<LoggingBehavior<DemoRequest, string>>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new DemoRequest("x"),
                () => throw new InvalidOperationException("boom"), CancellationToken.None));
    }
}
