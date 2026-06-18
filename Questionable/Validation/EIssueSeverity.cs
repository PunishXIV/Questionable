using Questionable.Model.Common.Converter;

namespace Questionable.Validation;

public enum EIssueSeverity
{
    None,
    Error
}
public sealed class IssueSeverityConverter() : EnumConverter<EIssueSeverity>();
