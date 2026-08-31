using System.Text;

namespace Questionable.Extensions;

internal static class EnumExtensions
{
    public static string ToFormattedText(this Enum value)
    {
        var stringVal = value.ToString();
        var bld = new StringBuilder();

        for (var i = 0; i < stringVal.Length; i++)
        {
            if (char.IsUpper(stringVal[i]))
            {
                bld.Append(' ');
            }

            bld.Append(stringVal[i]);
        }

        return bld.ToString().Trim();
    }
}
