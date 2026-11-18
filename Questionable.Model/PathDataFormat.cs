namespace Questionable.Model;

// Bump when older plugins can't load new path data or this build can't load old data: a new enum value
// paths use, a new required property, or a renamed or removed property or enum value. Not for new optional
// properties. Separate from the plugin version; installed plugins refuse bundles that need a newer one.
public static class PathDataFormat
{
    public const int CurrentVersion = 2;
}
