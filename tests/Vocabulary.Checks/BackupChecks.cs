using Microsoft.Data.Sqlite;
using Vocabulary.Core;

internal static class BackupChecks
{
    internal static void Run(string dictionary, string scratch, Action<bool, string> check)
    {
        var sourceRoot = Path.Combine(scratch, "backup-source");
        var targetRoot = Path.Combine(scratch, "backup-target");
        string backupPath = Path.Combine(scratch, "学习备份.lexipane");
        using var source = new StudyStore(sourceRoot, dictionary);
        using var target = new StudyStore(targetRoot, dictionary);
        var word = source.Search(Book.Cet4, "abandon", LearnedFilter.All, 0, 1).Single();
        source.Save(new(Book.Cet6, 23, true, false));
        source.Set("bounds", "100,100,350,200,96");
        source.SetBookmark(word.Id, true);
        source.SetNote(word.Id, "旧版笔记");
        source.Submit(new(Guid.NewGuid(), word), Rating.Good, DateTimeOffset.Now);
        var challenge = new ChallengeRound(new(ChallengeKind.Dictation, Book.Cet4, ChallengeSource.All), [word]);
        challenge.Input = word.English;
        challenge.Submit(challenge.Current.Id);
        challenge.Continue(challenge.Current.Id);
        source.SaveChallenge(challenge.Result(DateTimeOffset.Now));
        source.ExportBackup(backupPath);
        byte[] original = File.ReadAllBytes(backupPath);

        target.SetNote("ability", "目标数据库原始笔记");
        string previous = target.ImportBackup(backupPath);
        check(target.Load() == source.Load() && target.Setting("bounds") == source.Setting("bounds"), "backup restores study plan and widget bounds");
        check(target.GetEntry(word.Id) == source.GetEntry(word.Id) && target.GetWordHistory(word.Id).Attempts == 1, "backup restores notes, bookmarks and reviews");
        check(target.ChallengeHistory().Single().Answers.Single().Word == word && target.GetEntry("ability").Note == "", "restore replaces data and retains challenge results");
        check(File.ReadAllBytes(backupPath).SequenceEqual(original), "import leaves source backup byte-for-byte intact");
        target.ImportBackup(previous);
        check(target.GetEntry("ability").Note == "目标数据库原始笔记" && target.History().Count == 0, "automatic pre-import backup can be restored");

        string oldBackup = Directory.GetFiles(Path.Combine(scratch, "legacy"), "*.bak").Single();
        target.ImportBackup(oldBackup);
        check(target.Load().DailyGoal == 9 && target.GetWordHistory("abandon").Attempts == 1 && target.ChallengeHistory().Count == 0, "version 1 import upgrades into current tables");

        string invalid = Path.Combine(scratch, "broken.lexipane");
        File.Copy(backupPath, invalid);
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = invalid, Pooling = false }.ToString()))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "ALTER TABLE challenge_answers RENAME COLUMN payload TO bad";
            command.ExecuteNonQuery();
        }
        bool rejected = false;
        try { target.ImportBackup(invalid); } catch (SqliteException) { rejected = true; }
        check(rejected && target.Load().DailyGoal == 9 && target.GetWordHistory("abandon").Attempts == 1, "failed import rolls back progress, reviews and settings");
        target.ImportBackup(backupPath);
        check(target.Load().DailyGoal == 23, "import can be retried after a failed backup");
    }
}
