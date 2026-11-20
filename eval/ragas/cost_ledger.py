"""Running record of what the evaluation spends on paid APIs.

Every script that calls a paid model appends one line to eval/api-costs.jsonl
as it goes (per question generated, per scoring batch), so the spend is kept
even if a run is interrupted. `python cost_ledger.py` prints the totals.
"""
from __future__ import annotations

import json
import sys
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

LEDGER = Path(__file__).parent.parent / "api-costs.jsonl"
PRICES = {"claude-sonnet-5-5": (2.0, 10.0), "claude-opus-5-5": (4.0, 20.0), "claude-haiku-5-5": (0.10, 0.50)}


def cost(model: str, input_tokens: int, output_tokens: int) -> float:
    p_in, p_out = PRICES.get(model, (0.0, 0.0))
    return input_tokens / 1e6 * p_in + output_tokens / 1e6 * p_out


def record(task: str, target: str, model: str, input_tokens: int, output_tokens: int, calls: int = 1,
           ledger: Path = LEDGER) -> float:
    usd = cost(model, input_tokens, output_tokens)
    if input_tokens or output_tokens:
        entry = {"at": datetime.now(timezone.utc).isoformat(timespec="seconds"), "task": task, "target": target,
                 "model": model, "calls": calls, "input_tokens": input_tokens, "output_tokens": output_tokens,
                 "usd": round(usd, 6)}
        with ledger.open("a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    return usd


def totals(ledger: Path = LEDGER) -> dict:
    by = defaultdict(lambda: {"calls": 0, "input_tokens": 0, "output_tokens": 0, "usd": 0.0})
    if ledger.exists():
        for line in ledger.read_text(encoding="utf-8").splitlines():
            if line.strip():
                e = json.loads(line)
                for key in (("total",), (e["task"],), (e["task"], e["target"])):
                    t = by[" / ".join(key)]
                    for f in ("calls", "input_tokens", "output_tokens"):
                        t[f] += e[f]
                    t["usd"] += e["usd"]
    return dict(by)


if __name__ == "__main__":
    for k, v in sorted(totals().items(), key=lambda kv: (kv[0] != "total", kv[0])):
        print(f"{k:60s} {v['calls']:6d} llamadas {v['input_tokens']:>10,d} in {v['output_tokens']:>9,d} out  {v['usd']:8.2f} USD")
    sys.exit(0)
