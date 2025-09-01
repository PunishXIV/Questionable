using Questionable.Model;
using System.Collections.Generic;

namespace Questionable.Validation;

internal interface IQuestValidator
{
    IEnumerable<ValidationIssue> Validate(Quest quest);

    void Reset()
    {
    }
}
