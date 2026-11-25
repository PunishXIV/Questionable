using System.Text;

namespace Questionable.Extensions;

internal static class EnumExtensions
{
    public static string ToFormattedText(this Enum value)
    {
        var text = value.ToString();
        var builder = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]))
            {
                builder.Append(' ');
            }

            builder.Append(text[i]);
        }

        return builder.ToString().Trim();
    }
}
