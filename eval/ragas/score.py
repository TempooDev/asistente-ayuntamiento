"""Score a run with RAGAS (Ollama judge) plus deterministic hit@k / MRR.

    python score.py ../runs/<runId> [--dataset ../dataset] [--judge-model M] [--ollama-url URL]

Writes scores.csv, summary.csv and scoring.json into the run directory, only
after every score has been computed (nothing is written if Ollama or a model
is missing).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import unicodedata
from datetime import datetime, timezone
from pathlib import Path

import pandas as pd
import requests

from common import NO_ANSWER_CATEGORY, load_run, read_jsonl

os.environ.setdefault("RAGAS_DO_NOT_TRACK", "true")

NAN = float("nan")
RAGAS_COLUMNS = ["context_precision", "context_recall", "faithfulness", "answer_relevancy"]
RETRIEVAL_COLUMNS = ["hit@1", "hit@3", "hit@5", "reciprocal_rank"]
METRICS = RAGAS_COLUMNS + RETRIEVAL_COLUMNS

# Heuristic for `sin-respuesta`: the answer admits the bulletins do not cover the
# question. Matched on the lower-cased answer without accents.
NOT_COVERED_PHRASES = [
    "no he encontrado", "no encuentro", "no dispongo", "no hay informacion",
    "no aparece", "no tengo informacion", "no se menciona", "no consta",
    "no se recoge", "no recogen", "no contienen informacion", "no se ha encontrado",
]


class ConfigError(Exception):
    pass


# ---------------------------------------------------------------- deterministic

def retrieval_metrics(retrieved_ids: list[str], reference_ids: list[str]) -> dict[str, float]:
    """hit@k and reciprocal rank over the context ranks (1-based). NaN without references."""
    if not reference_ids:
        return {c: NAN for c in RETRIEVAL_COLUMNS}
    refs = set(reference_ids)
    first = next((i for i, d in enumerate(retrieved_ids, 1) if d in refs), None)
    out = {f"hit@{k}": float(first is not None and first <= k) for k in (1, 3, 5)}
    out["reciprocal_rank"] = 1.0 / first if first else 0.0
    return out


def _fold(text: str) -> str:
    nfkd = unicodedata.normalize("NFKD", text.lower())
    return "".join(ch for ch in nfkd if not unicodedata.combining(ch))


def says_not_covered(answer: str) -> bool:
    folded = _fold(answer or "")
    return any(p in folded for p in NOT_COVERED_PHRASES)


# ---------------------------------------------------------------- ollama / ragas

def check_ollama(url: str, models: list[str]) -> None:
    try:
        resp = requests.get(f"{url.rstrip('/')}/api/tags", timeout=10)
        resp.raise_for_status()
        available = {m["name"] for m in resp.json().get("models", [])}
    except (requests.RequestException, ValueError) as ex:
        raise ConfigError(f"Ollama no responde en {url} (modelo juez: {models[0]}): {ex}") from ex
    available |= {n.removesuffix(":latest") for n in available}
    missing = [m for m in models if m not in available]
    if missing:
        raise ConfigError(f"Faltan modelos en Ollama {url}: {', '.join(missing)} (ollama pull <modelo>)")


def run_ragas(rows: list[dict], cfg: dict) -> list[dict]:
    """Return one dict of RAGAS_COLUMNS per row (NaN on judge errors).

    Retrieval metrics and relevancy use the retrieved contexts; faithfulness uses
    the generation context as a single-element list. Tests monkeypatch this."""
    from langchain_ollama import ChatOllama, OllamaEmbeddings
    from ragas import EvaluationDataset, RunConfig, evaluate
    from ragas.embeddings import LangchainEmbeddingsWrapper
    from ragas.llms import LangchainLLMWrapper
    from ragas.metrics import Faithfulness, LLMContextPrecisionWithReference, LLMContextRecall, ResponseRelevancy

    llm = LangchainLLMWrapper(
        ChatOllama(model=cfg["judge_model"], base_url=cfg["ollama_url"], temperature=0, num_ctx=cfg["num_ctx"]),
        bypass_temperature=True,  # keep temperature 0 (ragas would otherwise force 0.01/0.3)
    )
    emb = LangchainEmbeddingsWrapper(OllamaEmbeddings(model=cfg["embedding_model"], base_url=cfg["ollama_url"]))
    run_config = RunConfig(max_workers=cfg["max_workers"], timeout=cfg["timeout"], max_retries=3)
    # strictness=1: at temperature 0 extra generated questions would be identical.
    named = {
        "context_precision": LLMContextPrecisionWithReference(),
        "context_recall": LLMContextRecall(),
        "answer_relevancy": ResponseRelevancy(strictness=1),
        "faithfulness": Faithfulness(),
    }

    def _eval(samples: list[dict], keys: list[str]) -> pd.DataFrame:
        metrics = [named[k] for k in keys]
        result = evaluate(EvaluationDataset.from_list(samples), metrics=metrics, llm=llm, embeddings=emb,
                          run_config=run_config, raise_exceptions=False, show_progress=True)
        df = result.to_pandas()
        return df.rename(columns={m.name: k for k, m in zip(keys, metrics)})[keys]

    base = [{"user_input": r["question"], "response": r["answer"], "reference": r["reference"]} for r in rows]
    retrieval = _eval([{**b, "retrieved_contexts": r["contexts"] or [""]} for b, r in zip(base, rows)],
                      ["context_precision", "context_recall", "answer_relevancy"])
    faith = _eval([{**b, "retrieved_contexts": [r["generation_context"] or ""]} for b, r in zip(base, rows)],
                  ["faithfulness"])
    return pd.concat([retrieval, faith], axis=1)[RAGAS_COLUMNS].to_dict("records")


# ---------------------------------------------------------------- assembly

def build_scores(answers: list[dict], questions: dict[str, dict], cfg: dict) -> pd.DataFrame:
    unknown = sorted({a["question_id"] for a in answers} - questions.keys())
    if unknown:
        raise ConfigError(f"Preguntas de answers.jsonl que no están en questions.jsonl: {', '.join(unknown)}")

    records, to_judge = [], []
    for a in answers:
        q = questions[a["question_id"]]
        failed = a.get("error_code") is not None
        contexts = a.get("contexts") or []
        rec = {
            "question_id": a["question_id"], "pipeline": a["pipeline"], "category": q["category"],
            "position": a.get("position"), "warmup": bool(a.get("warmup")), "error_code": a.get("error_code"),
            "failed": failed, **{c: NAN for c in RAGAS_COLUMNS},
            **(retrieval_metrics([c["document_id"] for c in contexts], q["reference_document_ids"])
               if not failed else {c: NAN for c in RETRIEVAL_COLUMNS}),
            "not_covered": (says_not_covered(a.get("answer", ""))
                            if q["category"] == NO_ANSWER_CATEGORY and not failed else None),
            "input_tokens": a.get("input_tokens"), "output_tokens": a.get("output_tokens"),
        }
        records.append(rec)
        if not failed:
            to_judge.append((rec, {"question": q["question"], "answer": a.get("answer") or "",
                                   "reference": q["reference"], "contexts": [c["text"] for c in contexts],
                                   "generation_context": a.get("generation_context") or ""}))

    if to_judge:
        judged = run_ragas([row for _, row in to_judge], cfg)
        for (rec, _), scores in zip(to_judge, judged):
            for c in RAGAS_COLUMNS:
                v = scores.get(c)
                rec[c] = NAN if v is None or pd.isna(v) else float(v)
    return pd.DataFrame.from_records(records)


def summarize(scores: pd.DataFrame, threshold: float) -> pd.DataFrame:
    rows = []
    for pipeline, g in scores.groupby("pipeline", sort=True):
        ok = g[~g["failed"]]
        row = {"pipeline": pipeline, "n": len(g), "failed": int(g["failed"].sum()),
               "missing": int(ok[RAGAS_COLUMNS].isna().any(axis=1).sum())}
        for m in METRICS:
            row[f"{m}_mean"], row[f"{m}_std"], row[f"{m}_n"] = g[m].mean(), g[m].std(), int(g[m].notna().sum())
        faith = g["faithfulness"].dropna()
        row["hallucination_rate"] = float((faith < threshold).mean()) if len(faith) else NAN
        nc = g["not_covered"].dropna().astype(bool)
        row["no_answer_n"], row["no_answer_detected_rate"] = len(nc), float(nc.mean()) if len(nc) else NAN
        rows.append(row)
    return pd.DataFrame(rows)


def ragas_version() -> str:
    try:
        from importlib.metadata import version
        return version("ragas")
    except Exception:  # pragma: no cover
        return "unknown"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--dataset", type=Path, default=Path(__file__).parent.parent / "dataset")
    ap.add_argument("--judge-model", default=os.environ.get("EVAL_JUDGE_MODEL"))
    ap.add_argument("--embedding-model", default=os.environ.get("EVAL_EMBEDDING_MODEL"))
    ap.add_argument("--ollama-url", default=os.environ.get("OLLAMA_URL", "http://localhost:11434"))
    ap.add_argument("--threshold", type=float, default=float(os.environ.get("HALLUCINATION_THRESHOLD", "0.8")))
    ap.add_argument("--max-workers", type=int, default=int(os.environ.get("EVAL_MAX_WORKERS", "2")))
    ap.add_argument("--timeout", type=int, default=int(os.environ.get("EVAL_TIMEOUT", "300")))
    ap.add_argument("--num-ctx", type=int, default=int(os.environ.get("EVAL_JUDGE_NUM_CTX", "8192")))
    args = ap.parse_args(argv)

    try:
        run, answers = load_run(args.run_dir)
        questions = {q["id"]: q for q in read_jsonl(args.dataset / "questions.jsonl")}
        cfg = {"judge_model": args.judge_model or run["chat_model"],
               "embedding_model": args.embedding_model or run["embedding_model"],
               "ollama_url": args.ollama_url, "max_workers": args.max_workers,
               "timeout": args.timeout, "num_ctx": args.num_ctx}
        check_ollama(cfg["ollama_url"], [cfg["judge_model"], cfg["embedding_model"]])
        scores = build_scores(answers, questions, cfg)
    except (ConfigError, FileNotFoundError, KeyError) as ex:
        print(f"ERROR {ex}", file=sys.stderr)
        return 1

    summary = summarize(scores, args.threshold)
    scores.to_csv(args.run_dir / "scores.csv", index=False)
    summary.to_csv(args.run_dir / "summary.csv", index=False)
    (args.run_dir / "scoring.json").write_text(json.dumps({
        "run_id": run.get("run_id"), "judge_model": cfg["judge_model"], "embedding_model": cfg["embedding_model"],
        "ollama_url": cfg["ollama_url"], "ragas_version": ragas_version(), "judge_temperature": 0,
        "hallucination_threshold": args.threshold, "max_workers": args.max_workers, "timeout_s": args.timeout,
        "num_ctx": args.num_ctx, "not_covered_phrases": NOT_COVERED_PHRASES,
        "scored_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(summary.to_string(index=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
