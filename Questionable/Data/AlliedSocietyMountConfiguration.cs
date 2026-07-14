using System.Collections.Generic;
using Questionable.Model.Common;

namespace Questionable.Data;

public sealed record AlliedSocietyMountConfiguration(IReadOnlyList<uint> IssuerDataIds, EAetheryteLocation ClosestAetheryte);
