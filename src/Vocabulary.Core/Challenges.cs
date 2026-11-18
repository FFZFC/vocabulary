using System.Text.RegularExpressions;

namespace Vocabulary.Core;

public enum ChallengeKind { Dictation, Wordle, Scramble, Matching }
public enum ChallengeSource { All, Learned, Notebook, Forgotten }
public enum AnswerOutcome { Correct, Assisted, Incorrect }
public enum LetterMark { Absent, Present, Exact }
public sealed record ChallengeOptions(ChallengeKind Kind, Book Book, ChallengeSource Source, int Count = 10, int Length = 5);
public sealed record ChallengeQuestion(Guid Id, Word Word, string Letters);
public sealed record WordleGuess(string Text, IReadOnlyList<LetterMark> Marks);
public sealed record ChallengeAnswer(Guid QuestionId, Word Word, string Input, AnswerOutcome Outcome, bool Hinted, int Attempts, IReadOnlyList<WordleGuess>? Guesses = null);
public sealed record ChallengeResult(Guid Id, ChallengeOptions Options, DateTimeOffset Completed, IReadOnlyList<ChallengeAnswer> Answers)
{
    public int Correct => Answers.Count(a => a.Outcome == AnswerOutcome.Correct);
    public int Assisted => Answers.Count(a => a.Outcome == AnswerOutcome.Assisted);
    public double Accuracy => Answers.Count == 0 ? 0 : (double)Correct / Answers.Count;
}

public static class ChallengeRules
{
    public static string Normalize(string text) => text.Trim().ToLowerInvariant();
    public static string Prompt(Word word) => Regex.Replace(word.Meaning, Regex.Escape(word.English), "____", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string MatchPrompt(Word word) => Prompt(word).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    public static bool Eligible(Word word, ChallengeOptions options)
    {
        if (!Regex.IsMatch(word.English, "^[a-z]+$", RegexOptions.CultureInvariant)) return false;
        return options.Kind switch
        {
            ChallengeKind.Wordle => word.English.Length == options.Length,
            ChallengeKind.Scramble => word.English.Length is >= 4 and <= 12,
            _ => true
        };
    }

    public static IReadOnlyList<LetterMark> Evaluate(string answer, string guess)
    {
        if (answer.Length != guess.Length) throw new ArgumentException("猜测长度须与答案一致。");
        var marks = new LetterMark[answer.Length];
        var remaining = new Dictionary<char, int>();
        for (int i = 0; i < answer.Length; i++)
        {
            if (answer[i] == guess[i]) marks[i] = LetterMark.Exact;
            else remaining[answer[i]] = remaining.GetValueOrDefault(answer[i]) + 1;
        }
        for (int i = 0; i < guess.Length; i++)
        {
            if (marks[i] == LetterMark.Exact || remaining.GetValueOrDefault(guess[i]) == 0) continue;
            marks[i] = LetterMark.Present;
            remaining[guess[i]]--;
        }
        return marks;
    }
}

/// <summary>One in-memory round. Completed results are saved separately from daily learning.</summary>
public sealed class ChallengeRound
{
    private readonly List<ChallengeAnswer> answers = [];
    private readonly List<WordleGuess> guesses = [];
    private readonly Dictionary<int, int> matchMistakes = [];
    private bool hinted;
    private int index;
    private int matchPage;
    private DateTimeOffset? completed;
    public Guid Id { get; } = Guid.NewGuid();
    public ChallengeOptions Options { get; }
    public IReadOnlyList<ChallengeQuestion> Questions { get; }
    public IReadOnlyList<ChallengeAnswer> Answers => answers.AsReadOnly();
    public IReadOnlyList<WordleGuess> Guesses => guesses.AsReadOnly();
    public IReadOnlyList<int> MatchingOrder { get; }
    public bool Finished { get; private set; }
    public bool Saved { get; set; }
    public string Input { get; set; } = "";
    public int? SelectedMatch { get; set; }
    public string Feedback { get; private set; } = "";
    public ChallengeQuestion Current => Questions[index];
    public ChallengeAnswer? CurrentAnswer => answers.FirstOrDefault(a => a.QuestionId == Current.Id);
    public bool Hinted => hinted;
    public int MatchPage => matchPage;
    public int MatchStart => matchPage * 5;
    public int MatchCount => Math.Min(5, Questions.Count - MatchStart);
    public bool GroupFinished => Enumerable.Range(MatchStart, MatchCount).All(IsMatched);
    public int Progress => Options.Kind == ChallengeKind.Matching ? answers.Count : index;
    public bool IsMatched(int at) => answers.Any(a => a.QuestionId == Questions[at].Id);

