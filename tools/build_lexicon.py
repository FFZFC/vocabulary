"""Rebuild the shipped read-only database from the pinned ECDICT snapshot."""
import csv
import hashlib
import json
import pathlib
import sqlite3
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
REV = "39cfb9672c0906b63569e0453c2e6abfb9ab182a"
BASE = f"https://raw.githubusercontent.com/skywind3000/ECDICT/{REV}/"
cache = ROOT / ".cache"
assets = ROOT / "src" / "Vocabulary" / "Data"
cache.mkdir(exist_ok=True)
assets.mkdir(parents=True, exist_ok=True)
source = cache / f"ecdict-{REV}.csv"
if not source.exists():
    part = source.with_suffix(".part")
    urllib.request.urlretrieve(BASE + "ecdict.csv", part)
    part.replace(source)
if hashlib.sha256(source.read_bytes()).hexdigest() != "d1ecbdf9ee2b920afb6f430ca6b6bb9f559588cf0dff86e04bd255f9390843cc":
    raise ValueError("ECDICT source checksum mismatch")
urllib.request.urlretrieve(BASE + "LICENSE", assets / "ECDICT-LICENSE.txt")
staging = cache / "lexicon.new.db"
staging.unlink(missing_ok=True)
db = sqlite3.connect(staging)
db.execute("""CREATE TABLE words(id TEXT PRIMARY KEY, word TEXT NOT NULL, phonetic TEXT NOT NULL,
        pos TEXT NOT NULL, meaning TEXT NOT NULL, cet4 INTEGER NOT NULL, cet6 INTEGER NOT NULL, frequency INTEGER)""")
with source.open(encoding="utf-8-sig", newline="") as stream:
    for row in csv.DictReader(stream):
        tags = set((row["tag"] or "").lower().split())
        if tags.isdisjoint({"cet4", "cet6"}):
            continue
        word = row["word"].strip()
        meaning = row["translation"].replace(r"\n", "\n").strip()
        if not word or not meaning:
            raise ValueError(f"Missing word or translation: {word!r}")
        rank = next((int(v) for v in (row["frq"], row["bnc"]) if v and int(v) > 0), None)
        pos = " / ".join(dict.fromkeys(x.split(":")[0] + "." for x in row["pos"].split("/") if x))
        db.execute("""INSERT INTO words VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET
                    cet4=max(cet4,excluded.cet4), cet6=max(cet6,excluded.cet6)""",
                   (word.casefold(), word, row["phonetic"].strip(), pos, meaning,
                    int("cet4" in tags), int("cet6" in tags), rank))
counts = db.execute("SELECT sum(cet4),count(*) FROM words").fetchone()
assert counts[0] > 2000 and counts[1] > counts[0]
db.commit()
db.close()
staging.replace(assets / "lexicon.db")
info = dict(source="https://github.com/skywind3000/ECDICT", commit=REV,
            source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(), cet4=counts[0], cet6_including_cet4=counts[1])
(assets / "source.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
print(info)


