using System;
using System.Globalization;
namespace Questionable.Model.Questing;

public class LastChecked
{
    public string? Username { get; set; }
    //[JsonConverter(typeof(DateConverter))]
    public string? Date { get; set; }

    public DateTime? ToDateTime() => Date != null ? DateTime.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    public TimeSpan? Since(DateTime dateTime) => Date != null ? dateTime.Subtract(ToDateTime()!.Value) : null;

    public override string ToString()
    {
        if (Date != null && Username != null)
            return $"{Date} by {Username}";
        if (Date != null)
            return Date;
        if (Username != null)
            return $"by {Username}";
        return "";
    }
}
