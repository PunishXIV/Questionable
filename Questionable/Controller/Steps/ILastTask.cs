using Questionable.Model.Questing;

namespace Questionable.Controller.Steps;

internal interface ILastTask : ITask
{
    public ElementId ElementId { get; }
    public int Sequence { get; }
}
