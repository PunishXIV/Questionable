using System;
namespace Questionable.Model.Questing;

public class ExcelRef
{

    public enum EType
    {
        None,
        Key,
        RowId,
        RawString
    }

    private readonly uint? _rowIdValue;
    private readonly string? _stringValue;

    public ExcelRef(string value)
    {
        _stringValue = value;
        _rowIdValue = null;
        Type = EType.Key;
    }

    public ExcelRef(uint value)
    {
        _stringValue = null;
        _rowIdValue = value;
        Type = EType.RowId;
    }

    private ExcelRef(string? stringValue, uint? rowIdValue, EType type)
    {
        _stringValue = stringValue;
        _rowIdValue = rowIdValue;
        Type = type;
    }

    public EType Type { get; }

    public static ExcelRef FromKey(string value) => new(value, rowIdValue: null, EType.Key);
    public static ExcelRef FromRowId(uint rowId) => new(stringValue: null, rowId, EType.RowId);
    public static ExcelRef FromSheetValue(string value) => new(value, rowIdValue: null, EType.RawString);

    public string AsKey()
    {
        if (Type != EType.Key)
            throw new InvalidOperationException();

        return _stringValue!;
    }

    public uint AsRowId()
    {
        if (Type != EType.RowId)
            throw new InvalidOperationException();

        return _rowIdValue!.Value;
    }

    public string AsRawString()
    {
        if (Type != EType.RawString)
            throw new InvalidOperationException();

        return _stringValue!;
    }
}
