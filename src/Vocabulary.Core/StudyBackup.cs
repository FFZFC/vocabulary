using Microsoft.Data.Sqlite;

namespace Vocabulary.Core;

public sealed partial class StudyStore
{
    private static readonly (string Name, string Columns, int Since)[] BackupTables =
    [
        ("settings", "key,value", 1),
        ("progress", "id,successes,ease,days,due", 1),
        ("reviews", "token,id,day,at,rating,is_new", 1),
        ("bookmarks", "id", 2),
        ("notes", "id,text", 2),
        ("challenge_rounds", "id,day,at,kind,book,options", 3),
        ("challenge_answers", "round_id,position,payload", 3)
    ];

    public void ExportBackup(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (string.Equals(destination, databasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择与当前学习数据库不同的备份文件。");

        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = temporary, Pooling = false }.ToString()))
            {
                backup.Open();
                db.BackupDatabase(backup);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public string ImportBackup(string source)
    {
        source = Path.GetFullPath(source);
        if (string.Equals(source, databasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择旧版存档或备份文件。");

        using var attach = Cmd("ATTACH DATABASE $uri AS incoming", ("$uri", new Uri(source).AbsoluteUri + "?mode=ro"));
        attach.ExecuteNonQuery();
        try
        {
            long version = Number("PRAGMA incoming.user_version");
            if (version is < 1 or > 3) throw new InvalidDataException("此文件不是受支持的 LexiPane 学习存档。");
            var tables = BackupTables.Where(t => t.Since <= version).ToArray();
            string saved = databasePath + ".before-import-" + Guid.NewGuid().ToString("N") + ".bak";
            ExportBackup(saved);
            using var tx = db.BeginTransaction(deferred: true);
            using var integrity = Cmd("PRAGMA incoming.quick_check(1)");
            integrity.Transaction = tx;
            if (!Equals(integrity.ExecuteScalar(), "ok")) throw new InvalidDataException("这个存档未通过 SQLite 完整性检查。");
            foreach (var table in tables)
            {
                using var validate = Cmd("""
                    SELECT 1 FROM incoming.sqlite_master
                    WHERE name=$name AND type='table' AND sql LIKE 'CREATE TABLE %'
                    """, ("$name", table.Name));
                validate.Transaction = tx;
                if (validate.ExecuteScalar() is null) throw new InvalidDataException($"存档缺少学习数据表 {table.Name}。");
            }
            foreach (var table in BackupTables.Reverse())
            {
                using var delete = Cmd($"DELETE FROM main.{table.Name}");
                delete.Transaction = tx;
                delete.ExecuteNonQuery();
            }
            foreach (var table in tables)
            {
                // Preserve original row order for bookmarks and reviews with equal timestamps.
                using var copy = Cmd($"INSERT INTO main.{table.Name}(rowid,{table.Columns}) SELECT rowid,{table.Columns} FROM incoming.{table.Name}");
                copy.Transaction = tx;
                copy.ExecuteNonQuery();
            }
            tx.Commit();
            return saved;
        }
        finally
        {
            using var detach = Cmd("DETACH DATABASE incoming");
            detach.ExecuteNonQuery();
        }
    }
}
