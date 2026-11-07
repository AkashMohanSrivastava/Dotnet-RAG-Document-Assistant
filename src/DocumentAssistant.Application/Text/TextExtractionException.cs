namespace DocumentAssistant.Application.Text;

/// <summary>
/// The file could not be turned into text. The message is safe to show to users and to store
/// as the document's failure reason.
/// </summary>
public class TextExtractionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
