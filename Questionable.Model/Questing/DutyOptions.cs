using System.Collections.Generic;

namespace Questionable.Model.Questing;

public class DutyOptions
{
    public bool Enabled { get; set; }
    public uint ContentFinderConditionId { get; set; }
    public bool LowPriority { get; set; }
    public List<string> Notes { get; set; } = [];
}
