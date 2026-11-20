"""Judge-free metrics for a run: document-level retrieval, abstention and reference-fact coverage.

    python judge_free.py ../runs/<runId> [--dataset ../dataset] [--seed 42]

Nothing here calls a model, so the result is deterministic and can be recomputed
for any existing run. Writes into the run directory:

- judge_free.csv          one row per answer
- judge_free_summary.csv  one row per pipeline (means over answerable questions)
- judge_free_tests.csv    paired comparisons of Hierarchical against each flat variant

Metrics
- doc_hit@1, doc_hit@5, doc_mrr: like hit@k/MRR but over the retrieved units
  collapsed to distinct documents in order of first appearance, so two chunks of
  the same document no longer take two ranks.
- abstained: the first 400 characters of the answer contain a negation followed,
  in the same sentence, by "información", "dato(s)" or "referencia(s)". On
  answerable questions this is a false abstention; on `sin-respuesta` questions
  it is the expected behaviour.
- facts_in_retrieved / facts_in_generation / facts_in_answer: share of the
  reference "facts" (day-month dates, percentages, decimals and integers of
  three or more digits taken from the reference answer) that appear in the
  retrieved units, in the text given to the model, and in the answer. NaN when
  the reference has no such fact.
- facts_in_reference_units: the same, but only over the retrieved units that
  belong to a reference document, so a similar figure from another document
  does not count. For the hierarchical variant these are the child fragments,
  not the parent text the model receives.
"""
from __future__ import annotations

import argparse
import math
import random
import re
import sys
import unicodedata
from pathlib import Path

import pandas as pd

from common import NO_ANSWER_CATEGORY, load_run, read_jsonl

NAN = float("nan")
HIERARCHICAL = "Hierarchical"

MONTHS = ("enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|"
          "octubre|noviembre|diciembre")
DATE_RE = re.compile(rf"\b(\d{{1,2}}) de ({MONTHS})\b")
NUMBER_RE = re.compile(r"(?<![\w-])(\d{1,3}(?:\.\d{3})+|\d+)(?:,(\d+))?(\s*%)?(?![\w-])")
NEGATION_RE = re.compile(r"\b(no|ningun|ninguna|ni)\b[^.!?\n]*?\b(informacion|datos?|referencias?)\b")
ABSTENTION_PREFIX = 400


def fold(text: str) -> str:
    """Lower case, no accents, single spaces."""
    nfkd = unicodedata.normalize("NFKD", (text or "").lower())
    plain = "".join(ch for ch in nfkd if not unicodedata.combining(ch))
    return re.sub(r"\s+", " ", plain)


def _canonical_number(integer: str, decimals: str | None, percent: str | None) -> str:
    value = integer.replace(".", "")
    if decimals:
        value += "," + decimals
    return value + ("%" if percent else "")


def extract_facts(reference: str) -> set[str]:
    """Checkable facts of a reference answer, in canonical form.

    Keeps day-month dates and numbers that are not too generic: two or more
    digits, a decimal part or a percent sign. Years on their own are skipped,
    because almost every bulletin text mentions the current year."""
    text = fold(reference)
    facts = {f"{int(d)} de {m}" for d, m in DATE_RE.findall(text)}
    without_dates = DATE_RE.sub(" ", text)
    for m in NUMBER_RE.finditer(without_dates):
        integer, decimals, percent = m.group(1), m.group(2), m.group(3)
        digits = integer.replace(".", "")
        if not decimals and not percent and (len(digits) < 3 or digits.startswith("0")):
            continue  # small integers ("10 días", "08:00") match unrelated text too easily
        if len(digits) == 4 and 1990 <= int(digits) <= 2030 and not decimals and not percent:
            continue
        facts.add(_canonical_number(integer, decimals, percent))
    return facts


def facts_in_text(text: str) -> set[str]:
    """All facts present in a text, in the same canonical form as `extract_facts`."""
    folded = fold(text)
    found = {f"{int(d)} de {m}" for d, m in DATE_RE.findall(folded)}
    for m in NUMBER_RE.finditer(folded):
        found.add(_canonical_number(m.group(1), m.group(2), m.group(3)))
        if m.group(3):  # "15 %" also counts as the bare number
            found.add(_canonical_number(m.group(1), m.group(2), None))
    return found


