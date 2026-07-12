using System;

namespace Questionable.Model.Common;

[AttributeUsage(AttributeTargets.Property)]
public sealed class AlwaysSerializeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DefaultTrueAttribute : Attribute;
[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreWhenDefaultInstanceAttribute : Attribute;