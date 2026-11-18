using Microsoft.Data.Sqlite;
using Vocabulary.Core;

internal static class ChallengeChecks
{
    internal static void Run(string dictionary, string scratch, Action<bool, string> check)
    {
        Word MakeWord(string text) => new(text, text, "", "n.", "测试释义 " + text, true);
        var time = DateTimeOffset.Now;
        var word = MakeWord("apple");
        var options = new ChallengeOptions(ChallengeKind.Dictation, Book.Cet4, ChallengeSource.All);
        var round = new ChallengeRound(options, [word, word], new Random(2));
        check(round.Questions.Count == 1 && !ChallengeRules.Prompt(word).Contains("apple"), "challenge de-duplicates and conceals answers in prompt");
        var token = round.Current.Id;
        round.Input = " APPLE "; round.Submit(token); round.Submit(token);
        check(round.Answers is [{ Outcome: AnswerOutcome.Correct, Input: " APPLE " }] && !round.Finished, "dictation trims and ignores case with single submission");
        round.Continue(token); round.Continue(token);
        check(round.Finished && round.Result(time).Completed == round.Result(time.AddDays(1)).Completed, "completed result timestamp stable across save retries");
        round = new(options, [word]); round.Hint(round.Current.Id); round.Input = "apple"; round.Submit(round.Current.Id);
        check(round.Answers.Single() is { Outcome: AnswerOutcome.Assisted, Hinted: true }, "hinted dictation recorded as assisted");
        round = new(options, [word]); round.Input = "wrong"; round.Submit(round.Current.Id); round.Hint(round.Current.Id);
        check(round.Answers.Single().Outcome == AnswerOutcome.Incorrect && !round.Hinted, "wrong first submission locked against later hints");
        round = new(options, [word]); round.Reveal(round.Current.Id); round.Submit(round.Current.Id);
        check(round.Answers.Single().Outcome == AnswerOutcome.Incorrect, "reveal and skip count as unsolved");
        round = new(options, [word, MakeWord("state")]);
        token = round.Current.Id; round.Reveal(token); round.Continue(token); round.Submit(token);
        check(round.Answers.Count == 1 && round.CurrentAnswer is null, "stale question identity cannot submit next word");
        var retry = new ChallengeRound(options, round.Answers.Where(a => a.Outcome != AnswerOutcome.Correct).Select(a => a.Word));
        check(retry.Questions.Count == 1 && retry.Answers.Count == 0 && retry.Id != round.Id, "wrong word retry creates a fresh round");

        var scramble = options with { Kind = ChallengeKind.Scramble };
        round = new(scramble, [word, MakeWord("a"), MakeWord("abcdefghijklmnop")], new Random(1));
        check(round.Questions.Count == 1 && round.Current.Letters != word.English && round.Current.Letters.Order().SequenceEqual(word.English.Order()), "scramble retains duplicate letters and enforces length limits");
        round.Input = "app"; round.Submit(round.Current.Id);
        check(round.Answers.Count == 0, "scramble rejects partial letter set");
        round.Input = "axx"; round.Hint(round.Current.Id);
        check(round.Input == "ap", "scramble hint repairs wrong suffix then fills next letter");
        round.Input = "apple"; round.Submit(round.Current.Id);
        check(round.Answers.Single().Outcome == AnswerOutcome.Assisted, "scramble hint affects result");

        var marks = ChallengeRules.Evaluate("apple", "allee");
        check(marks.SequenceEqual(new[] { LetterMark.Exact, LetterMark.Present, LetterMark.Absent, LetterMark.Absent, LetterMark.Exact }), "Wordle exact matches reserve duplicates before yellows");
        check(ChallengeRules.Evaluate("level", "eerie").Count(m => m != LetterMark.Absent) == 2, "Wordle consumes only remaining copies of duplicate letters");
        var dictionaryWords = File.ReadLines(Path.Combine(Path.GetDirectoryName(dictionary)!, "wordle-words.txt")).ToHashSet();
        check(dictionaryWords.Count == 19649 && dictionaryWords.Contains("about"), "offline guess dictionary contains broader common words");
        using var store = new StudyStore(Path.Combine(scratch, "games"), dictionary);
        foreach (int length in new[] { 4, 5, 6, 7 })
        {
            var o = options with { Kind = ChallengeKind.Wordle, Length = length };
            var pool = store.ChallengeCandidates(o);
            check(pool.Count > 100 && pool.All(w => w.English.Length == length && dictionaryWords.Contains(w.English)), "Wordle all eligible answers accepted at length " + length);
        }
        var wordle = options with { Kind = ChallengeKind.Wordle };
        round = new(wordle, [word]); token = round.Current.Id;
        foreach (string bad in new[] { "a", "?????", "zzzzz" }) { round.Input = bad; round.Guess(token, dictionaryWords); }
        check(round.Guesses.Count == 0, "invalid length, letters and dictionary guesses use no opportunity");
        round.Input = "allee"; round.Guess(token, new HashSet<string> { "allee", "apple" });
        round.Input = "allee"; round.Guess(token, dictionaryWords);
        check(round.Guesses.Count == 1, "duplicate Wordle guesses use no opportunity");
        round.Input = "apple"; round.Guess(token, dictionaryWords); round.Guess(token, dictionaryWords);
        check(round.Finished && round.Answers.Single() is { Outcome: AnswerOutcome.Correct, Attempts: 2 } && round.KeyboardMarks()['l'] == LetterMark.Exact, "Wordle win locks score and keyboard preserves best evidence");
        var win = round.Result(time);
        round = new(wordle, [word]);
        foreach (string guess in new[] { "state", "might", "point", "house", "water", "night" }) { round.Input = guess; round.Guess(round.Current.Id, dictionaryWords); }
        check(round.Finished && round.Guesses.Count == 6 && round.Answers.Single().Outcome == AnswerOutcome.Incorrect, "Wordle sixth unsuccessful guess ends round");
        round = new(wordle, [word]); round.Reveal(round.Current.Id);
        check(round.Finished && round.Answers.Single() is { Outcome: AnswerOutcome.Incorrect, Attempts: 0 }, "Wordle reveal records a loss");

        var pairing = options with { Kind = ChallengeKind.Matching };
        var pairWords = new[] { "state", "apple", "might", "point", "water", "house" }.Select(MakeWord).ToArray();
        round = new(pairing, pairWords.Concat([MakeWord("extra") with { Meaning = pairWords[0].Meaning.Replace("state", "extra") }]));
        // All synthetic prompts normalize to the same value, as intended by prompt de-duplication.
        check(round.Questions.Count == 1, "matching avoids duplicate visible prompts after masking");
        pairWords = pairWords.Select((w, i) => w with { Meaning = "释义编号 " + i }).ToArray();
        round = new(pairing, pairWords);
        check(round.MatchCount == 5 && round.MatchingOrder.Take(5).Order().SequenceEqual(Enumerable.Range(0, 5)), "matching boards contain unique shuffled pairs");
        round.Match(0, 0, 1); round.Match(0, 0, 0); round.Match(0, 0, 0);
        check(round.Answers.Single() is { Outcome: AnswerOutcome.Incorrect, Attempts: 2 }, "matching tracks first attempt and rejects matched duplicates");
        for (int i = 1; i < 5; i++) round.Match(0, i, i);
        round.NextGroup(0); round.Match(0, 5, 5);
        check(round.MatchPage == 1 && round.Answers.Count == 5, "matching rejects stale board operations");
        round.Match(1, 5, 5);
        check(round.Finished && round.Answers.Count == 6 && round.Result(time).Correct == 5, "matching finishes all boards with first-try accuracy");

        var a = store.Search(Book.Cet4, "abandon", LearnedFilter.All, 0, 1).Single();
        store.SetBookmark(a.Id, true); store.SetNote(a.Id, "保留笔记");
        check(store.ChallengeCandidates(options with { Source = ChallengeSource.Notebook }).Single() == a && store.ChallengeCandidates(options with { Source = ChallengeSource.Learned }).Count == 0, "challenge source filters use real saved and learned states");
        store.Submit(new(Guid.NewGuid(), a), Rating.Again, time);
        check(store.ChallengeCandidates(options with { Source = ChallengeSource.Forgotten }).Single() == a, "forgotten challenge source respects existing progress");
        var before = store.History().ToArray();
        var schedule = store.GetWordHistory(a.Id);
        store.SaveChallenge(win); store.SaveChallenge(win); store.SaveChallenge(round.Result(time));
        var history = store.ChallengeHistory();
        check(history.Count == 2 && history[1].Answers.Single().Guesses?.Count == 2 && history[0].Answers.Count == 6, "completed challenge persistence is idempotent with full guesses and answers");
        check(store.History().SequenceEqual(before) && store.GetWordHistory(a.Id).Due == schedule.Due && store.GetEntry(a.Id).Note == "保留笔记", "games leave daily statistics, due dates and notes intact");
        check(store.ChallengeHistory(DateOnly.FromDateTime(time.LocalDateTime.AddDays(1)), DateOnly.FromDateTime(time.LocalDateTime.AddDays(2))).Count == 0 && store.ChallengeHistory(limit: 1).Count == 1, "challenge history date windows and recent-round limit");

        string dbPath = Path.Combine(scratch, "games", "study.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_challenge BEFORE INSERT ON challenge_answers WHEN NEW.position=1 BEGIN SELECT RAISE(ABORT,'test'); END";
            command.ExecuteNonQuery();
            var failed = round.Result(time) with { Id = Guid.NewGuid() };
            bool rejected = false;
            try { store.SaveChallenge(failed); } catch (SqliteException) { rejected = true; }
            check(rejected && store.ChallengeHistory().Count == 2, "failed multi-answer save rolls back the entire round");
            command.CommandText = "DROP TRIGGER fail_challenge"; command.ExecuteNonQuery();
            store.SaveChallenge(failed);
            check(store.ChallengeHistory().Count == 3, "failed save can be retried successfully");
        }
        using var reopened = new StudyStore(Path.Combine(scratch, "games"), dictionary);
        check(reopened.ChallengeHistory().Count == 3 && reopened.GetEntry(a.Id).Note == "保留笔记", "restart keeps completed games and user notes");
    }
}
