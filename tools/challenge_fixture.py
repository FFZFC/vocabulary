"""Create a version-2 QA profile to exercise migration and game UI deterministically."""
import json
import re
import sqlite3
import sys
from pathlib import Path

folder = Path(sys.argv[1]).resolve()
workspace = Path(__file__).resolve().parents[1]
if not folder.is_relative_to(workspace / ".cache"):
    raise ValueError("Expected an isolated QA directory")
folder.mkdir(parents=True)
with sqlite3.connect(folder / "study.db") as db:
    db.executescript("""
        CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        CREATE TABLE progress(id TEXT PRIMARY KEY,successes INTEGER,ease REAL,days INTEGER,due INTEGER);
        CREATE TABLE reviews(token TEXT PRIMARY KEY,id TEXT NOT NULL,day TEXT NOT NULL,at INTEGER NOT NULL,rating INTEGER NOT NULL,is_new INTEGER NOT NULL);
        CREATE TABLE bookmarks(id TEXT PRIMARY KEY);
        CREATE TABLE notes(id TEXT PRIMARY KEY,text TEXT NOT NULL);
        INSERT INTO settings VALUES('limit','20'),('book','4');
        INSERT INTO bookmarks VALUES('part'),('state'),('system'),('against');
        INSERT INTO notes VALUES('state','This is a QA memory note.');
        PRAGMA user_version=2;
    """)
with sqlite3.connect((workspace / "src/Vocabulary/Data/lexicon.db").as_uri() + "?mode=ro", uri=True) as db:
    words = [{"word": word, "prompt": re.sub(re.escape(word), "____", meaning, flags=re.I)}
             for word, meaning in db.execute("SELECT word,meaning FROM words WHERE id IN ('part','state','system','against')")]
assert len(words) == 4
(folder / "catalog.json").write_text(json.dumps(words, ensure_ascii=False), encoding="utf-8")
