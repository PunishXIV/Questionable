namespace Questionable.Functions;

/// <remarks>
///     The whole free/favored aetheryte situation is primarily relevant for early ARR anyhow, since teleporting to
///     each class quest the moment it becomes available might end up with the character running out of gil.
/// </remarks>
public enum AetheryteRegistrationResult
{
    NotPossible,
    SecurityTokenFreeDestinationAvailable,
    FavoredDestinationAvailable
}
