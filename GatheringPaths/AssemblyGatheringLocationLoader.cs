using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Questionable.Model.Gathering;

namespace Questionable.GatheringPaths;

public static partial class AssemblyGatheringLocationLoader
{
    private static Dictionary<ushort, GatheringRoot>? _locations;

    [SuppressMessage("Style", "IDE0074:Use compound assignment")]
    public static IReadOnlyDictionary<ushort, GatheringRoot> Locations
    {
        get
        {
            if (_locations == null)
            {
                _locations = [];
#if RELEASE
                LoadLocations();
#endif
            }

            return _locations ?? throw new InvalidOperationException("location data is not initialized");
        }
    }

    public static Stream GatheringSchema =>
        typeof(AssemblyGatheringLocationLoader).Assembly.GetManifestResourceStream("Questionable.GatheringPaths.GatheringLocationSchema")!;

    private static void AddLocation(ushort questId, GatheringRoot root) => _locations![questId] = root;
}
