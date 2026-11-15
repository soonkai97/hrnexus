namespace HRNexus.Application.Common;

/// <summary>Thrown when the signed-in person is known but may not do this (for example signing off someone who does not report to them). Maps to HTTP 403.</summary>
public class ForbiddenException(string message = "You are not allowed to do this.")
    : Exception(message);
