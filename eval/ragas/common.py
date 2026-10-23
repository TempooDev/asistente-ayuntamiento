"""Shared helpers to read a run and the dataset."""
from __future__ import annotations

import json
from pathlib import Path

NO_ANSWER_CATEGORY = "sin-respuesta"


def read_jsonl(path: Path) -> list[dict]:
    with path.open(encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def corpus_ids(path: Path) -> list[str]:
    """`json/{SOURCE}/{id}.json` -> `{id}` (the file stem)."""
    lines = [l.strip() for l in path.read_text(encoding="utf-8").splitlines()]
    return [Path(l).stem for l in lines if l and not l.startswith("#")]


def load_run(run_dir: Path) -> tuple[dict, list[dict]]:
    return read_json(run_dir / "run.json"), read_jsonl(run_dir / "answers.jsonl")
