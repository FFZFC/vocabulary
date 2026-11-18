using Vocabulary.Core;
using Microsoft.Data.Sqlite;

string dict = Path.GetFullPath(args[0]);
string scratch = Path.Combine(Path.GetTempPath(), "VocabularyChecks-" + Guid.NewGuid().ToString("N"));
int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    Console.WriteLine("PASS " + name); checks++;
}
var t = new DateTimeOffset(DateTime.Today.AddHours(9));
var scheduler = new Sm2Scheduler();
foreach (var score in Enum.GetValues<Rating>())
{
    var m = scheduler.Schedule(null, score, t);
    Check(m.Due == (score == Rating.Again ? t.AddMinutes(10) : t.AddDays(1)), score + " first interval");
}
var first = scheduler.Schedule(null, Rating.Good, t);
var second = scheduler.Schedule(first, Rating.Good, first.Due);
Check(second.Days == 6 && scheduler.Schedule(second, Rating.Good, second.Due).Days == 15, "SM-2 progression");
var reset = scheduler.Schedule(second, Rating.Again, t);
Check(reset.Successes == 0 && scheduler.Schedule(reset, Rating.Hard, reset.Due).Days == 1, "relearn");

using (var store = new StudyStore(scratch, dict))
{
    var p = new Preferences(Book.Cet4, 1, true, true);
    store.Save(p);
    var session = new StudySession(store, () => t);
    session.StartRound();
    var firstCard = session.Current!;
    session.Peek(firstCard.Token);
    session.Remember(firstCard.Token);
    Check(session.Current!.Face == CardFace.Lookup && store.History().Count == 0, "lookup is neutral even on stale grading input");
    session.Continue(firstCard.Token);
    Check(session.Current is not null && session.Current.Word != firstCard.Word && session.Browsed && store.Summary(p, t).GoalRemaining == 1, "neutral lookup skips to another word without changing goal");
    session.StartRound();
    Check(session.Current!.Word == firstCard.Word && session.Current.Token != firstCard.Token, "new round restores skipped word with fresh identity");
    var card = session.Current;
    session.Forget(card.Token);
    session.Forget(card.Token);
    var day = DateOnly.FromDateTime(t.LocalDateTime);
    var data = store.Statistics(day, 7);
    Check(session.Current!.Face == CardFace.Forgotten && data[^1].Attempts == 1 && data[^1].Recall == 0, "forget commits immediately exactly once");
    Check(data[0].Recall is null && data[0].DistinctWords == 0, "empty dates are gaps");
    Check(store.Search(Book.Cet4, "", LearnedFilter.Forgotten, 0, 10).Single() == card.Word, "forgotten list populated");
    Check(store.Summary(p, t).NextDue == t.AddMinutes(10), "ten minute due date");
    var receipt = session.UndoToken!.Value;
    session.Undo(receipt);
    Check(session.Current!.Word == card.Word && session.Current.Face == CardFace.Front && store.History().Count == 0, "undo first grade removes progress and log");
    Check(store.Summary(p, t).GoalRemaining == 1 && store.Search(Book.Cet4, "", LearnedFilter.Forgotten, 0, 5).Count == 0, "undo restores goal progress and forgotten list");
    card = session.Current;
    session.Remember(card.Token);
    session.Remember(card.Token);
    session.Continue(card.Token);
    Check(session.Current is not null && session.Current.Word != card.Word && store.Statistics(day, 1)[0].Attempts == 1 && store.Summary(p, t).GoalReached, "reaching goal continues to next word and rejects stale input");
    session.Undo(session.UndoToken!.Value);
    session.Forget(session.Current!.Token);
    session.Continue(session.Current.Token);
    Check(session.Current is not null && session.Current.Word != card.Word && store.Statistics(day, 1)[0].Attempts == 1, "forgotten definition continues past goal without scoring twice");
    t = t.AddMinutes(11);
    session.StartRound();
    Check(session.Current!.Word == card.Word, "overdue word first");
    var overdue = session.Current;
    session.Remember(overdue.Token);
    var after = store.Statistics(day, 1)[0];
    Check(after.NewWords == 1 && after.Reviewed == 1 && after.DistinctWords == 1 && after.Attempts == 2 && after.Recall == .5, "statistics distinguish attempts and distinct words");
    session.Undo(session.UndoToken!.Value);
    Check(store.Summary(p, t).Due == 1 && store.Statistics(day, 1)[0].Reviewed == 0, "undo review restores previous due date and log");
    session.StartRound(StudyMode.Forgotten);
    session.Peek(session.Current!.Token);
    session.Continue(session.Current.Token);
    Check(session.Current is null && store.Search(Book.Cet4, "", LearnedFilter.Forgotten, 0, 2).Count == 1, "practice lookup keeps forgotten membership");
    session.StartRound(StudyMode.Forgotten);
    session.Forget(session.Current!.Token);
    session.Continue(session.Current.Token);
    Check(session.Current is null, "failed practice word shown once per round");
    session.StartRound(StudyMode.Forgotten);
    Check(session.UndoToken is null, "new round invalidates undo");
    session.Remember(session.Current!.Token);
    Check(store.Search(Book.Cet4, "", LearnedFilter.Forgotten, 0, 5).Count == 0, "practice success resolves word");
    var six = p with { Book = Book.Cet6 };
    store.Save(six);
    session.StartRound();
    Check(session.UndoToken is null && store.Search(Book.Cet6, card.Word.English, LearnedFilter.Learned, 0, 10).Any(w => w.Id == card.Word.Id), "cross-book progress retained");
    t = t.AddDays(1);
    session.NewDay();
    session.StartRound();
    Check(store.Summary(six, t).GoalRemaining == 1 && session.Current!.Word == card.Word, "local date goal reset and due priority");
    // Old versions wrote these same rating values and progress rows.
    store.Submit(new(Guid.NewGuid(), card.Word), Rating.Hard, t);
    store.Submit(new(Guid.NewGuid(), card.Word), Rating.Easy, t);
    data = store.Statistics(day.AddDays(1), 7);
    Check(data[^1].Remembered == 2 && data[^1].Reviewed == 1 && data[^1].Recall == 1, "legacy Hard and Easy count as recalled");
    Check(store.Summary(six, t).Total == 5805 && store.Summary(p, t).Total == 3849, "pinned corpus membership");
    Check(store.Search(Book.Cet4, "放弃", LearnedFilter.All, 0, 10).Count > 0, "Chinese lookup");
}
using (var reopened = new StudyStore(scratch, dict))
    Check(reopened.Load().Book == Book.Cet6 && reopened.History().Count == 2, "persistent settings and reviews");

