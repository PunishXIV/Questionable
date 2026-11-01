namespace Questionable.External;

internal enum ArtisanStatus
{
    Unavailable,
    Idle,
    // Busy with setup/cleanup, but neither a list nor endurance is active yet.
    Queued,
    Busy,
    Paused,
}
