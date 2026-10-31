using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Questionable.Utils;

/// <summary>
/// Adapted from Henchman by Knightmore
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness")]
internal static class NameGenerator
{
    private static readonly Random Rand = new();

    private static readonly List<PropertyInfo> FemaleFirstNames = (from p in typeof(CharaMakeName).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                                                   where p.Name.EndsWith("Female") && !p.Name.Contains("Lalafell") && !p.Name.Contains("SeaWolf") && !p.Name.Contains("Viera")
                                                                   select p).ToList();

    private static readonly List<PropertyInfo> MaleFirstNames = (from p in typeof(CharaMakeName).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                                                 where p.Name.EndsWith("Male") && !p.Name.Contains("Lalafell") && !p.Name.Contains("SeaWolf") && !p.Name.Contains("Viera")
                                                                 select p).ToList();

    private static string? _generateFirstName;
    private static DateTime _generateFirstNameUpdated = DateTime.MinValue;
    internal static string GenerateFirstName(bool fast = false)
    {
        if (_generateFirstName == null || (DateTime.Now - _generateFirstNameUpdated).TotalMilliseconds > (fast ? 50 : 500))
        {
            _generateFirstName = Rand.NextBool() ? GetMasculineName() : GetFeminineName();
            _generateFirstNameUpdated = DateTime.Now;
        }
        return _generateFirstName;
    }

    internal static string GetFeminineName()
    {
        ExcelSheet<CharaMakeName> nameSheet = Svc.Data.GetExcelSheet<CharaMakeName>();
        int nameRowAmount = nameSheet.Count;
        PropertyInfo randomLastNameColumn = FemaleFirstNames[Rand.Next(FemaleFirstNames.Count)];
        string firstName = "";
        do
        {
            if (randomLastNameColumn.GetValue(nameSheet.GetRow((uint)Rand.Next(nameRowAmount))) is ReadOnlySeString value)
                firstName = value.ExtractText();
        }
        while (string.IsNullOrEmpty(firstName) || firstName == "Ilcum");
        return firstName;
    }

    internal static string GetMasculineName()
    {
        ExcelSheet<CharaMakeName> nameSheet = Svc.Data.GetExcelSheet<CharaMakeName>();
        int nameRowAmount = nameSheet.Count;
        PropertyInfo randomLastNameColumn = MaleFirstNames[Rand.Next(MaleFirstNames.Count)];
        string firstName = "";
        do
        {
            if (randomLastNameColumn.GetValue(nameSheet.GetRow((uint)Rand.Next(nameRowAmount))) is ReadOnlySeString value)
                firstName = value.ExtractText();
        }
        while (string.IsNullOrEmpty(firstName));
        return firstName;
    }
}