def coverage(facts: set[str], text: str) -> float:
    if not facts:
        return NAN
    present = facts_in_text(text)
    return sum(f in present for f in facts) / len(facts)


def abstained(answer: str) -> bool:
    head = fold(answer)[:ABSTENTION_PREFIX]
    return NEGATION_RE.search(head) is not None


def document_ranking(contexts: list[dict]) -> list[str]:
    seen, out = set(), []
    for c in contexts:
        d = c["document_id"]
        if d not in seen:
            seen.add(d)
            out.append(d)
    return out


def doc_retrieval(contexts: list[dict], reference_ids: list[str]) -> dict[str, float]:
    if not reference_ids:
        return {"doc_hit@1": NAN, "doc_hit@5": NAN, "doc_mrr": NAN}
    refs = set(reference_ids)
    ranking = document_ranking(contexts)
    first = next((i for i, d in enumerate(ranking, 1) if d in refs), None)
    return {"doc_hit@1": float(first == 1), "doc_hit@5": float(first is not None and first <= 5),
            "doc_mrr": 1.0 / first if first else 0.0}


def score_answers(answers: list[dict], questions: dict[str, dict]) -> pd.DataFrame:
    rows = []
    for a in answers:
        q = questions[a["question_id"]]
        failed = a.get("error_code") is not None
        answerable = q["category"] != NO_ANSWER_CATEGORY
        contexts = a.get("contexts") or []
        facts = extract_facts(q["reference"]) if answerable else set()
        row = {"question_id": a["question_id"], "pipeline": a["pipeline"], "category": q["category"],
               "answerable": answerable, "failed": failed, "n_facts": len(facts)}
        if failed:
            row.update({"doc_hit@1": NAN, "doc_hit@5": NAN, "doc_mrr": NAN, "abstained": None,
                        "facts_in_retrieved": NAN, "facts_in_reference_units": NAN, "facts_in_generation": NAN, "facts_in_answer": NAN})
        else:
            row.update(doc_retrieval(contexts, q["reference_document_ids"] if answerable else []))
            row["abstained"] = abstained(a.get("answer", ""))
            row["facts_in_retrieved"] = coverage(facts, "\n".join(c["text"] for c in contexts))
            refs = set(q["reference_document_ids"])
            row["facts_in_reference_units"] = coverage(
                facts, "\n".join(c["text"] for c in contexts if c["document_id"] in refs))
            row["facts_in_generation"] = coverage(facts, a.get("generation_context") or "")
            row["facts_in_answer"] = coverage(facts, a.get("answer") or "")
        rows.append(row)
    return pd.DataFrame(rows)


METRICS = ["doc_hit@1", "doc_hit@5", "doc_mrr", "false_abstention",
           "facts_in_retrieved", "facts_in_reference_units", "facts_in_generation", "facts_in_answer"]


def _per_question(scores: pd.DataFrame) -> pd.DataFrame:
    s = scores[scores["answerable"] & ~scores["failed"]].copy()
    s["false_abstention"] = s["abstained"].astype(float)
    return s


def summarize(scores: pd.DataFrame) -> pd.DataFrame:
    s = _per_question(scores)
    out = []
    for pipeline, g in s.groupby("pipeline", sort=True):
        row = {"pipeline": pipeline, "n_answerable": len(g),
               "n_with_facts": int((g["n_facts"] > 0).sum())}
        for m in METRICS:
            row[m] = g[m].mean()
        unanswerable = scores[(~scores["answerable"]) & (~scores["failed"]) & (scores["pipeline"] == pipeline)]
        row["no_answer_n"] = len(unanswerable)
        row["no_answer_recognised"] = unanswerable["abstained"].astype(float).mean() if len(unanswerable) else NAN
        out.append(row)
    return pd.DataFrame(out)


def mcnemar_exact(wins: int, losses: int) -> float:
    n = wins + losses
    if n == 0:
        return 1.0
    k = min(wins, losses)
    tail = sum(math.comb(n, i) for i in range(k + 1)) / 2 ** n
    return min(1.0, 2 * tail)


