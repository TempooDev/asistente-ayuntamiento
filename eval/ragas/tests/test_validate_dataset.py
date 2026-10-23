import json

import validate_dataset


def test_fixture_dataset_is_valid(dataset_dir, capsys):
    assert validate_dataset.main([str(dataset_dir)]) == 0
    out = capsys.readouterr().out
    assert "Preguntas: 3 (válidas)" in out
    assert "sin-respuesta: 1" in out and "Corpus: 4 documentos" in out


def _append(dataset_dir, **q):
    base = {"id": "q99", "question": "¿?", "reference": "r", "reference_document_ids": ["BOE-A-2026-18178"],
            "category": "ayudas"}
    with (dataset_dir / "questions.jsonl").open("a", encoding="utf-8") as fh:
        fh.write(json.dumps({**base, **q}) + "\n")


def test_reference_outside_corpus(dataset_dir, capsys):
    _append(dataset_dir, reference_document_ids=["BOE-A-2026-9999"])
    assert validate_dataset.main([str(dataset_dir)]) == 1
    err = capsys.readouterr().err
    assert "q99" in err and "BOE-A-2026-9999" in err


def test_duplicate_id_missing_field_and_bad_no_answer(dataset_dir):
    _append(dataset_dir, id="q01")
    _append(dataset_dir, id="q98", reference="")
    _append(dataset_dir, id="q97", category="sin-respuesta")
    _append(dataset_dir, id="q96", category="Mal Categoria")
    errors, _, _ = validate_dataset.validate(dataset_dir)
    assert any("q01: id repetido" in e for e in errors)
    assert any(e.startswith("q98") and "reference" in e for e in errors)
    assert any(e.startswith("q97") and "vacío" in e for e in errors)
    assert any(e.startswith("q96") and "categoría" in e for e in errors)
