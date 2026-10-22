"""Validate eval/dataset: `python validate_dataset.py ../dataset`."""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path

from common import NO_ANSWER_CATEGORY, corpus_ids

# Categories are free lowercase slugs (ayudas, urbanismo, empleo-publico, ...);
# only `sin-respuesta` has special rules.
CATEGORY_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
REQUIRED = {"id": str, "question": str, "reference": str, "reference_document_ids": list, "category": str}


def validate(dataset_dir: Path) -> tuple[list[str], Counter, int]:
    """Return (errors, count per category, corpus size)."""
    errors: list[str] = []
    corpus_path, questions_path = dataset_dir / "corpus.txt", dataset_dir / "questions.jsonl"
    for p in (corpus_path, questions_path):
        if not p.exists():
            errors.append(f"No existe {p}")
    if errors:
        return errors, Counter(), 0

    corpus = corpus_ids(corpus_path)
    for doc, n in Counter(corpus).items():
        if n > 1:
            errors.append(f"corpus.txt: el documento {doc} aparece {n} veces")
    corpus_set = set(corpus)

    seen: set[str] = set()
    categories: Counter = Counter()
    for lineno, line in enumerate(questions_path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            q = json.loads(line)
        except json.JSONDecodeError as ex:
            errors.append(f"línea {lineno}: JSON inválido ({ex.msg})")
            continue
        qid = q.get("id") if isinstance(q.get("id"), str) else f"línea {lineno}"
        bad = [k for k, t in REQUIRED.items() if not isinstance(q.get(k), t)]
        bad += [k for k in ("id", "question", "reference", "category") if k not in bad and not q[k].strip()]
        if bad:
            errors.append(f"{qid}: campos ausentes, vacíos o de tipo incorrecto: {', '.join(bad)}")
            continue
        if qid in seen:
            errors.append(f"{qid}: id repetido")
        seen.add(qid)
        cat, refs = q["category"], q["reference_document_ids"]
        categories[cat] += 1
        if not CATEGORY_RE.match(cat):
            errors.append(f"{qid}: categoría '{cat}' inválida (minúsculas y guiones, p. ej. empleo-publico)")
        if cat == NO_ANSWER_CATEGORY and refs:
            errors.append(f"{qid}: una pregunta '{NO_ANSWER_CATEGORY}' debe tener reference_document_ids vacío")
        if cat != NO_ANSWER_CATEGORY and not refs:
            errors.append(f"{qid}: reference_document_ids vacío en categoría '{cat}'")
        for doc in refs:
            if doc not in corpus_set:
                errors.append(f"{qid}: el documento {doc} no está en corpus.txt")
    return errors, categories, len(corpus_set)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("dataset", type=Path, nargs="?", default=Path("../dataset"))
    args = ap.parse_args(argv)
    errors, categories, corpus_size = validate(args.dataset)
    for e in errors:
        print(f"ERROR {e}", file=sys.stderr)
    total = sum(categories.values())
    print(f"Preguntas: {total} ({'con errores' if errors else 'válidas'})")
    for cat, n in sorted(categories.items()):
        print(f"  {cat}: {n}")
    print(f"Corpus: {corpus_size} documentos")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
