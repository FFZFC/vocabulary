"""Generate the offline Wordle guess list from the already pinned ECDICT CSV."""
import csv
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root / "src/Vocabulary/Data"
source = json.loads((assets / "source.json").read_text(encoding="utf-8"))
csv_path = root / ".cache" / f"ecdict-{source['commit']}.csv"
if hashlib.sha256(csv_path.read_bytes()).hexdigest() != source["source_sha256"]:
    raise ValueError("ECDICT source checksum mismatch")
tags = {"zk", "gk", "cet4", "cet6", "ky", "ielts", "toefl", "gre"}
words = set()
with csv_path.open(encoding="utf-8-sig", newline="") as stream:
    for row in csv.DictReader(stream):
        word = row["word"].strip()
        if not re.fullmatch("[a-z]{4,7}", word):
            continue
        frequent = any(value.isdigit() and int(value) > 0 for value in (row["frq"], row["bnc"], row["collins"]))
        if frequent or set(row["tag"].split()) & tags:
            words.add(word)
# Every eligible answer is accepted even if its source tags use an unusual format.
import sqlite3
with sqlite3.connect((assets / "lexicon.db").as_uri() + "?mode=ro", uri=True) as db:
    words.update(word for (word,) in db.execute("SELECT word FROM words") if re.fullmatch("[a-z]{4,7}", word))
destination = assets / "wordle-words.txt"
destination.write_text("\n".join(sorted(words)) + "\n", encoding="utf-8", newline="\n")
metadata = dict(source, counts=dict(sorted(Counter(map(len, words)).items())), total=len(words),
                rule="lowercase ASCII 4-7 letters; exam tag (zk,gk,cet4,cet6,ky,ielts,toefl,gre) or positive frq/bnc/collins; union all eligible CET answers",
                sha256=hashlib.sha256(destination.read_bytes()).hexdigest())
(assets / "wordle-source.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(metadata, ensure_ascii=False))
