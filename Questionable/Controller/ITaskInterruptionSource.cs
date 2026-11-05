namespace Questionable.Controller;

/// <summary>Damage interruptions consumed by task scheduling independently of the native action hook.</summary>
internal interface ITaskInterruptionSource
{
    event EventHandler? Interrupted;
}
