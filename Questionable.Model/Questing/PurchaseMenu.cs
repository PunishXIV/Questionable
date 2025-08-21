using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

public sealed class PurchaseMenu
{
    public string? ExcelSheet { get; set; }

    [JsonConverter(typeof(ExcelRefConverter))]
    public ExcelRef? Key { get; set; }
}
