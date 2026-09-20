namespace Cx.Core.Scoring;

public sealed record DealerScore(string DealerId, string DealerName, int SurveyCount, double? Score, int? Rank);

public static class Ranking
{
    /// <summary>
    /// Standard competition ranking ("1224"): dealers with equal scores share a rank and the next rank is skipped.
    /// Dealers without surveys in the period are unranked (Rank = null) and listed last.
    /// </summary>
    public static IReadOnlyList<DealerScore> Rank(IEnumerable<(string Id, string Name, double TotalNetScore, int SurveyCount)> dealers)
    {
        var scored = dealers
            .Select(d => (d.Id, d.Name, d.SurveyCount, Score: SurveyScoring.DealerScore(d.TotalNetScore, d.SurveyCount)))
            .ToList();

        var ranked = scored.Where(d => d.Score is not null)
            .OrderByDescending(d => d.Score).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();

        var result = new List<DealerScore>(scored.Count);
        int? previousRank = null;
        double? previousScore = null;
        for (var i = 0; i < ranked.Count; i++)
        {
            var d = ranked[i];
            var rank = previousScore == d.Score ? previousRank!.Value : i + 1;
            result.Add(new DealerScore(d.Id, d.Name, d.SurveyCount, d.Score, rank));
            previousRank = rank;
            previousScore = d.Score;
        }

        result.AddRange(scored.Where(d => d.Score is null)
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new DealerScore(d.Id, d.Name, d.SurveyCount, null, null)));
        return result;
    }
}
