using DocumentAssistant.Application.Text;

namespace DocumentAssistant.Application.Documents;

public record DocumentTextDto(
    Guid DocumentId,
    string FileName,
    int PageCount,
    int TotalCharacters,
    IReadOnlyList<ExtractedPage> Pages);
