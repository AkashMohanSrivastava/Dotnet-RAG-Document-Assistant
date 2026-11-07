namespace DocumentAssistant.Application.Common;

/// <summary>
/// The request is valid but conflicts with the resource's current state. The API maps this to 409 Conflict.
/// </summary>
public class ConflictException(string message) : Exception(message);
