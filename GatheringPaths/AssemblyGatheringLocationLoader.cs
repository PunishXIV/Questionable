using Questionable.Model.Gathering;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Questionable.GatheringPaths;

[SuppressMessage("ReSharper", "PartialTypeWithSinglePart", Justification = "Required for RELEASE")]
public static partial class AssemblyGatheringLocationLoader
{
    private static Dictionary<ushort, GatheringRoot>? _locations;

    public static IReadOnlyDictionary<ushort, GatheringRoot> GetLocations()
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

    public static Stream GatheringSchema =>
        typeof(AssemblyGatheringLocationLoader).Assembly.GetManifestResourceStream("Questionable.GatheringPaths.GatheringLocationSchema")!;

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private static void AddLocation(ushort questId, GatheringRoot root) => _locations![questId] = root;
}
