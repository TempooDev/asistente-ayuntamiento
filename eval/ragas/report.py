"""Latency and indexing cost report: `python report.py ../runs/<runId>`.

latency.csv: per pipeline and stage, n / mean / p50 / p95 in ms, excluding
warm-up rows (`warmup: true`) and failed samples.
indexing.csv: per pipeline, from indexing.json (latest IngestionMetrics row per
corpus document) plus the points in its Qdrant collection.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import pandas as pd

from common import load_run, read_json

STAGES = ["expansion", "retrieval", "ttft", "generation", "total"]


def latency_table(answers: list[dict]) -> pd.DataFrame:
    rows = [{"pipeline": a["pipeline"], **{s: (a.get("timings") or {}).get(f"{s}_ms") for s in STAGES}}
            for a in answers if not a.get("warmup") and a.get("error_code") is None]
    df = pd.DataFrame(rows, columns=["pipeline", *STAGES])
    out = []
    for pipeline, g in df.groupby("pipeline", sort=True):
        for stage in STAGES:
            v = pd.to_numeric(g[stage], errors="coerce").dropna()
            out.append({"pipeline": pipeline, "stage": stage, "n": len(v), "mean_ms": v.mean(),
                        "p50_ms": v.quantile(0.5), "p95_ms": v.quantile(0.95)})
    return pd.DataFrame(out, columns=["pipeline", "stage", "n", "mean_ms", "p50_ms", "p95_ms"])


def indexing_table(indexing: dict) -> pd.DataFrame:
    out = []
    for p in indexing.get("pipelines", []):
        docs = pd.DataFrame(p.get("documents", []),
                            columns=["document_id", "processing_ms", "chunks", "embedded_tokens", "llm_calls", "llm_tokens"])
        out.append({
            "pipeline": p["pipeline"], "collection": p.get("collection"), "documents": len(docs),
            "mean_ms_per_document": docs["processing_ms"].mean(), "p50_ms_per_document": docs["processing_ms"].quantile(0.5),
            "p95_ms_per_document": docs["processing_ms"].quantile(0.95),
            "mean_chunks_per_document": docs["chunks"].mean(), "embedded_tokens": int(docs["embedded_tokens"].sum()),
            "llm_calls": int(docs["llm_calls"].sum()), "llm_tokens": int(docs["llm_tokens"].sum()),
            "points": p.get("points"),
        })
    return pd.DataFrame(out).sort_values("pipeline").reset_index(drop=True) if out else pd.DataFrame(out)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run_dir", type=Path)
    args = ap.parse_args(argv)
    try:
        _, answers = load_run(args.run_dir)
    except FileNotFoundError as ex:
        print(f"ERROR {ex}", file=sys.stderr)
        return 1
    latency = latency_table(answers)
    latency.to_csv(args.run_dir / "latency.csv", index=False)
    print(latency.to_string(index=False))
    indexing_path = args.run_dir / "indexing.json"
    if indexing_path.exists():
        indexing = indexing_table(read_json(indexing_path))
        indexing.to_csv(args.run_dir / "indexing.csv", index=False)
        print(indexing.to_string(index=False))
    else:
        print(f"AVISO no existe {indexing_path}; no se escribe indexing.csv", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
