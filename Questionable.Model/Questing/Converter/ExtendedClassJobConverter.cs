using System.Collections.Generic;
using Questionable.Model.Common.Converter;
namespace Questionable.Model.Questing.Converter;

internal sealed class ExtendedClassJobConverter() : EnumConverter<EExtendedClassJob>(Values)
{
    private static readonly Dictionary<EExtendedClassJob, string> Values = new()
    {
        // members whose JSON name equals the enum name are filled in by EnumConverter
        { EExtendedClassJob.None, "None" },
        { EExtendedClassJob.WhiteMage, "White Mage" },
        { EExtendedClassJob.BlackMage, "Black Mage" },
        { EExtendedClassJob.DarkKnight, "Dark Knight" },
        { EExtendedClassJob.RedMage, "Red Mage" },
        { EExtendedClassJob.BlueMage, "Blue Mage" }
    };
}
