namespace NightclubWorkforce.Application.Common;

/// <summary>Thrown when a caller exceeds an allowed request rate. Maps to HTTP 429.</summary>
public class TooManyRequestsException(string message = "Too many requests. Please try again later.")
    : Exception(message);
