"""Populate only a fresh GUI-test profile with a deterministic v1 learning history."""
from datetime import date, timedelta, datetime, time
from pathlib import Path
import random
import sqlite3
import sys
import uuid

root = Path(__file__).resolve().parents[1]
profile = Path(sys.argv[1]).resolve()
assert profile.parent == root / ".cache" and profile.name.startswith("gui-")
profile.mkdir(exist_ok=False)
rng = random.Random(23)
with sqlite3.connect((root / "src/Vocabulary/Data/lexicon.db").as_uri()+"?mode=ro", uri=True) as dictionary:
    words = [row[0] for row in dictionary.execute("SELECT id FROM words WHERE cet4=1 ORDER BY id LIMIT 1800")]
rng.shuffle(words)
with sqlite3.connect(profile/"study.db") as db:
    db.executescript("""
        CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        CREATE TABLE progress(id TEXT PRIMARY KEY,successes INTEGER,ease REAL,days INTEGER,due INTEGER);
        CREATE TABLE reviews(token TEXT PRIMARY KEY,id TEXT NOT NULL,day TEXT NOT NULL,at INTEGER NOT NULL,rating INTEGER NOT NULL,is_new INTEGER NOT NULL);
        PRAGMA user_version=1;
    """)
    learned = []
    for offset in range(83, 0, -1):
        day = date.today() - timedelta(days=offset)
        if rng.random() < .12: continue
        fresh = [words.pop() for _ in range(rng.randrange(4, 16))]
        reviews = rng.sample(learned, min(len(learned),rng.randrange(5, 24)))
        stamp = int(datetime.combine(day, time(12)).timestamp())
        for word in fresh + reviews:
            score = rng.choices([0,3,4,5],weights=[.19,.13,.52,.16])[0]
            db.execute("INSERT INTO reviews VALUES(?,?,?,?,?,?)",
                       (str(uuid.uuid4()),word,day.isoformat(),stamp,score,int(word in fresh)))
            db.execute("INSERT OR REPLACE INTO progress VALUES(?,?,?,?,?)",
                       (word,int(score>0),2.5,1 if score else 0,stamp+(86400 if score else 600)))
            stamp += 30
        learned.extend(fresh)