def paired_tests(scores: pd.DataFrame, seed: int = 42, n_resamples: int = 20000) -> pd.DataFrame:
    """Hierarchical minus each flat variant, per metric, paired by question.

    Binary metrics get the exact McNemar test; every metric gets a paired
    bootstrap 95 % CI and a sign-flip permutation p-value (exact up to 20
    non-zero differences, Monte Carlo with `n_resamples` above)."""
    s = _per_question(scores)
    rng = random.Random(seed)
    piv = {m: s.pivot_table(index="question_id", columns="pipeline", values=m) for m in METRICS}
    out = []
    flat = sorted(p for p in s["pipeline"].unique() if p != HIERARCHICAL)
    if HIERARCHICAL not in set(s["pipeline"]):
        return pd.DataFrame()
    for m in METRICS:
        for other in flat:
            pair = piv[m][[HIERARCHICAL, other]].dropna()
            if pair.empty:
                continue
            diffs = (pair[HIERARCHICAL] - pair[other]).tolist()
            n = len(diffs)
            mean = sum(diffs) / n
            boots = sorted(sum(diffs[rng.randrange(n)] for _ in range(n)) / n for _ in range(n_resamples))
            lo, hi = boots[int(0.025 * n_resamples)], boots[int(0.975 * n_resamples) - 1]
            nz = [d for d in diffs if d != 0]
            observed = abs(sum(nz))
            if not nz:
                p_perm = 1.0
            elif len(nz) <= 20:
                hits = 0
                for mask in range(2 ** len(nz)):
                    total = sum(d if (mask >> i) & 1 else -d for i, d in enumerate(nz))
                    hits += abs(total) >= observed - 1e-12
                p_perm = hits / 2 ** len(nz)
            else:
                hits = sum(abs(sum(d if rng.random() < 0.5 else -d for d in nz)) >= observed - 1e-12
                           for _ in range(n_resamples))
                p_perm = (hits + 1) / (n_resamples + 1)
            row = {"metric": m, "against": other, "n": n, "mean_diff": mean, "ci_low": lo, "ci_high": hi,
                   "p_permutation": p_perm}
            if m in ("doc_hit@1", "doc_hit@5", "false_abstention"):
                wins = sum(d > 0 for d in diffs)
                losses = sum(d < 0 for d in diffs)
                row.update({"wins": wins, "losses": losses, "p_mcnemar": mcnemar_exact(wins, losses)})
            out.append(row)
    return pd.DataFrame(out)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("run_dir", type=Path)
    ap.add_argument("--dataset", type=Path, default=None,
                    help="carpeta del dataset; por defecto la de run.json o ../dataset")
    ap.add_argument("--seed", type=int, default=42)
    args = ap.parse_args(argv)

    run, answers = load_run(args.run_dir)
    dataset = args.dataset or Path(run.get("dataset_dir") or Path(__file__).parent.parent / "dataset")
    try:
        questions = {q["id"]: q for q in read_jsonl(dataset / "questions.jsonl")}
    except FileNotFoundError:
        print(f"ERROR no encuentro {dataset / 'questions.jsonl'}; usa --dataset", file=sys.stderr)
        return 1
    unknown = sorted({a["question_id"] for a in answers} - questions.keys())
    if unknown:
        print(f"ERROR preguntas que no están en {dataset}: {', '.join(unknown)} (¿--dataset?)", file=sys.stderr)
        return 1

    scores = score_answers(answers, questions)
    summary = summarize(scores)
    tests = paired_tests(scores, seed=args.seed)
    scores.to_csv(args.run_dir / "judge_free.csv", index=False)
    summary.to_csv(args.run_dir / "judge_free_summary.csv", index=False)
    tests.to_csv(args.run_dir / "judge_free_tests.csv", index=False)
    with pd.option_context("display.width", 200, "display.max_columns", 30, "display.float_format", "{:.3f}".format):
        print(summary.to_string(index=False))
        if not tests.empty:
            print()
            print(tests.to_string(index=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
