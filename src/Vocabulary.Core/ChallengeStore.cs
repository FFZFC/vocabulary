using System.Text.Json;
using System.Globalization;

namespace Vocabulary.Core;

public sealed partial class StudyStore
{
    public IReadOnlyList<Word> ChallengeCandidates(ChallengeOptions options)
    {
        var filter = options.Source switch
        {
            ChallengeSource.Learned => LearnedFilter.Learned,
            ChallengeSource.Notebook => LearnedFilter.Notebook,
            ChallengeSource.Forgotten => LearnedFilter.Forgotten,
            _ => LearnedFilter.All
        };
        var words = Search(options.Book, "", filter, 0, int.MaxValue).Where(w => ChallengeRules.Eligible(w, options));
        if (options.Kind == ChallengeKind.Matching) words = words.DistinctBy(ChallengeRules.MatchPrompt);
        return words.ToArray();
    }

    public void SaveChallenge(ChallengeResult result)
    {
        using var tx = db.BeginTransaction();
        using var round = Cmd("INSERT INTO challenge_rounds VALUES($id,$day,$at,$kind,$book,$options) ON CONFLICT(id) DO NOTHING",
            ("$id", result.Id.ToString()), ("$day", LocalDay(result.Completed)), ("$at", result.Completed.ToUnixTimeSeconds()),
            ("$kind", (int)result.Options.Kind), ("$book", (int)result.Options.Book), ("$options", JsonSerializer.Serialize(result.Options)));
        round.Transaction = tx;
        if (round.ExecuteNonQuery() == 0) return;
        for (int i = 0; i < result.Answers.Count; i++)
        {
            using var answer = Cmd("INSERT INTO challenge_answers VALUES($id,$position,$payload)",
                ("$id", result.Id.ToString()), ("$position", i), ("$payload", JsonSerializer.Serialize(result.Answers[i])));
            answer.Transaction = tx;
            answer.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<ChallengeResult> ChallengeHistory(DateOnly? start = null, DateOnly? end = null, int limit = int.MaxValue)
    {
        using var c = Cmd("""
            SELECT r.id,r.at,r.options,a.position,a.payload FROM
              (SELECT rowid AS sequence,* FROM challenge_rounds WHERE day BETWEEN $start AND $end ORDER BY at DESC,rowid DESC LIMIT $limit) r
            JOIN challenge_answers a ON a.round_id=r.id ORDER BY r.at DESC,r.sequence DESC,a.position
            """,
            ("$start", (start ?? DateOnly.MinValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$end", (end ?? DateOnly.MaxValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$limit", limit));
        using var reader = c.ExecuteReader();
        var results = new List<ChallengeResult>();
        List<ChallengeAnswer>? items = null;
        Guid previous = Guid.Empty;
        while (reader.Read())
        {
            Guid id = Guid.Parse(reader.GetString(0));
            if (id != previous)
            {
                items = [];
                results.Add(new(id, JsonSerializer.Deserialize<ChallengeOptions>(reader.GetString(2))!, DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)), items));
                previous = id;
            }
            items!.Add(JsonSerializer.Deserialize<ChallengeAnswer>(reader.GetString(4))!);
        }
        return results;
    }
}
