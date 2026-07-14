using System.Collections.Generic;
using Questionable.Domain;

namespace Questionable.Validation;

internal interface IQuestValidator
{
    IEnumerable<ValidationIssue> Validate(Quest quest);

    void Reset()
    {
    }
}
