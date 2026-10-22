import json
import math

import pandas as pd
import pytest
import requests

import score


class FakeResp:
    def __init__(self, names):
        self._names = names

    def raise_for_status(self):
        pass

    def json(self):
        return {"models": [{"name": n} for n in self._names]}


@pytest.fixture
def ollama_ok(monkeypatch):
    monkeypatch.setattr(score.requests, "get", lambda *a, **k: FakeResp(
        ["qwen2.5:7b-instruct-q4_K_M", "qwen3-embedding:0.6b"]))


@pytest.fixture
def stub_ragas(monkeypatch):
    calls = {}

    def fake(rows, cfg):
        calls["rows"], calls["cfg"] = rows, cfg
        out = []
        for r in rows:
            s = {"context_precision": 0.8, "context_recall": 0.6, "faithfulness": 0.9, "answer_relevancy": 0.7}
            if "0,5 %" in r["answer"]:
                s["faithfulness"] = 0.2  # hallucination
            if r["answer"].startswith("Antes") and r["contexts"][0].startswith("Real Decreto"):
                s["context_precision"] = float("nan")  # judge error
            out.append(s)
        return out

    monkeypatch.setattr(score, "run_ragas", fake)
    return calls


def test_score_writes_outputs(run_dir, dataset_dir, ollama_ok, stub_ragas):
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 0
    assert len(stub_ragas["rows"]) == 8  # failed sample is not sent to the judge
    assert stub_ragas["cfg"]["judge_model"] == "qwen2.5:7b-instruct-q4_K_M"
    assert stub_ragas["cfg"]["embedding_model"] == "qwen3-embedding:0.6b"

    scores = pd.read_csv(run_dir / "scores.csv")
    assert len(scores) == 9
    for col in score.METRICS:
        assert col in scores.columns
    failed = scores[scores["failed"]].iloc[0]
    assert all(math.isnan(failed[c]) for c in score.METRICS)  # NaN, never 0
    no_ans = scores[scores["category"] == "sin-respuesta"]
    assert no_ans[score.RETRIEVAL_COLUMNS].isna().all().all()

    s = pd.read_csv(run_dir / "summary.csv").set_index("pipeline")
    assert list(s.index) == ["Baseline", "Hierarchical", "Semantic"]
    assert s.loc["Baseline", "reciprocal_rank_mean"] == pytest.approx(0.75)
    assert s.loc["Baseline", "hit@1_mean"] == pytest.approx(0.5)
    assert s.loc["Hierarchical", "reciprocal_rank_mean"] == pytest.approx(2 / 3)
    assert s.loc["Semantic", "reciprocal_rank_mean"] == 0.0
    assert s.loc["Semantic", "failed"] == 1 and s.loc["Baseline", "failed"] == 0
    assert s.loc["Baseline", "missing"] == 1 and s.loc["Baseline", "context_precision_n"] == 2
    assert s.loc["Hierarchical", "hallucination_rate"] == pytest.approx(1 / 3)
    assert s.loc["Baseline", "hallucination_rate"] == 0.0
    assert s.loc["Baseline", "no_answer_detected_rate"] == 1.0
    assert s.loc["Hierarchical", "no_answer_detected_rate"] == 0.0
    assert s.loc["Semantic", "no_answer_detected_rate"] == 1.0

    meta = json.loads((run_dir / "scoring.json").read_text())
    assert meta["judge_model"] == "qwen2.5:7b-instruct-q4_K_M" and meta["ragas_version"] == "0.3.9"


def test_judge_model_from_env(run_dir, dataset_dir, monkeypatch, stub_ragas):
    monkeypatch.setenv("EVAL_JUDGE_MODEL", "qwen2.5:14b")
    monkeypatch.setattr(score.requests, "get", lambda *a, **k: FakeResp(["qwen2.5:14b", "qwen3-embedding:0.6b"]))
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 0
    assert stub_ragas["cfg"]["judge_model"] == "qwen2.5:14b"


def test_ollama_unreachable_writes_nothing(run_dir, dataset_dir, monkeypatch, stub_ragas, capsys):
    def boom(*a, **k):
        raise requests.ConnectionError("refused")

    monkeypatch.setattr(score.requests, "get", boom)
    assert score.main([str(run_dir), "--dataset", str(dataset_dir), "--ollama-url", "http://nohost:11434"]) == 1
    err = capsys.readouterr().err
    assert "http://nohost:11434" in err and "qwen2.5:7b-instruct-q4_K_M" in err
    assert not any((run_dir / f).exists() for f in ("scores.csv", "summary.csv", "scoring.json"))
    assert "rows" not in stub_ragas


def test_missing_model_writes_nothing(run_dir, dataset_dir, monkeypatch, stub_ragas, capsys):
    monkeypatch.setattr(score.requests, "get", lambda *a, **k: FakeResp(["qwen2.5:7b-instruct-q4_K_M"]))
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 1
    assert "qwen3-embedding:0.6b" in capsys.readouterr().err
    assert not (run_dir / "scores.csv").exists()
