"""Variation between repeated runs of the same question set and configuration.

    python repeats.py [--out ../runs/repeats.csv]

For each question set, the runs listed in REPEATS were made with the same code,
prompts, models and collection; only Ollama's own nondeterminism differs. The
script recomputes the judge-free metrics of every run (judge_free.py) and
reports, per set, variant and metric, the mean, standard deviation, minimum and
maximum across runs, and how many questions changed hit@1 between runs.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import pandas as pd

import judge_free as jf
from common import load_run, read_jsonl

RUNS = Path(__file__).parent.parent / "runs"
DATASETS = Path(__file__).parent.parent
REPEATS = {
    "E3": ("dataset", ["20261119-185051", "20261120-210557", "20261120-211105"]),
    "E4": ("dataset-sizes", ["20261119-185754", "20261120-211623", "20261120-212318"]),
    "E5": ("dataset-e5", ["20261120-202058", "20261120-213029", "20261120-214340"]),
}
METRICS = ["doc_hit@1", "doc_hit@5", "false_abstention", "facts_in_reference_units", "passage_in_generation"]


def questions_for(dataset: Path) -> dict[str, dict]:
    qs = {q["id"]: q for q in read_jsonl(dataset / "questions.jsonl")}
    detail = dataset / "questions-detail.jsonl"
    if detail.exists():
        for d in read_jsonl(detail):
            if d["id"] in qs:
                qs[d["id"]]["supporting_quote"] = d.get("supporting_quote", "")
    return qs


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=RUNS / "repeats.csv")
    args = ap.parse_args(argv)
    rows, flips = [], []
    for name, (dataset, runs) in REPEATS.items():
        questions = questions_for(DATASETS / dataset)
        per_run = {}
        for run_id in runs:
            _, answers = load_run(RUNS / run_id)
            scores = jf.score_answers(answers, questions)
            s = scores[scores["answerable"] & ~scores["failed"]].copy()
            s["false_abstention"] = s["abstained"].astype(float)
            per_run[run_id] = s
            for pipeline, g in s.groupby("pipeline"):
                rows.append({"set": name, "run": run_id, "pipeline": pipeline,
                             **{m: g[m].mean() for m in METRICS if m in g}})
        # questions whose hit@1 differs between at least two runs, per variant
        allruns = pd.concat([df.assign(run=r) for r, df in per_run.items()])
        piv = allruns.pivot_table(index=["pipeline", "question_id"], columns="run", values="doc_hit@1")
        changed = (piv.nunique(axis=1) > 1).groupby(level=0).sum()
        for pipeline, c in changed.items():
            flips.append({"set": name, "pipeline": pipeline, "questions_hit1_changed": int(c),
                          "questions": int(piv.loc[pipeline].shape[0])})
    df = pd.DataFrame(rows)
    df.to_csv(args.out, index=False)
    agg = df.groupby(["set", "pipeline"])[[m for m in METRICS if m in df]].agg(["mean", "std", "min", "max"])
    with pd.option_context("display.width", 250, "display.max_columns", 40, "display.float_format", "{:.3f}".format):
        print(agg)
        print()
        print(pd.DataFrame(flips).to_string(index=False))
    pd.DataFrame(flips).to_csv(args.out.with_name("repeats-hit1-changes.csv"), index=False)
    return 0


if __name__ == "__main__":
    sys.exit(main())
