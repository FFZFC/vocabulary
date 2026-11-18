"""Compare all packaged word memberships against the verified input CSV."""
import csv
import hashlib
import pathlib
import sqlite3
import json

root = pathlib.Path(__file__).resolve().parents[1]
assets = root / "src/Vocabulary/Data"
info = json.loads((assets/"source.json").read_text())
source = root/".cache"/("ecdict-"+info["commit"]+".csv")
assert hashlib.sha256(source.read_bytes()).hexdigest() == info["source_sha256"]
expected = {}
with source.open(encoding="utf-8-sig", newline="") as stream:
    for row in csv.DictReader(stream):
        labels=set(row["tag"].split())
        if labels.intersection({"cet4","cet6"}):
            expected.setdefault(row["word"].strip().casefold(),set()).update(labels & {"cet4","cet6"})
with sqlite3.connect((assets/"lexicon.db").as_uri()+"?mode=ro",uri=True) as db:
    actual={word: {tag for tag,present in (("cet4",four),("cet6",six)) if present}
            for word,four,six in db.execute("select id,cet4,cet6 from words")}
    assert len(actual)==db.execute("select count(*) from words").fetchone()[0]
    assert db.execute("pragma integrity_check").fetchone()[0]=="ok"
assert expected==actual
print(f"PASS {len(actual)} normalized headwords and all original CET memberships")