string continuousRoot = Path.Combine(scratch, "continuous");
string tinyDictionary = Path.Combine(scratch, "tiny.db");
using (var tiny = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tinyDictionary, Pooling = false }.ToString()))
{
    tiny.Open();
    using var command = tiny.CreateCommand();
    command.CommandText = """
        ATTACH DATABASE $dictionary AS original;
        CREATE TABLE words AS SELECT * FROM original.words WHERE cet4=1 ORDER BY id LIMIT 5;
        """;
    command.Parameters.AddWithValue("$dictionary", dict);
    command.ExecuteNonQuery();
}
var moment = new DateTimeOffset(DateTime.Today.AddHours(9));
using (var unlimited = new StudyStore(continuousRoot, tinyDictionary))
{
    var goal = new Preferences(Book.Cet4, 1, true, true);
    unlimited.Save(goal);
    var session = new StudySession(unlimited, () => moment);
    session.StartRound();
    var seen = new HashSet<string>();
    for (int i = 0; i < 5; i++)
    {
        var current = session.Current!;
        Check(current is not null && seen.Add(current.Word.Id), "unlimited candidate after neutral skip " + i);
        session.Peek(current!.Token);
        session.Continue(current.Token);
    }
    Check(session.Current is null && session.Browsed && unlimited.Summary(goal, moment).Unlearned == 5,
        "browsing round ends only after every candidate was skipped");
    session.StartRound();
    session.Remember(session.Current!.Token);
    Check(session.Current is not null && unlimited.Summary(goal, moment) is { NewToday: 1, GoalRemaining: 0, Unlearned: 4 },
        "goal of one is reached while four more words remain");
    var lookupCard = session.Current!;
    session.Peek(lookupCard.Token);
    session.Continue(lookupCard.Token);
    Check(session.Current!.Word != lookupCard.Word && unlimited.Summary(goal, moment).NewToday == 1,
        "neutral lookup continues after reaching goal");
    var third = session.Current!;
    session.Forget(third.Token);
    Check(unlimited.Summary(goal, moment).NewToday == 2 && session.Current!.Face == CardFace.Forgotten,
        "over-goal forgotten word is counted");
    session.Undo(session.UndoToken!.Value);
    Check(unlimited.Summary(goal, moment).NewToday == 1 && session.Current!.Word == third.Word,
        "undo over-goal grade restores real count");
    session.Remember(session.Current!.Token);
    Check(unlimited.Statistics(DateOnly.FromDateTime(moment.LocalDateTime), 1)[0].NewWords == 2,
        "chart records over-goal words");
    unlimited.Save(goal with { DailyGoal = 800 });
    Check(unlimited.Load().DailyGoal == 800 && unlimited.Setting("limit") == "800", "positive target above old cap uses legacy setting key");
    unlimited.Save(goal);
}
using (var unlimited = new StudyStore(continuousRoot, tinyDictionary))
{
    var session = new StudySession(unlimited, () => moment);
    session.StartRound();
    Check(unlimited.Summary(unlimited.Load(), moment).NewToday == 2 && session.Current is not null,
        "restart after exceeding goal continues learning");
    while (session.Current is { } current) session.Remember(current.Token);
    var summary = unlimited.Summary(unlimited.Load(), moment);
    Check(summary.NewToday == 5 && summary.Unlearned == 0 && summary.GoalReached && !session.Browsed,
        "round ends at actual vocabulary exhaustion");
    moment = moment.AddDays(1);
    session.NewDay();
    summary = unlimited.Summary(unlimited.Load(), moment);
    Check(summary.NewToday == 0 && summary.GoalRemaining == 1 && summary.Unlearned == 0 && summary.Due == 5 && session.Current is not null,
        "new day resets goal while learned words remain scheduled for review");
    session.Remember(session.Current!.Token);
    Check(unlimited.Summary(unlimited.Load(), moment).NewToday == 0 && session.Current is not null,
        "reviews do not consume new-word goal or stop the next review");
}
string legacyRoot = Path.Combine(scratch, "legacy");
Directory.CreateDirectory(legacyRoot);
string legacyFile = Path.Combine(legacyRoot, "study.db");
using (var legacy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = legacyFile, Pooling = false }.ToString()))
{
    legacy.Open();
    using var seed = legacy.CreateCommand();
    seed.CommandText = """
        CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        CREATE TABLE progress(id TEXT PRIMARY KEY,successes INTEGER,ease REAL,days INTEGER,due INTEGER);
        CREATE TABLE reviews(token TEXT PRIMARY KEY,id TEXT NOT NULL,day TEXT NOT NULL,at INTEGER NOT NULL,rating INTEGER NOT NULL,is_new INTEGER NOT NULL);
        INSERT INTO settings VALUES('limit','9');
        INSERT INTO progress VALUES('abandon',1,2.5,1,1000000);
        INSERT INTO reviews VALUES('old','abandon','1970-01-01',10,3,1);
        PRAGMA user_version=1;
        """;
    seed.ExecuteNonQuery();
}
using (var journal = new StudyStore(legacyRoot, dict))
{
    var backup = Directory.GetFiles(legacyRoot, "*.bak").Single();
    using var original = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    original.Open();
    using var version = original.CreateCommand();
    version.CommandText = "PRAGMA user_version";
    Check(Convert.ToInt32(version.ExecuteScalar()) == 1 && journal.Load().DailyGoal == 9, "migration keeps old database snapshot and settings");
    var oldRecord = journal.GetWordHistory("abandon");
    Check(oldRecord.Due?.ToUnixTimeSeconds() == 1000000 && oldRecord.Recent.Single() is { Rating: Rating.Hard, WasNew: true }, "legacy review and schedule in word details");
    var session = new StudySession(journal, () => moment);
    journal.BookmarkChanged += session.BookmarkChanged;
    session.StartRound(StudyMode.Notebook);
    Check(session.Current is null, "empty notebook has no practice candidates");
    journal.SetBookmark("abandon", true);
    journal.SetNote("abandon", "  联想 LEAVE  ");
    var a = journal.Search(Book.Cet4, "abandon", LearnedFilter.Notebook, 0, 1).Single();
    var b = journal.Search(Book.Cet4, "ability", LearnedFilter.All, 0, 1).Single();
    Check(session.Current?.Word.Id == a.Id && journal.GetEntry(a.Id) == new NotebookEntry(true, "联想 LEAVE"), "bookmark wakes notebook practice and note is trimmed");
    journal.SetBookmark(b.Id, true);
    journal.SetBookmark(b.Id, true);
    Check(journal.BookmarkCount(Book.Cet4) == 2 && journal.Search(Book.Cet6, "leave", LearnedFilter.Notebook, 0, 5).Contains(a), "saved words deduplicated and shared notes searchable across books");
    session.StartRound(StudyMode.Notebook);
    var newest = session.Current!;
    Check(newest.Word == b && journal.GetWordHistory(b.Id).Attempts == 0, "newest saved first and bookmarking leaves scoring untouched");
    session.Remember(newest.Token);
    session.Remember(newest.Token);
    Check(session.Current?.Word == a && journal.GetWordHistory(b.Id).Attempts == 1 && journal.GetEntry(b.Id).Saved, "notebook remember advances once and keeps bookmark");
    session.Undo(session.UndoToken!.Value);
    Check(session.Current?.Word == b && journal.GetWordHistory(b.Id).Due is null, "undo in notebook restores first study");
    session.Peek(session.Current!.Token);
    session.Continue(session.Current.Token);
    session.Forget(session.Current!.Token);
    session.Continue(session.Current.Token);
    Check(session.Current is null && journal.GetWordHistory(b.Id).Attempts == 0 && journal.GetWordHistory(a.Id).Due == moment.AddMinutes(10), "notebook lookup and forgetting each visit once");
    session.StartRound(StudyMode.Notebook);
    journal.SetBookmark(b.Id, false);
    Check(session.Current?.Word == a && journal.BookmarkCount(Book.Cet4) == 1, "removing current notebook front switches card");
    journal.SetBookmark(a.Id, false);
    Check(session.Current is null && journal.GetEntry(a.Id) == new NotebookEntry(false, "联想 LEAVE") && journal.GetWordHistory(a.Id).Attempts == 2, "removal preserves notes and history");
    var advanced = journal.Search(Book.Cet6, "", LearnedFilter.All, 0, 5805).First(w => !w.Cet4);
    journal.SetBookmark(advanced.Id, true);
    Check(journal.BookmarkCount(Book.Cet4) == 0 && journal.BookmarkCount(Book.Cet6) == 1 && session.Current is null, "notebook practice respects book membership");
    journal.SetBookmark(a.Id, true);
    foreach (var rating in new[]{Rating.Easy, Rating.Hard, Rating.Good, Rating.Again, Rating.Good})
        journal.Submit(new(Guid.NewGuid(), a), rating, moment);
    var recent = journal.GetWordHistory(a.Id);
    Check(recent.Attempts == 7 && recent.Recent.Count == 5 && recent.Recent[0].Rating == Rating.Good && recent.Recent[1].Rating == Rating.Again, "history counts all attempts and orders last five with same timestamp");
}
using (var journal = new StudyStore(legacyRoot, dict))
{
    Check(journal.GetEntry("abandon") == new NotebookEntry(true, "联想 LEAVE") && Directory.GetFiles(legacyRoot, "*.bak").Length == 1, "notes persist and migration only backs up once");
    journal.SetNote("abandon", "");
    Check(journal.GetEntry("abandon") == new NotebookEntry(true, "") && journal.GetWordHistory("abandon").Attempts == 7, "clearing note preserves bookmark and scores");
}
ChallengeChecks.Run(dict, scratch, Check);
Console.WriteLine($"{checks} checks passed.");
Directory.Delete(scratch, true);
