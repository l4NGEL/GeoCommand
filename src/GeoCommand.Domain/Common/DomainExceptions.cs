namespace GeoCommand.Domain.Common;

/// <summary>Girdi bir veya daha fazla doğrulama kuralını ihlal ettiğinde fırlatılır.</summary>
public sealed class DomainValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Geçerli bir girdi, nesnenin mevcut durumunda izin verilmeyen bir işlem istediğinde fırlatılır.</summary>
public sealed class DomainRuleException(string message) : Exception(message);
