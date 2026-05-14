using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NightclubWorkforce.Api.ModelBinders;

/// <summary>
/// Binds [FromQuery] / [FromRoute] DateTime parameters as Kind=Utc.
/// Prevents "Cannot write DateTime with Kind=Unspecified" when query-string dates
/// reach an EF Core query against a PostgreSQL 'timestamp with time zone' column.
/// </summary>
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var result = context.ValueProvider.GetValue(context.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        var raw = result.FirstValue;

        // Nullable DateTime — allow empty string / missing value
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (context.ModelMetadata.IsReferenceOrNullableType)
                context.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        // Try ISO 8601 with explicit timezone first ("2025-01-01T00:00:00Z", "+08:00", etc.)
        // then fall back to plain date/datetime treated as UTC.
        if (DateTime.TryParse(raw, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            || DateTime.TryParse(raw, null,
                System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
        {
            context.Result = ModelBindingResult.Success(
                dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        }
        else
        {
            context.ModelState.TryAddModelError(context.ModelName, $"Invalid date '{raw}'.");
        }

        return Task.CompletedTask;
    }
}

public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.ModelType;
        return (type == typeof(DateTime) || type == typeof(DateTime?))
            ? new UtcDateTimeModelBinder()
            : null;
    }
}
