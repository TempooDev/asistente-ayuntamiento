"""Write the files the importer needs for a run that was not scored with RAGAS.

    python import_summary.py ../runs/<runId> --dataset ../dataset

The repeated runs are only compared with judge-free metrics, so score.py never
ran on them. To store them in the database next to the scored runs, this writes
a summary.csv with the retrieval metrics (the RAGAS columns empty) and a
scoring.json that says no judge was used. It refuses to overwrite a run that
already has a summary.csv.
"""
from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

import pandas as pd

import score
from common import load_run, read_jsonl


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--dataset", type=Path, required=True)
    args = ap.parse_args(argv)
    if (args.run_dir / "summary.csv").exists():
        print(f"ERROR {args.run_dir} ya tiene summary.csv", file=sys.stderr)
        return 1
    run, answers = load_run(args.run_dir)
    questions = {q["id"]: q for q in read_jsonl(args.dataset / "questions.jsonl")}
    # build_scores without a judge: every answer counts as already judged with empty scores
    records = []
    for a in answers:
        q = questions[a["question_id"]]
        failed = a.get("error_code") is not None
        contexts = a.get("contexts") or []
        records.append({
            "question_id": a["question_id"], "pipeline": a["pipeline"], "category": q["category"],
            "failed": failed, **{c: score.NAN for c in score.RAGAS_COLUMNS},
            **(score.retrieval_metrics([c["document_id"] for c in contexts], q["reference_document_ids"])
               if not failed else {c: score.NAN for c in score.RETRIEVAL_COLUMNS}),
            "not_covered": (score.says_not_covered(a.get("answer", ""))
                            if q["category"] == score.NO_ANSWER_CATEGORY and not failed else None),
        })
    summary = score.summarize(pd.DataFrame.from_records(records), threshold=0.8)
    summary.to_csv(args.run_dir / "summary.csv", index=False)
    (args.run_dir / "scoring.json").write_text(json.dumps({
        "run_id": run.get("run_id"), "judge_model": "ninguno (solo métricas sin juez)", "ragas_version": None,
        "judge_provider": None, "scored_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(summary[["pipeline", "hit@1_mean", "hit@5_mean", "reciprocal_rank_mean"]].to_string(index=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
