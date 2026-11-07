using Questionable.Model.Common;

namespace Questionable.Data;

public sealed record AlliedSocietyMount(IReadOnlyList<uint> IssuerDataIds, EAetheryteLocation ClosestAetheryte);
