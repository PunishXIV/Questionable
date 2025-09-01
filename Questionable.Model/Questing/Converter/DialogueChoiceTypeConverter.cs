using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class DialogueChoiceTypeConverter() : EnumConverter<EDialogChoiceType>(Values)
{
    private static readonly Dictionary<EDialogChoiceType, string> Values = new()
    {
        { EDialogChoiceType.YesNo, "YesNo" },
        { EDialogChoiceType.List, "List" },
    };
}
