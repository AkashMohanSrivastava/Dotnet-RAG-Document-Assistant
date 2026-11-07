namespace DocumentAssistant.Application.Common;

/// <summary>
/// The request was understood but its input is invalid. The API maps this to 400 Bad Request.
/// </summary>
public class ValidationFailedException(string message) : Exception(message);
