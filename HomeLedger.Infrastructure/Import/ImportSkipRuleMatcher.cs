using System.Text.RegularExpressions;
using HomeLedger.Core.Entities;
using HomeLedger.Core.Import;

namespace HomeLedger.Infrastructure.Import;

public record ImportSkipRuleMatch(string SkipKind, string RuleName);

public interface IImportSkipRuleMatcher
{
    ImportSkipRuleMatch? Match(string description, IReadOnlyList<ImportSkipRule> rules);
}

public class ImportSkipRuleMatcher : IImportSkipRuleMatcher
{
    public ImportSkipRuleMatch? Match(string description, IReadOnlyList<ImportSkipRule> rules)
    {
        if (string.IsNullOrWhiteSpace(description) || rules.Count == 0)
            return null;

        foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            if (rule.MatchType == ImportSkipRuleMatchType.Regex)
            {
                try
                {
                    if (Regex.IsMatch(description, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        return new ImportSkipRuleMatch(rule.SkipKind, rule.Name);
                }
                catch (RegexParseException)
                {
                    continue;
                }
            }
            else if (description.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new ImportSkipRuleMatch(rule.SkipKind, rule.Name);
            }
        }

        return null;
    }
}
