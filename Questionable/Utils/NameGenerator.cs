using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Questionable.Utils;

/// <summary>
/// Adapted from Henchman by Knightmore
/// </summary>
#pragma warning disable CA5394 // insecure randomness
internal static class NameGenerator
{
	private static readonly Random Rand = new Random();

	private static readonly string[] Prefix = new string[35]
	{
		"A", "Ad", "Al", "Am", "An", "Ar", "Be", "Bl", "Br", "Ch",
		"Cl", "De", "Ed", "El", "Em", "Fa", "Fl", "Fr", "Gr", "Jo",
		"Ki", "La", "Ma", "Na", "O", "Ol", "Or", "Pa", "Re", "Sh",
		"Si", "Ta", "Th", "Tr", "Va"
	};

	private static readonly string[] Middle = new string[21]
	{
		"ad", "al", "am", "an", "ar", "as", "at", "el", "en", "er",
		"es", "et", "ei", "il", "in", "ir", "ol", "on", "or", "os",
		"ur"
	};

	private static readonly string[] FeminineSuffix = new string[28]
	{
		"a", "ina", "ie", "ara", "eera", "aea", "osa", "ya", "tha", "aya",
		"ana", "ielle", "ia", "ora", "iss", "ea", "ene", "ice", "ra", "ka",
		"nira", "wen", "elle", "lina", "wyn", "lora", "vina", "yn"
	};

	private static readonly string[] MasculineSuffix = new string[21]
	{
		"an", "nik", "anar", "ton", "or", "ant", "er", "dir", "ius", "ric",
		"no", "ien", "ard", "len", "ian", "en", "o", "as", "on", "rus",
		"us"
	};

	private static readonly List<PropertyInfo> RaceLastNames = (from p in typeof(CharaMakeName).GetProperties(BindingFlags.Instance | BindingFlags.Public)
		where p.Name.EndsWith("LastName") && !p.Name.Contains("Lalafell") && !p.Name.Contains("SeaWolf") && !p.Name.Contains("Viera")
		select p).ToList();

	private static readonly List<PropertyInfo> FemaleFirstNames = (from p in typeof(CharaMakeName).GetProperties(BindingFlags.Instance | BindingFlags.Public)
		where p.Name.EndsWith("Female") && !p.Name.Contains("Lalafell") && !p.Name.Contains("SeaWolf") && !p.Name.Contains("Viera")
		select p).ToList();

	private static readonly List<PropertyInfo> MaleFirstNames = (from p in typeof(CharaMakeName).GetProperties(BindingFlags.Instance | BindingFlags.Public)
		where p.Name.EndsWith("Male") && !p.Name.Contains("Lalafell") && !p.Name.Contains("SeaWolf") && !p.Name.Contains("Viera")
		select p).ToList();

	internal static string GenerateFeminineName => GeneratePrefixMiddle(1, 3) + FeminineSuffix[Rand.Next(FeminineSuffix.Length)];

	internal static string GenerateMasculineName => GeneratePrefixMiddle(1, 3) + MasculineSuffix[Rand.Next(MasculineSuffix.Length)];

	internal static (string FirstName, string LastName) GetFullMasculineName => GenerateFullName(GenerateMasculineName);

	internal static (string FirstName, string LastName) GetFullFeminineName => GenerateFullName(GenerateFeminineName);

	internal static (string FirstName, string LastName) GenerateFullName(string firstName)
	{
		int firstNameLength = firstName.Length;
		int possibleLastNameLength = 20 - firstNameLength;
		string lastName;
		do
		{
			lastName = GetLastName();
		}
		while (lastName.Length > possibleLastNameLength);
		return (FirstName: firstName, LastName: lastName);
	}

	internal static string GetFirstName(string lastName, bool male)
	{
		int lastNameLength = lastName.Length;
		int possibleFirstNameLength = 20 - lastNameLength;
		string firstName;
		do
		{
			firstName = male ? GetMasculineName() : GetFeminineName();
		}
		while (firstName.Length > possibleFirstNameLength);
		return firstName;
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

	internal static string GetLastName()
	{
		ExcelSheet<CharaMakeName> nameSheet = Svc.Data.GetExcelSheet<CharaMakeName>();
		int nameRowAmount = nameSheet.Count;
		PropertyInfo randomLastNameColumn = RaceLastNames[Rand.Next(RaceLastNames.Count)];
		string lastName = "";
		do
		{
            if (randomLastNameColumn.GetValue(nameSheet.GetRow((uint)Rand.Next(nameRowAmount))) is ReadOnlySeString value)
			    lastName = value.ExtractText();
		}
		while (string.IsNullOrEmpty(lastName));
		return lastName;
	}

	internal static string GetLastName(string firstName)
	{
		int firstNameLength = firstName.Length;
		int possibleLastNameLength = 20 - firstNameLength;
		ExcelSheet<CharaMakeName> nameSheet = Svc.Data.GetExcelSheet<CharaMakeName>();
		int nameRowAmount = nameSheet.Count;
		PropertyInfo randomLastNameColumn = RaceLastNames[Rand.Next(RaceLastNames.Count)];
		string lastName = "";
		do
		{
            if (randomLastNameColumn.GetValue(nameSheet.GetRow((uint)Rand.Next(nameRowAmount))) is ReadOnlySeString value)
			    lastName = value.ExtractText();
		}
		while (string.IsNullOrEmpty(lastName) && lastName.Length > possibleLastNameLength);
		return lastName;
	}

	private static string GeneratePrefixMiddle(int min, int max)
	{
		int middleAmount = Rand.Next(min, max);
		string name = Prefix[Rand.Next(Prefix.Length)];
		for (int i = 0; i < middleAmount; i++)
		{
			name += Middle[Rand.Next(Middle.Length)];
		}
		return name;
	}
}
#pragma warning restore CA5394
