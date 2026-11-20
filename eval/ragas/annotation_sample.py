"""Draw a sample of answers for human annotation and compare annotations with the judge.

    python annotation_sample.py sample --out ../annotation [--n 50] [--seed 7] RUN:DATASET ...
    python annotation_sample.py agree ../annotation/anotacion-A.csv ../annotation/anotacion-B.csv [--judge RUN_DIR ...]

`sample` takes answerable, non-warm-up answers from the given runs (each given
as run_dir:dataset_dir), stratified by run and pipeline, and writes one CSV per
annotator with the question, the reference answer, the answer and the text the
model received, plus empty label columns:

- correcta: si / parcial / no   (does the answer give the reference fact?)
- fiel: si / no                 (is everything it states supported by the text it received?)
- se_abstiene: si / no          (does it say the information is not available?)

The order of the rows is shuffled and the pipeline is hidden, so the
annotator does not know which variant wrote each answer (a key file keeps it).

`agree` reports Cohen's kappa between two annotators for each label and, with
--judge, the agreement between the annotators' consensus and the judge's
faithfulness (>= 0.8 counts as "fiel").
"""
from __future__ import annotations

import argparse
import csv
import json
import random
import sys
from collections import Counter
from pathlib import Path

from common import NO_ANSWER_CATEGORY, load_run, read_jsonl

LABELS = ["correcta", "fiel", "se_abstiene"]
MAX_CONTEXT = 6000


def sample(specs: list[str], out: Path, n: int, seed: int) -> None:
    rng = random.Random(seed)
    strata: dict[tuple[str, str], list[dict]] = {}
    for spec in specs:
        run_part, dataset_part = spec.rsplit(":", 1)  # relative paths, e.g. ../runs/X:../dataset
        run_dir, dataset = Path(run_part), Path(dataset_part)
        run, answers = load_run(run_dir)
        questions = {q["id"]: q for q in read_jsonl(dataset / "questions.jsonl")}
        for a in answers:
            q = questions[a["question_id"]]
            if a.get("warmup") or a.get("error_code") or q["category"] == NO_ANSWER_CATEGORY:
                continue
            strata.setdefault((run_dir.name, a["pipeline"]), []).append({
                "run": run_dir.name, "question_id": a["question_id"], "pipeline": a["pipeline"],
                "question": q["question"], "reference": q["reference"], "answer": a.get("answer") or "",
                "context": (a.get("generation_context") or "")[:MAX_CONTEXT]})
    keys = sorted(strata)
    for k in keys:
        rng.shuffle(strata[k])
    picked, i = [], 0
    while len(picked) < n and any(strata.values()):
        k = keys[i % len(keys)]
        if strata[k]:
            picked.append(strata[k].pop())
        i += 1
    rng.shuffle(picked)
    out.mkdir(parents=True, exist_ok=True)
    for idx, row in enumerate(picked, 1):
        row["item"] = f"a{idx:03d}"
    with (out / "clave.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["item", "run", "question_id", "pipeline"])
        w.writeheader()
        w.writerows({k: r[k] for k in ("item", "run", "question_id", "pipeline")} for r in picked)
    for annotator in ("A", "B"):
        with (out / f"anotacion-{annotator}.csv").open("w", encoding="utf-8-sig", newline="") as f:
            w = csv.DictWriter(f, fieldnames=["item", "question", "reference", "answer", "context", *LABELS], delimiter=";")
            w.writeheader()
            w.writerows({**{k: r[k] for k in ("item", "question", "reference", "answer", "context")},
                         **{l: "" for l in LABELS}} for r in picked)
    counts = Counter((r["run"], r["pipeline"]) for r in picked)
    print(f"{len(picked)} respuestas -> {out}")
    for k, c in sorted(counts.items()):
        print(f"  {k[0]} {k[1]}: {c}")


def read_labels(path: Path) -> dict[str, dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as f:
        return {r["item"]: {l: (r.get(l) or "").strip().lower() for l in LABELS} for r in csv.DictReader(f, delimiter=";")}


def kappa(a: list[str], b: list[str]) -> float:
    n = len(a)
    if n == 0:
        return float("nan")
    po = sum(x == y for x, y in zip(a, b)) / n
    ca, cb = Counter(a), Counter(b)
    pe = sum(ca[k] * cb[k] for k in set(ca) | set(cb)) / n ** 2
    return 1.0 if pe == 1 else (po - pe) / (1 - pe)


def agree(file_a: Path, file_b: Path, judge_runs: list[Path]) -> None:
    la, lb = read_labels(file_a), read_labels(file_b)
    items = sorted(set(la) & set(lb))
    for label in LABELS:
        pairs = [(la[i][label], lb[i][label]) for i in items if la[i][label] and lb[i][label]]
        k = kappa([p[0] for p in pairs], [p[1] for p in pairs])
        print(f"{label}: {len(pairs)} pares, acuerdo {sum(x == y for x, y in pairs) / max(1, len(pairs)):.2f}, kappa {k:.2f}")
    if judge_runs:
        key = {r["item"]: r for r in csv.DictReader((file_a.parent / "clave.csv").open(encoding="utf-8-sig"))}
        judge = {}
        for run in judge_runs:
            with (run / "scores.csv").open(encoding="utf-8") as f:
                for r in csv.DictReader(f):
                    if r["faithfulness"]:
                        judge[(run.name.split("-")[0] + "-" + run.name.split("-")[1], r["question_id"], r["pipeline"])] = float(r["faithfulness"])
        human, model = [], []
        for i in items:
            if la[i]["fiel"] and la[i]["fiel"] == lb[i]["fiel"]:
                k = (key[i]["run"], key[i]["question_id"], key[i]["pipeline"])
                if k in judge:
                    human.append(la[i]["fiel"])
                    model.append("si" if judge[k] >= 0.8 else "no")
        print(f"fiel (consenso humano) frente al juez: {len(human)} pares, kappa {kappa(human, model):.2f}")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("sample")
    s.add_argument("runs", nargs="+", help="run_dir:dataset_dir")
    s.add_argument("--out", type=Path, required=True)
    s.add_argument("--n", type=int, default=50)
    s.add_argument("--seed", type=int, default=7)
    g = sub.add_parser("agree")
    g.add_argument("file_a", type=Path)
    g.add_argument("file_b", type=Path)
    g.add_argument("--judge", type=Path, nargs="*", default=[])
    args = ap.parse_args(argv)
    if args.cmd == "sample":
        sample(args.runs, args.out, args.n, args.seed)
    else:
        agree(args.file_a, args.file_b, args.judge)
    return 0


if __name__ == "__main__":
    sys.exit(main())
