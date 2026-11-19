namespace Questionable.Validation;

internal interface IQuestValidator
{
    IEnumerable<ValidationIssue> Validate(Quest quest);

    void Reset()
    {
    }
}
