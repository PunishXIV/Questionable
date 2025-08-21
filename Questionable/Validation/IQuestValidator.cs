using System.Collections.Generic;
using Questionable.Model;

namespace Questionable.Validation;

internal interface IQuestValidator
{
    IEnumerable<ValidationIssue> Validate(Quest quest);

    void Reset()
    {
    }
}
