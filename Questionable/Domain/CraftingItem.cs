using Questionable.Model.Questing;

namespace Questionable.Domain;

internal sealed record CraftingItem(uint ItemId, int ItemCount, EItemQuality Quality);
