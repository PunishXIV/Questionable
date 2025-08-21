using System.Collections.Generic;

namespace Questionable.Model.Gathering;

public sealed class GatheringNode
{
    public uint DataId { get; set; }
    public bool? Fly { get; set; }

    public List<GatheringLocation> Locations { get; set; } = [];
}