    public ChallengeRound(ChallengeOptions options, IEnumerable<Word> candidates, Random? random = null)
    {
        if (!Enum.IsDefined(options.Kind) || !Enum.IsDefined(options.Source) || !Enum.IsDefined(options.Book) ||
            options.Count is not (10 or 20) || options.Length is < 4 or > 7) throw new ArgumentOutOfRangeException(nameof(options));
        Options = options;
        random ??= Random.Shared;
        var pool = candidates.Where(w => ChallengeRules.Eligible(w, options)).DistinctBy(w => w.Id);
        if (options.Kind == ChallengeKind.Matching) pool = pool.DistinctBy(ChallengeRules.MatchPrompt);
        var words = pool.ToArray();
        random.Shuffle(words);
        int count = options.Kind == ChallengeKind.Wordle ? 1 : options.Count;
        Questions = Array.AsReadOnly(words.Take(count).Select(w => new ChallengeQuestion(Guid.NewGuid(), w, ShuffleLetters(w.English, random))).ToArray());
        if (Questions.Count == 0) throw new InvalidOperationException("当前范围没有符合要求的单词，请切换范围。");
        var order = new List<int>();
        for (int start = 0; start < Questions.Count; start += 5)
        {
            var group = Enumerable.Range(start, Math.Min(5, Questions.Count - start)).ToArray();
            random.Shuffle(group);
            order.AddRange(group);
        }
        MatchingOrder = order.AsReadOnly();
    }

    private static string ShuffleLetters(string word, Random random)
    {
        var letters = word.ToCharArray();
        random.Shuffle(letters);
        if (new string(letters) == word && letters.Distinct().Count() > 1)
        {
            int other = Array.FindIndex(letters, c => c != letters[0]);
            (letters[0], letters[other]) = (letters[other], letters[0]);
        }
        return new string(letters);
    }

    public void Hint(Guid question)
    {
        if (Finished || question != Current.Id || CurrentAnswer is not null || Options.Kind is not (ChallengeKind.Dictation or ChallengeKind.Scramble)) return;
        hinted = true;
        if (Options.Kind == ChallengeKind.Dictation) Feedback = $"首字母是 {Current.Word.English[0]}";
        else
        {
            string answer = Current.Word.English;
            string prefix = ChallengeRules.Normalize(Input);
            int length = 0;
            while (length < prefix.Length && length < answer.Length && prefix[length] == answer[length]) length++;
            Input = answer[..Math.Min(length + 1, answer.Length)];
            Feedback = "已补入下一个正确字母";
        }
    }

    public bool Submit(Guid question)
    {
        if (Finished || question != Current.Id || CurrentAnswer is not null || Options.Kind is not (ChallengeKind.Dictation or ChallengeKind.Scramble)) return false;
        string value = ChallengeRules.Normalize(Input);
        if (value.Length == 0) { Feedback = "请输入英文拼写，或选择查看答案。"; return false; }
        if (Options.Kind == ChallengeKind.Scramble && !value.Order().SequenceEqual(Current.Word.English.Order()))
        { Feedback = "请使用全部字母完成拼词。"; return false; }
        bool correct = value == Current.Word.English;
        answers.Add(new(Current.Id, Current.Word, Input, correct ? hinted ? AnswerOutcome.Assisted : AnswerOutcome.Correct : AnswerOutcome.Incorrect, hinted, 1));
        Feedback = correct ? hinted ? "提示后答对" : "回答正确" : "这次未答对";
        return true;
    }

