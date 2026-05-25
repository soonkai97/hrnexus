namespace HRNexus.Api.Filters;

/// <summary>
/// Marks a controller or action as requiring one or more feature flags to be enabled.
/// If none of the specified flags are enabled, the filter returns 403 Forbidden.
/// When multiple keys are provided, any one being enabled is sufficient (OR semantics).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class FeatureRequiredAttribute(params string[] keys) : Attribute
{
    public string[] Keys { get; } = keys;
}
