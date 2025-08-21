using System;

namespace Questionable.Model.Questing;

public class ExcelRef
{
    private readonly string? _stringValue;
    private readonly uint? _rowIdValue;

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

    public static ExcelRef FromKey(string value) => new(value, null, EType.Key);
    public static ExcelRef FromRowId(uint rowId) => new(null, rowId, EType.RowId);
    public static ExcelRef FromSheetValue(string value) => new(value, null, EType.RawString);

    public EType Type { get; }

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

    public enum EType
    {
        None,
        Key,
        RowId,
        RawString,
    }
}
