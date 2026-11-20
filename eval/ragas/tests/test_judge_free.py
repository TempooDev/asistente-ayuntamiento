import math

import pandas as pd

import judge_free as jf


def test_extract_facts_keeps_specific_figures_and_dates():
    ref = "Ayuda de 3.000 euros (el 15 % del coste) hasta el 15 de octubre de 2026; plazo de 20 días; BOP n.º 87."
    assert jf.extract_facts(ref) == {"3000", "15%", "15 de octubre"}


def test_extract_facts_keeps_decimals_and_skips_leading_zero():
    assert jf.extract_facts("Importe 106.113.934,64 euros, a las 08:00 horas") == {"106113934,64"}


def test_coverage_matches_canonical_forms():
    facts = jf.extract_facts("3.000 euros antes del 15 de octubre")
    assert jf.coverage(facts, "Se concede una ayuda de 3000 € (plazo: 15 de Octubre).") == 1.0
    assert jf.coverage(facts, "Ayuda de 2.000 euros hasta el 15 de octubre") == 0.5
    assert math.isnan(jf.coverage(set(), "lo que sea"))


def test_abstained_needs_negation_and_information_word_in_the_same_sentence():
    assert jf.abstained("Los documentos no contienen información sobre esa convocatoria.")
    assert jf.abstained("No dispongo de datos sobre ese municipio.")
    assert not jf.abstained("No es necesario aportar más documentación. La información está en el BOE.")
    assert not jf.abstained("x" * 450 + " No hay información.")  # only the first 400 characters count


def test_doc_retrieval_collapses_repeated_documents():
    contexts = [{"document_id": d} for d in ["A", "A", "B", "A", "C"]]
    out = jf.doc_retrieval(contexts, ["C"])
    assert out == {"doc_hit@1": 0.0, "doc_hit@5": 1.0, "doc_mrr": 1 / 3}


def test_mcnemar_exact():
    assert jf.mcnemar_exact(0, 0) == 1.0
    assert abs(jf.mcnemar_exact(10, 2) - 0.0386) < 1e-3
    assert abs(jf.mcnemar_exact(3, 3) - 1.0) < 1e-12


def _answers():
    questions = {
        "q1": {"id": "q1", "category": "mediano", "reference": "Plazo hasta el 15 de octubre",
               "reference_document_ids": ["D1"]},
        "q2": {"id": "q2", "category": "sin-respuesta", "reference": "No consta",
               "reference_document_ids": []},
    }
    answers = []
    for p, ctx_doc, ans in [("Hierarchical", "D1", "El plazo acaba el 15 de octubre."),
                            ("Baseline", "D2", "Los documentos no contienen información sobre el plazo.")]:
        answers.append({"question_id": "q1", "pipeline": p, "answer": ans, "error_code": None,
                        "contexts": [{"document_id": ctx_doc, "text": "... 15 de octubre ..." if ctx_doc == "D1" else "otra cosa"}],
                        "generation_context": "... 15 de octubre ..." if ctx_doc == "D1" else "otra cosa"})
        answers.append({"question_id": "q2", "pipeline": p, "answer": "No hay información sobre eso.",
                        "error_code": None, "contexts": [], "generation_context": ""})
    return answers, questions


def test_score_and_summary():
    answers, questions = _answers()
    scores = jf.score_answers(answers, questions)
    summary = jf.summarize(scores).set_index("pipeline")
    assert summary.loc["Hierarchical", "doc_hit@1"] == 1.0
    assert summary.loc["Baseline", "doc_hit@1"] == 0.0
    assert summary.loc["Baseline", "false_abstention"] == 1.0
    assert summary.loc["Hierarchical", "facts_in_reference_units"] == 1.0
    assert summary.loc["Baseline", "no_answer_recognised"] == 1.0


def test_paired_tests_compare_hierarchical_with_each_flat_variant():
    answers, questions = _answers()
    tests = jf.paired_tests(jf.score_answers(answers, questions), n_resamples=200)
    hit = tests[(tests.metric == "doc_hit@1") & (tests.against == "Baseline")].iloc[0]
    assert hit["mean_diff"] == 1.0 and hit["wins"] == 1 and hit["losses"] == 0
    assert isinstance(tests, pd.DataFrame)
