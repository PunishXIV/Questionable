using System.Collections.Generic;

namespace Questionable.Model.Questing;

public sealed class SinglePlayerDutyOptions
{
    public bool Enabled { get; set; }
    public List<string> Notes { get; set; } = [];
    public byte Index { get; set; }
}
