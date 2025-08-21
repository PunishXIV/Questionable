using System.Collections.Generic;
using Questionable.Model.Common.Converter;

namespace Questionable.Model.Questing.Converter;

public sealed class DialogueChoiceTypeConverter() : EnumConverter<EDialogChoiceType>(Values)
{
    private static readonly Dictionary<EDialogChoiceType, string> Values = new()
    {
        { EDialogChoiceType.YesNo, "YesNo" },
        { EDialogChoiceType.List, "List" },
    };
}
