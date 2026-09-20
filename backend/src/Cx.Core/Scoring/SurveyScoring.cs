using Cx.Core.Domain;

namespace Cx.Core.Scoring;

/// <summary>The scoring rules. Keep in sync with Policies/scoring-methodology.md.</summary>
public static class SurveyScoring
{
    public static int Score(RecommendAnswer answer) => answer switch
    {
        RecommendAnswer.HighlyRecommend => 100,
        RecommendAnswer.Recommend => 50,
        RecommendAnswer.MightRecommend => 25,
        RecommendAnswer.NotRecommend => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(answer)),
    };

    public static int Score(VehicleConditionAnswer answer) => answer switch
    {
        VehicleConditionAnswer.AllGood => 100,
        VehicleConditionAnswer.PartMissing => 50,
        VehicleConditionAnswer.Defect => 50,
        VehicleConditionAnswer.PartMissingAndDefect => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(answer)),
    };

    /// <summary>Net score of one survey = average of the two question scores.</summary>
    public static double NetScore(int recommendScore, int conditionScore) => (recommendScore + conditionScore) / 2.0;

    public static SurveyResponse Apply(SurveyResponse response)
    {
        response.RecommendScore = Score(response.Recommend);
        response.ConditionScore = Score(response.VehicleCondition);
        response.NetScore = NetScore(response.RecommendScore, response.ConditionScore);
        return response;
    }

    /// <summary>Dealer score for a period = total of net scores / number of surveys. Null when there are no surveys.</summary>
    public static double? DealerScore(double totalNetScore, int surveyCount) =>
        surveyCount == 0 ? null : Math.Round(totalNetScore / surveyCount, 2);
}
