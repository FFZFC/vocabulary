namespace Vocabulary.Core;

public enum Book { Cet4, Cet6 }
public enum Rating { Again = 0, Hard = 3, Good = 4, Easy = 5 }
public enum LearnedFilter { All, New, Learned, Forgotten, Notebook }
public enum StudyMode { Daily, Forgotten, Notebook }
public sealed record Word(string Id, string English, string Phonetic, string PartOfSpeech, string Meaning, bool Cet4);
public sealed record NotebookEntry(bool Saved, string Note);
public sealed record LibraryEntry(Word Word, NotebookEntry Entry);
public sealed record WordReview(DateTimeOffset At, Rating Rating, bool WasNew);
public sealed record WordHistory(DateTimeOffset? Due, int Attempts, IReadOnlyList<WordReview> Recent);
public sealed record Preferences(Book Book, int DailyGoal, bool Configured, bool WidgetVisible);
public sealed record Memory(int Successes, double Ease, int Days, DateTimeOffset Due);
public sealed record DayRecord(string Day, int NewWords, int Reviewed);
public sealed record Dashboard(int NewToday, int ReviewToday, int Due, int GoalRemaining, int Learned, int Total, int Streak, DateTimeOffset? NextDue)
{
    public int Unlearned => Total - Learned;
    public bool GoalReached => GoalRemaining == 0;
}
public enum CardFace { Front, Lookup, Forgotten }
public sealed record Card(Guid Token, Word Word, CardFace Face = CardFace.Front);
public sealed record ReviewReceipt(Guid Token, Word Word, Memory? Previous);
public sealed record DailyStats(DateOnly Date, int NewWords, int Reviewed, int Remembered, int Attempts, int DistinctWords)
{
    public double? Recall => Attempts == 0 ? null : (double)Remembered / Attempts;
}
public interface IReviewScheduler { Memory Schedule(Memory? previous, Rating rating, DateTimeOffset now); }
public sealed class Sm2Scheduler : IReviewScheduler
{
    public Memory Schedule(Memory? previous, Rating rating, DateTimeOffset now)
    {
        if (!Enum.IsDefined(rating)) throw new ArgumentOutOfRangeException(nameof(rating));
        double ease = previous?.Ease ?? 2.5;
        int misses = 5 - (int)rating;
        double nextEase = Math.Max(1.3, ease + 0.1 - misses * (0.08 + misses * 0.02));
        if (rating == Rating.Again) return new(0, nextEase, 0, now.AddMinutes(10));
        int successes = (previous?.Successes ?? 0) + 1;
        int days = successes switch { 1 => 1, 2 => 6, _ => (int)Math.Min(36500, Math.Ceiling(previous!.Days * ease)) };
        return new(successes, nextEase, days, now.AddDays(days));
    }
}
public interface ILexicon
{
    IReadOnlyList<Word> Search(Book book, string query, LearnedFilter filter, int offset, int limit);
}
public interface IPreferences
{
    Preferences Load();
    void Save(Preferences preferences);
}
public sealed class StudySession(StudyStore store, Func<DateTimeOffset> clock)
{
    private readonly HashSet<string> skipped = [];
    private ReviewReceipt? lastReview;
    public Card? Current { get; private set; }
    public StudyMode Mode { get; private set; }
    public bool Browsed { get; private set; }
    public Guid? UndoToken => lastReview?.Token;
    public event Action? Changed;
    public event Action? RecordsChanged;

    public void StartRound(StudyMode mode = StudyMode.Daily)
    {
        Mode = mode;
        lastReview = null;
        skipped.Clear();
        Browsed = false;
        Advance();
    }

    public void RefreshIfIdle()
    {
        if (Current is null && Mode == StudyMode.Daily) Advance();
    }

    public void BookmarkChanged(string id, bool saved)
    {
        if (Mode != StudyMode.Notebook) return;
        if (Current is null && saved || Current is { Face: CardFace.Front } card && card.Word.Id == id && !saved)
            Advance();
    }

    public void NewDay()
    {
        RecordsChanged?.Invoke();
        skipped.Clear();
        lastReview = null;
        Browsed = false;
        if (Current is null) Advance();
        else Changed?.Invoke();
    }

    private void Advance()
    {
        Word? word = store.Next(store.Load(), clock(), skipped, Mode);
        Current = word is null ? null : new(Guid.NewGuid(), word);
        Changed?.Invoke();
    }

    public void Remember(Guid token) => Grade(token, Rating.Good);
    public void Forget(Guid token) => Grade(token, Rating.Again);

    private void Grade(Guid token, Rating rating)
    {
        if (Current is not { Face: CardFace.Front } card || card.Token != token) return;
        var receipt = store.Submit(card, rating, clock());
        if (receipt is null) return;
        lastReview = receipt;
        RecordsChanged?.Invoke();
        if (rating == Rating.Again)
        {
            Current = card with { Face = CardFace.Forgotten };
            Changed?.Invoke();
        }
        else
        {
            if (Mode == StudyMode.Notebook) skipped.Add(card.Word.Id);
            Advance();
        }
    }

    public void Peek(Guid token)
    {
        if (Current is not { Face: CardFace.Front } card || card.Token != token) return;
        Current = card with { Face = CardFace.Lookup };
        Changed?.Invoke();
    }

    public void Continue(Guid token)
    {
        if (Current is not { } card || card.Token != token || card.Face == CardFace.Front) return;
        if (card.Face == CardFace.Lookup) Browsed = true;
        if (card.Face == CardFace.Lookup || Mode != StudyMode.Daily) skipped.Add(card.Word.Id);
        Advance();
    }

    public void Undo(Guid receiptToken)
    {
        if (lastReview is not { } receipt || receipt.Token != receiptToken) return;
        if (!store.Undo(receipt)) return;
        lastReview = null;
        skipped.Remove(receipt.Word.Id);
        RecordsChanged?.Invoke();
        Current = new(Guid.NewGuid(), receipt.Word);
        Changed?.Invoke();
    }
}
