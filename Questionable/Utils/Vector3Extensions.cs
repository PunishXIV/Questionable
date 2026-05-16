using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Questionable.Model.Common;

namespace Questionable.Utils;
internal static class Vector3Extensions
{
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static float Length_XZ( this Vector3 value )
	{
		return MathF.Sqrt( value.X * value.X + value.Z * value.Z );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static float DistanceTo_XZ( this Vector3 value1, Vector3 value2 )
	{
		Vector3 difference = value1 - value2;
		return MathF.Sqrt( difference.X * difference.X + difference.Z * difference.Z );
	}
}