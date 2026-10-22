import pandas as pd
import pytest

import report


def test_report_writes_latency_and_indexing(run_dir):
    assert report.main([str(run_dir)]) == 0
    lat = pd.read_csv(run_dir / "latency.csv")
    assert len(lat) == 3 * 5  # pipelines x stages
    total = lat.set_index(["pipeline", "stage"]).loc[("Baseline", "total")]
    assert total["n"] == 2  # warm-up excluded
    assert total["mean_ms"] == pytest.approx(5200) and total["p50_ms"] == pytest.approx(5200)
    assert lat.set_index(["pipeline", "stage"]).loc[("Semantic", "total"), "n"] == 1  # failed excluded
    assert lat.set_index(["pipeline", "stage"]).loc[("Hierarchical", "expansion"), "mean_ms"] == pytest.approx(300)

    idx = pd.read_csv(run_dir / "indexing.csv").set_index("pipeline")
    b = idx.loc["Baseline"]
    assert b["documents"] == 4 and b["mean_ms_per_document"] == pytest.approx(2000)
    assert b["p95_ms_per_document"] == pytest.approx(2850)
    assert b["mean_chunks_per_document"] == pytest.approx(2.5) and b["embedded_tokens"] == 3000
    h = idx.loc["Hierarchical"]
    assert (h["llm_calls"], h["llm_tokens"], h["points"]) == (7, 5200, 30)
