import math

import pytest

from score import retrieval_metrics, says_not_covered


def test_hit_and_rr_first_rank():
    assert retrieval_metrics(["A", "B"], ["A"]) == {"hit@1": 1.0, "hit@3": 1.0, "hit@5": 1.0, "reciprocal_rank": 1.0}


def test_hit_and_rr_later_rank():
    m = retrieval_metrics(["X", "Y", "Z", "A", "B"], ["A", "B"])
    assert (m["hit@1"], m["hit@3"], m["hit@5"]) == (0.0, 0.0, 1.0)
    assert m["reciprocal_rank"] == pytest.approx(0.25)


def test_no_hit_is_zero():
    assert retrieval_metrics(["X"], ["A"])["reciprocal_rank"] == 0.0
    assert retrieval_metrics([], ["A"])["hit@5"] == 0.0


def test_no_reference_is_missing():
    assert all(math.isnan(v) for v in retrieval_metrics(["A"], []).values())


@pytest.mark.parametrize("answer,expected", [
    ("No he encontrado información en los boletines.", True),
    ("NO DISPONGO de esos datos", True),
    ("No hay información sobre ello.", True),
    ("Lamentablemente no aparece en los boletines.", True),
    ("El tipo es del 0,5 %.", False),
    ("", False),
])
def test_not_covered_heuristic(answer, expected):
    assert says_not_covered(answer) is expected
