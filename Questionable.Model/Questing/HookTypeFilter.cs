using System;
using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(HookTypeFilterConverter))]
public readonly struct HookTypeFilter : IEquatable<HookTypeFilter>
{
    public bool IsAllHooksets { get; }
    public Hookset? Hookset { get; }

    private HookTypeFilter(bool isAllHooksets, Hookset? hookset)
    {
        IsAllHooksets = isAllHooksets;
        Hookset = hookset;
    }

    public static HookTypeFilter AllHooksets => new(isAllHooksets: true, hookset: null);

    public static HookTypeFilter From(Hookset hookset) => new(isAllHooksets: false, hookset);

    public bool Equals(HookTypeFilter other) =>
        IsAllHooksets == other.IsAllHooksets && Equals(Hookset, other.Hookset);

    public override bool Equals(object? obj) => obj is HookTypeFilter other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (IsAllHooksets.GetHashCode() * 397) ^ (Hookset?.GetHashCode() ?? 0);
        }
    }
}
