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
    calls = {"rows": [], "batches": 0}

    def fake(rows, cfg):
        calls["rows"] = calls["rows"] + rows
        calls["cfg"] = cfg
        calls["batches"] += 1
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
    assert stub_ragas["batches"] == 0


def test_missing_model_writes_nothing(run_dir, dataset_dir, monkeypatch, stub_ragas, capsys):
    monkeypatch.setattr(score.requests, "get", lambda *a, **k: FakeResp(["qwen2.5:7b-instruct-q4_K_M"]))
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 1
    assert "qwen3-embedding:0.6b" in capsys.readouterr().err
    assert not (run_dir / "scores.csv").exists()


def test_interrupted_scoring_resumes_from_the_cache(run_dir, dataset_dir, ollama_ok, stub_ragas, monkeypatch):
    real = score.run_ragas

    def dies_on_third_batch(rows, cfg):
        if stub_ragas["batches"] == 2:
            raise KeyboardInterrupt
        return real(rows, cfg)

    monkeypatch.setattr(score, "run_ragas", dies_on_third_batch)
    with pytest.raises(KeyboardInterrupt):
        score.main([str(run_dir), "--dataset", str(dataset_dir), "--checkpoint-batch", "3"])
    assert not (run_dir / "summary.csv").exists()
    cached = (run_dir / score.CACHE_FILE).read_text().splitlines()
    assert len(cached) == 6  # two batches of 3 were saved before the interruption

    monkeypatch.setattr(score, "run_ragas", real)
    stub_ragas["rows"] = []
    assert score.main([str(run_dir), "--dataset", str(dataset_dir), "--checkpoint-batch", "3"]) == 0
    assert len(stub_ragas["rows"]) == 2  # only the samples that were not scored yet
    assert len(pd.read_csv(run_dir / "scores.csv")) == 9


def test_fresh_ignores_the_cache_and_another_judge_does_not_reuse_it(run_dir, dataset_dir, ollama_ok, stub_ragas, monkeypatch):
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 0
    stub_ragas["rows"] = []
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 0
    assert stub_ragas["rows"] == []  # everything came from the cache

    assert score.main([str(run_dir), "--dataset", str(dataset_dir), "--fresh"]) == 0
    assert len(stub_ragas["rows"]) == 8

    stub_ragas["rows"] = []
    monkeypatch.setattr(score.requests, "get", lambda *a, **k: FakeResp(["qwen2.5:14b", "qwen3-embedding:0.6b"]))
    assert score.main([str(run_dir), "--dataset", str(dataset_dir), "--judge-model", "qwen2.5:14b"]) == 0
    assert len(stub_ragas["rows"]) == 8


def test_a_changed_answer_is_judged_again(run_dir, dataset_dir, ollama_ok, stub_ragas):
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 0
    answers = [json.loads(l) for l in (run_dir / "answers.jsonl").read_text().splitlines() if l.strip()]
    answers[0]["answer"] = answers[0]["answer"] + " (regenerada)"
    (run_dir / "answers.jsonl").write_text("\n".join(json.dumps(a, ensure_ascii=False) for a in answers) + "\n")

    stub_ragas["rows"] = []
    assert score.main([str(run_dir), "--dataset", str(dataset_dir)]) == 0
    assert [r["answer"] for r in stub_ragas["rows"]] == [answers[0]["answer"]]
