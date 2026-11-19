using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Vocabulary.Core;

public sealed partial class StudyStore : ILexicon, IPreferences, IDisposable
{
    private readonly SqliteConnection db;
    private readonly string databasePath;
    private readonly IReviewScheduler scheduler;
    public event Action<string, bool>? BookmarkChanged;
    public event Action<string>? NoteChanged;
    private const string Fields = "w.id,w.word,w.phonetic,w.pos,w.meaning,w.cet4";
    private const string InBook = "($book=1 OR w.cet4=1)";
    public StudyStore(string dataRoot, string dictionary, IReviewScheduler? scheduler = null)
    {
        this.scheduler = scheduler ?? new Sm2Scheduler();
        Directory.CreateDirectory(dataRoot);
        string file = databasePath = Path.GetFullPath(Path.Combine(dataRoot, "study.db"));
        db = new(new SqliteConnectionStringBuilder { DataSource = new Uri(Path.GetFullPath(file)).AbsoluteUri, Pooling = false }.ToString());
        db.Open();
        try
        {
            long version = Number("PRAGMA user_version");
            if (version > 3) throw new InvalidDataException("学习数据库来自更新的版本，请使用新版程序。");
            if (version < 3)
            {
                if (Number("SELECT COUNT(*) FROM sqlite_master WHERE type='table'") > 0)
                {
                    using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file + "." + DateTime.UtcNow.Ticks + ".bak", Pooling = false }.ToString());
                    backup.Open();
                    db.BackupDatabase(backup);
                }
                using var tx = db.BeginTransaction();
                using var create = db.CreateCommand();
                create.Transaction = tx;
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS progress(id TEXT PRIMARY KEY,successes INTEGER,ease REAL,days INTEGER,due INTEGER);
                    CREATE INDEX IF NOT EXISTS progress_due ON progress(due);
                    CREATE TABLE IF NOT EXISTS reviews(token TEXT PRIMARY KEY,id TEXT NOT NULL,day TEXT NOT NULL,at INTEGER NOT NULL,rating INTEGER NOT NULL,is_new INTEGER NOT NULL);
                    CREATE INDEX IF NOT EXISTS reviews_day ON reviews(day,id);
                    CREATE INDEX IF NOT EXISTS reviews_word ON reviews(id,at DESC);
                    CREATE TABLE IF NOT EXISTS bookmarks(id TEXT PRIMARY KEY);
                    CREATE TABLE IF NOT EXISTS notes(id TEXT PRIMARY KEY, text TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS challenge_rounds(id TEXT PRIMARY KEY,day TEXT NOT NULL,at INTEGER NOT NULL,kind INTEGER NOT NULL,book INTEGER NOT NULL,options TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS challenges_day ON challenge_rounds(day,kind);
                    CREATE TABLE IF NOT EXISTS challenge_answers(round_id TEXT NOT NULL,position INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(round_id,position),FOREIGN KEY(round_id) REFERENCES challenge_rounds(id));
                    PRAGMA user_version=3;
                    """;
                create.ExecuteNonQuery();
                tx.Commit();
            }
            using var attach = Cmd("ATTACH DATABASE $uri AS lex", ("$uri", new Uri(Path.GetFullPath(dictionary)).AbsoluteUri + "?mode=ro&immutable=1"));
            attach.ExecuteNonQuery();
        }
        catch { db.Dispose(); throw; }
    }
    private SqliteCommand Cmd(string sql, params (string, object)[] args)
    {
        var c = db.CreateCommand();
        c.CommandText = sql;
        foreach (var (key, value) in args) c.Parameters.AddWithValue(key, value);
        return c;
    }
    private long Number(string sql, params (string, object)[] args)
    {
        using var c = Cmd(sql, args);
        return Convert.ToInt64(c.ExecuteScalar());
    }
    public string? Setting(string key)
    {
        using var c = Cmd("SELECT value FROM settings WHERE key=$k", ("$k", key));
        return c.ExecuteScalar() as string;
    }
    public void Set(string key, string value, SqliteTransaction? tx = null)
    {
        using var c = Cmd("INSERT OR REPLACE INTO settings VALUES($k,$v)", ("$k", key), ("$v", value));
        c.Transaction = tx;
        c.ExecuteNonQuery();
    }
    public Preferences Load() => new(Setting("book") == "6" ? Book.Cet6 : Book.Cet4,
        int.TryParse(Setting("limit"), out int n) && n > 0 ? n : 20, Setting("configured") == "1", Setting("widget") != "0");
    public void Save(Preferences p)
    {
        if (p.DailyGoal < 1) throw new ArgumentOutOfRangeException(nameof(p));
        using var tx = db.BeginTransaction();
        Set("book", p.Book == Book.Cet4 ? "4" : "6", tx);
        Set("limit", p.DailyGoal.ToString(), tx);
        Set("configured", p.Configured ? "1" : "0", tx);
        Set("widget", p.WidgetVisible ? "1" : "0", tx);
        tx.Commit();
    }
    private static Word Read(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5) != 0);
    public IReadOnlyList<Word> Search(Book book, string query, LearnedFilter filter, int offset, int limit)
        => SearchEntries(book, query, filter, offset, limit).Select(entry => entry.Word).ToArray();

    public IReadOnlyList<LibraryEntry> SearchEntries(Book book, string query, LearnedFilter filter, int offset, int limit)
    {
        using var c = Cmd($"""
            SELECT {Fields},b.id IS NOT NULL,coalesce(n.text,'') FROM lex.words w LEFT JOIN progress p ON w.id=p.id
            LEFT JOIN bookmarks b ON b.id=w.id LEFT JOIN notes n ON n.id=w.id
            WHERE {InBook} AND ($filter=0 OR ($filter=1 AND p.id IS NULL) OR ($filter=2 AND p.id IS NOT NULL)
                OR ($filter=3 AND p.successes=0) OR ($filter=4 AND b.id IS NOT NULL))
            AND (instr(w.id,lower($q))>0 OR instr(w.meaning,$q)>0 OR ($filter=4 AND instr(lower(n.text),lower($q))>0))
            ORDER BY (w.id=lower($q)) DESC,(instr(w.id,lower($q))=1) DESC,CASE WHEN $filter=4 THEN b.rowid END DESC,w.id LIMIT $limit OFFSET $offset
            """, ("$book", (int)book), ("$filter", (int)filter), ("$q", query.Trim()), ("$limit", limit), ("$offset", offset));
        using var r = c.ExecuteReader();
        var results = new List<LibraryEntry>();
        while (r.Read()) results.Add(new(Read(r), new(r.GetBoolean(6), r.GetString(7))));
        return results;
    }
    public NotebookEntry GetEntry(string id)
    {
        using var c = Cmd("SELECT EXISTS(SELECT 1 FROM bookmarks WHERE id=$id),coalesce((SELECT text FROM notes WHERE id=$id),'')", ("$id", id));
        using var r = c.ExecuteReader();
        r.Read();
        return new(r.GetBoolean(0), r.GetString(1));
    }

    public int BookmarkCount(Book book) => (int)Number($"SELECT count(*) FROM bookmarks b JOIN lex.words w ON w.id=b.id WHERE {InBook}", ("$book", (int)book));

    public void SetBookmark(string id, bool saved)
    {
        using var c = Cmd(saved ? "INSERT OR IGNORE INTO bookmarks(id) VALUES($id)" : "DELETE FROM bookmarks WHERE id=$id", ("$id", id));
        if (c.ExecuteNonQuery() != 0) BookmarkChanged?.Invoke(id, saved);
    }

    public void SetNote(string id, string text)
    {
        string note = text.Trim();
        using var c = note.Length == 0 ? Cmd("DELETE FROM notes WHERE id=$id", ("$id", id))
            : Cmd("INSERT INTO notes(id,text) VALUES($id,$note) ON CONFLICT(id) DO UPDATE SET text=excluded.text WHERE text<>excluded.text",
                ("$id", id), ("$note", note));
        if (c.ExecuteNonQuery() != 0) NoteChanged?.Invoke(id);
    }

    public WordHistory GetWordHistory(string id)
    {
        using var c = Cmd("SELECT (SELECT due FROM progress WHERE id=$id),(SELECT count(*) FROM reviews WHERE id=$id)", ("$id", id));
        DateTimeOffset? due;
        int attempts;
        using (var r = c.ExecuteReader())
        {
            r.Read();
            due = r.IsDBNull(0) ? null : DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0));
            attempts = r.GetInt32(1);
        }
        using var recent = Cmd("SELECT at,rating,is_new FROM reviews WHERE id=$id ORDER BY at DESC,rowid DESC LIMIT 5", ("$id", id));
        var reviews = new List<WordReview>();
        using var reader = recent.ExecuteReader();
        while (reader.Read()) reviews.Add(new(DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)), (Rating)reader.GetInt32(1), reader.GetBoolean(2)));
        return new(due, attempts, reviews);
    }
    private static string LocalDay(DateTimeOffset now) => now.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public Word? Next(Preferences p, DateTimeOffset now, IReadOnlySet<string> skipped, StudyMode mode)
    {
        string candidates = mode switch
        {
            StudyMode.Forgotten => $"SELECT {Fields} FROM lex.words w JOIN progress p ON p.id=w.id WHERE {InBook} AND p.successes=0 ORDER BY p.due,w.id",
            StudyMode.Notebook => $"SELECT {Fields} FROM bookmarks b JOIN lex.words w ON b.id=w.id WHERE {InBook} ORDER BY b.rowid DESC",
            _ => $"""
              WITH new_words AS (
                  SELECT {Fields},w.frequency FROM lex.words w
                  WHERE {InBook} AND NOT EXISTS(SELECT 1 FROM progress p WHERE p.id=w.id)
              ), candidates AS (
                  SELECT {Fields},0 AS fresh,p.due AS priority FROM lex.words w JOIN progress p ON p.id=w.id
                  WHERE {InBook} AND p.due<=$now
                  UNION ALL
                  SELECT id,word,phonetic,pos,meaning,cet4,1,coalesce(frequency,2147483647) FROM new_words
              )
              SELECT * FROM candidates ORDER BY fresh,priority,id
              """
        };
        using var c = Cmd(candidates, ("$book", (int)p.Book));
        if (mode == StudyMode.Daily)
        {
            c.Parameters.AddWithValue("$now", now.ToUnixTimeSeconds());
        }
        using var r = c.ExecuteReader();
        while (r.Read())
            if (!skipped.Contains(r.GetString(0))) return Read(r);
        return null;
    }
    public ReviewReceipt? Submit(Card card, Rating rating, DateTimeOffset now)
    {
        using var tx = db.BeginTransaction();
        using var previous = Cmd("SELECT successes,ease,days,due FROM progress WHERE id=$id", ("$id", card.Word.Id));
        previous.Transaction = tx;
        Memory? memory = null;
        using (var r = previous.ExecuteReader())
            if (r.Read()) memory = new(r.GetInt32(0), r.GetDouble(1), r.GetInt32(2), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)));
        var next = scheduler.Schedule(memory, rating, now);
        using var log = Cmd("INSERT INTO reviews VALUES($token,$id,$day,$at,$rating,$new) ON CONFLICT(token) DO NOTHING",
            ("$token", card.Token.ToString()), ("$id", card.Word.Id), ("$day", LocalDay(now)), ("$at", now.ToUnixTimeSeconds()), ("$rating", (int)rating), ("$new", memory is null ? 1 : 0));
        log.Transaction = tx;
        if (log.ExecuteNonQuery() == 0) return null;
        WriteProgress(card.Word.Id, next, tx);
        tx.Commit();
        return new(card.Token, card.Word, memory);
    }

    private void WriteProgress(string id, Memory m, SqliteTransaction tx)
    {
        using var save = Cmd("INSERT OR REPLACE INTO progress VALUES($id,$n,$e,$d,$due)",
            ("$id", id), ("$n", m.Successes), ("$e", m.Ease), ("$d", m.Days), ("$due", m.Due.ToUnixTimeSeconds()));
        save.Transaction = tx;
        save.ExecuteNonQuery();
    }

    public bool Undo(ReviewReceipt receipt)
    {
        using var tx = db.BeginTransaction();
        using var log = Cmd("DELETE FROM reviews WHERE token=$token AND rowid=(SELECT max(rowid) FROM reviews)",
            ("$token", receipt.Token.ToString()));
        log.Transaction = tx;
        if (log.ExecuteNonQuery() != 1) return false;
        if (receipt.Previous is { } previous) WriteProgress(receipt.Word.Id, previous, tx);
        else
        {
            using var remove = Cmd("DELETE FROM progress WHERE id=$id", ("$id", receipt.Word.Id));
            remove.Transaction = tx;
            remove.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    public IReadOnlyList<DailyStats> Statistics(DateOnly end, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var start = end.AddDays(1 - days);
        string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var query = Cmd("""
            SELECT day,sum(is_new),count(DISTINCT CASE WHEN is_new=0 THEN id END),
                   sum(CASE WHEN rating IN (3,4,5) THEN 1 ELSE 0 END),count(*),count(DISTINCT id)
            FROM reviews WHERE day BETWEEN $start AND $end GROUP BY day
            """, ("$start", Iso(start)), ("$end", Iso(end)));
        using var r = query.ExecuteReader();
        var results = Enumerable.Range(0, days).Select(i => new DailyStats(start.AddDays(i), 0, 0, 0, 0, 0)).ToArray();
        while (r.Read())
        {
            var date = DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            results[date.DayNumber - start.DayNumber] = new(date, r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5));
        }
        return results;
    }
    public List<DayRecord> History()
    {
        using var c = Cmd("""
            SELECT day,sum(is_new),count(DISTINCT CASE WHEN is_new=0 THEN id END)
            FROM reviews GROUP BY day ORDER BY day DESC
            """);
        using var r = c.ExecuteReader();
        var days = new List<DayRecord>();
        while (r.Read()) days.Add(new(r.GetString(0), r.GetInt32(1), r.GetInt32(2)));
        return days;
    }
    public Dashboard Summary(Preferences p, DateTimeOffset now)
    {
        string today = LocalDay(now);
        using var daily = Cmd("""
            SELECT coalesce(sum(is_new),0),count(DISTINCT CASE WHEN is_new=0 THEN id END),count(*)
            FROM reviews WHERE day=$today
            """, ("$today", today));
        int newToday, reviewed;
        DateTime cursor;
        using (var day = daily.ExecuteReader())
        {
            day.Read();
            newToday = day.GetInt32(0);
            reviewed = day.GetInt32(1);
            cursor = now.LocalDateTime.Date.AddDays(day.GetInt32(2) == 0 ? -1 : 0);
        }
        int streak = 0;
        using var dates = Cmd("SELECT DISTINCT day FROM reviews WHERE day<=$today ORDER BY day DESC", ("$today", today));
        using (var days = dates.ExecuteReader())
            while (days.Read() && days.GetString(0) == cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }
        using var c = Cmd($"""
            SELECT count(*),count(p.id),count(CASE WHEN p.due<=$now THEN 1 END),min(CASE WHEN p.due>$now THEN p.due END)
            FROM lex.words w LEFT JOIN progress p ON p.id=w.id WHERE {InBook}
            """, ("$book", (int)p.Book), ("$now", now.ToUnixTimeSeconds()));
        using var r = c.ExecuteReader();
        r.Read();
        return new(newToday, reviewed, r.GetInt32(2), Math.Max(0, p.DailyGoal - newToday), r.GetInt32(1), r.GetInt32(0), streak, r.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)));
    }
    public void Dispose() => db.Dispose();
}
