namespace Questionable.External;

internal enum FeeshStartResult
{
    Started,
    /// <summary>The call may have reached Feesh, but the reply was lost.</summary>
    Uncertain,
    /// <summary>WahTools or Questionable is already busy.</summary>
    Busy,
    /// <summary>Feesh refused the job; the reason is in the Dalamud log.</summary>
    Refused,
    NotReady,
    Invalid,
}
