using Questionable.Model.Questing.Converter;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing;

public sealed class PurchaseMenu
{
    public string? ExcelSheet { get; set; }

    [JsonConverter(typeof(ExcelRefConverter))]
    public ExcelRef? Key { get; set; }
}
