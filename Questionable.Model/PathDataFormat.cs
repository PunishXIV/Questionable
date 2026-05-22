namespace Questionable.Model;

/// <summary>
///     Versioning contract between compiled path data (quest / gathering JSON) and the
///     <c>Questionable.Model</c> assembly that deserializes it.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CurrentVersion" /> describes the <em>shape</em> of path data that this
///         build of the model assembly can deserialize. It is deliberately independent of the
///         plugin's release version (<c>Directory.Build.targets</c>) — path data can be
///         updated and shipped without a plugin release, and this constant is what lets the
///         plugin decide whether a downloaded path bundle is safe to load.
///     </para>
///     <para>
///         <b>Bump <see cref="CurrentVersion" /></b> whenever a change makes newly-authored
///         path data un-loadable by older plugin builds, or makes existing path data
///         un-loadable by this build. For example:
///         <list type="bullet">
///             <item>a new <c>EInteractionType</c>, <c>EAetheryteLocation</c> (or any other enum) value that path data starts using;</item>
///             <item>a new <em>required</em> property on a path model type;</item>
///             <item>renaming or removing a property or enum value.</item>
///         </list>
///     </para>
///     <para>
///         <b>Do NOT bump</b> for changes that stay forward-compatible — e.g. adding a new
///         <em>optional</em> property that older plugin builds can safely ignore.
///     </para>
///     <para>
///         A published path bundle records the minimum <see cref="CurrentVersion" /> it
///         requires; the plugin refuses to load a bundle whose requirement exceeds its own
///         <see cref="CurrentVersion" />, so a path-only update can never break an installed
///         plugin.
///     </para>
/// </remarks>
public static class PathDataFormat
{
    /// <summary>
    ///     The path-data format version this assembly understands. See the remarks on
    ///     <see cref="PathDataFormat" /> for when this must be bumped.
    /// </summary>
    public const int CurrentVersion = 1;
}