    public void Reveal(Guid question)
    {
        if (Finished || question != Current.Id || CurrentAnswer is not null || Options.Kind == ChallengeKind.Matching) return;
        answers.Add(new(Current.Id, Current.Word, Input, AnswerOutcome.Incorrect, hinted, guesses.Count,
            Options.Kind == ChallengeKind.Wordle ? guesses.ToArray() : null));
        Feedback = "已揭晓答案";
        if (Options.Kind == ChallengeKind.Wordle) Finished = true;
    }

    public void Continue(Guid question)
    {
        if (Finished || question != Current.Id || CurrentAnswer is null || Options.Kind is not (ChallengeKind.Dictation or ChallengeKind.Scramble)) return;
        if (index + 1 == Questions.Count) Finished = true;
        else index++;
        hinted = false; Input = ""; Feedback = "";
    }

    public bool Guess(Guid question, IReadOnlySet<string> dictionary)
    {
        if (Finished || Options.Kind != ChallengeKind.Wordle || question != Current.Id) return false;
        string value = ChallengeRules.Normalize(Input);
        if (value.Length != Options.Length || value.Any(c => c is < 'a' or > 'z'))
        { Feedback = $"请输入 {Options.Length} 个英文字母。"; return false; }
        if (!dictionary.Contains(value) && value != Current.Word.English) { Feedback = "本地词典未收录这个词，请换一个。"; return false; }
        if (guesses.Any(g => g.Text == value)) { Feedback = "已经猜过这个词，请换一个。"; return false; }
        guesses.Add(new(value, ChallengeRules.Evaluate(Current.Word.English, value)));
        Input = "";
        bool won = value == Current.Word.English;
        if (won || guesses.Count == 6)
        {
            answers.Add(new(Current.Id, Current.Word, value, won ? AnswerOutcome.Correct : AnswerOutcome.Incorrect, false, guesses.Count, guesses.ToArray()));
            Finished = true;
            Feedback = won ? $"猜中了！用了 {guesses.Count} 次机会" : "本局机会已用完";
        }
        else Feedback = $"还剩 {6 - guesses.Count} 次机会";
        return true;
    }

    public IReadOnlyDictionary<char, LetterMark> KeyboardMarks()
    {
        var marks = new Dictionary<char, LetterMark>();
        foreach (var guess in guesses)
            for (int i = 0; i < guess.Text.Length; i++)
                if (!marks.TryGetValue(guess.Text[i], out var previous) || previous < guess.Marks[i]) marks[guess.Text[i]] = guess.Marks[i];
        return marks;
    }

    public void Match(int page, int left, int right)
    {
        if (Finished || Options.Kind != ChallengeKind.Matching || page != matchPage ||
            left < MatchStart || left >= MatchStart + MatchCount || right < MatchStart || right >= MatchStart + MatchCount ||
            IsMatched(left) || IsMatched(right)) return;
        SelectedMatch = null;
        if (left != right)
        {
            matchMistakes[left] = matchMistakes.GetValueOrDefault(left) + 1;
            Feedback = $"{Questions[left].Word.English} 与这条释义不匹配，再试一次。";
            return;
        }
        var q = Questions[left];
        int mistakes = matchMistakes.GetValueOrDefault(left);
        answers.Add(new(q.Id, q.Word, q.Word.English, mistakes == 0 ? AnswerOutcome.Correct : AnswerOutcome.Incorrect, false, mistakes + 1));
        Feedback = $"✓ {q.Word.English} 配对成功";
        if (answers.Count == Questions.Count) Finished = true;
    }

    public void NextGroup(int page)
    {
        if (Finished || Options.Kind != ChallengeKind.Matching || page != matchPage || !GroupFinished) return;
        matchPage++; SelectedMatch = null; Feedback = "";
    }

    public ChallengeResult Result(DateTimeOffset now)
    {
        if (!Finished) throw new InvalidOperationException("完成本轮后保存成绩。");
        completed ??= now;
        return new(Id, Options, completed.Value, answers.ToArray());
    }
}
