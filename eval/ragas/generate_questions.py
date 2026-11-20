"""Generate an evaluation question set with Claude from a stratified random sample of the collection.

    python generate_questions.py --out ../dataset-e5 [--n 72] [--unanswerable 8] [--seed 20261120]
                                 [--limit N] [--model claude-sonnet-5-5] [--dry-run]

Steps (all reproducible from the seed):
1. Sample documents from `<out>/candidates.csv` (the active parent documents of
   collection C3, exported from PostgreSQL), stratified by bulletin (BOE/BOJA),
   size tertile and whether the issuer is a single municipality. Documents used
   by the existing question sets are excluded, so the new set is unseen.
2. Read each document's full text from PostgreSQL (through `podman exec`, the
   same database the variants were ingested from) into `<out>/.cache/`.
3. Ask the model for one question per document, written as municipal staff
   would ask it, with a reference answer and the verbatim passage that supports
   it. Documents longer than WINDOW characters are shown as one window chosen
   at random, so answers are not only in the title or the opening lines.
   A few extra documents get an unanswerable question instead.
4. Check every answerable item: the verbatim passage must occur in the
   document. Items that fail are dropped and reported, never repaired by hand.
5. Write `questions.jsonl`, `corpus.txt` and `generation.json` (model, prompts,
   seed, sample, token usage and estimated cost, overlap statistics).

The API key is read from ANTHROPIC_API_KEY or, failing that, from
~/tfg-eval/anthropic.key (outside the repository).
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import random
import re
import subprocess
import sys
import unicodedata
from datetime import datetime, timezone
from pathlib import Path
from typing import Literal

from pydantic import BaseModel

import cost_ledger

HERE = Path(__file__).parent
EVAL = HERE.parent
MODEL = "claude-sonnet-5-5"
PRICE_IN, PRICE_OUT = 2.0, 10.0  # USD per million tokens, Claude Sonnet 5.5
WINDOW = 60_000
POSTGRES_CONTAINER_PREFIX = "postgres-"
DATABASE = "asistente-ayuntamiento-db"

SYSTEM = """Eres un asistente que prepara preguntas de evaluación para un buscador de boletines oficiales (BOE y BOJA) que usa el personal de los ayuntamientos.

Vas a recibir un documento de un boletín. Tu tarea es escribir UNA pregunta como la escribiría un funcionario o administrativo de un ayuntamiento pequeño que no conoce el documento: con sus propias palabras, de forma natural y directa, sin copiar el título ni frases del texto, sin citar el número del boletín ni el identificador del documento, y sin dar pistas que solo conoce quien ya lo ha leído. La pregunta debe ser concreta (un plazo, un importe, un requisito, un número de plazas, una fecha, un beneficiario, un órgano responsable...) y tener una respuesta inequívoca en el documento.

Devuelve también:
- reference_answer: la respuesta correcta en una o dos frases, con los datos concretos (cifras, fechas, nombres) tal como aparecen en el documento.
- supporting_quote: un fragmento LITERAL del documento, copiado carácter a carácter, de entre 40 y 400 caracteres, que contenga la respuesta.
- question_type: el tipo de dato que se pregunta."""

UNANSWERABLE = """Ahora, en lugar de una pregunta con respuesta, escribe una pregunta PLAUSIBLE que un funcionario podría hacer sobre un tema muy cercano al de este documento pero que el documento NO responde: por ejemplo, la misma convocatoria en otro municipio, otro cuerpo o categoría, otra ayuda u otro año. No debe ser absurda ni evidente. En reference_answer escribe «Los boletines consultados no recogen esa información.» y en supporting_quote deja una cadena vacía."""

QuestionType = Literal["plazo", "importe", "plazas", "requisito", "fecha", "beneficiario", "organo", "procedimiento", "otro"]


class GeneratedQuestion(BaseModel):
    question: str
    reference_answer: str
    supporting_quote: str
    question_type: QuestionType


# ---------------------------------------------------------------- sampling

def fold(text: str) -> str:
    nfkd = unicodedata.normalize("NFKD", (text or "").lower())
    plain = "".join(ch for ch in nfkd if not unicodedata.combining(ch))
    return re.sub(r"\s+", " ", plain).strip()


def is_local(row: dict) -> bool:
    return bool(row["municipality"].strip()) or "ayuntamiento" in fold(row["issuing_body"])


def used_ids() -> set[str]:
    ids = set()
    for ds in ("dataset", "dataset-sizes"):
        corpus = EVAL / ds / "corpus.txt"
        if corpus.exists():
            ids |= {Path(l.strip()).stem for l in corpus.read_text(encoding="utf-8").splitlines() if l.strip()}
        qfile = EVAL / ds / "questions.jsonl"
        if qfile.exists():
            for line in qfile.read_text(encoding="utf-8").splitlines():
                if line.strip():
                    ids |= set(json.loads(line)["reference_document_ids"])
    return ids


def stratified_sample(rows: list[dict], n: int, seed: int, boja_share: float = 0.25) -> list[dict]:
    """Equal allocation across size tertiles and local/non-local within each bulletin,
    with `boja_share` of the sample from BOJA (it is 6 % of the collection, so it is oversampled)."""
    rng = random.Random(seed)
    sizes = sorted(int(r["chars"]) for r in rows)
    t1, t2 = sizes[len(sizes) // 3], sizes[2 * len(sizes) // 3]
    for r in rows:
        c = int(r["chars"])
        r["size_class"] = "pequeno" if c <= t1 else "mediano" if c <= t2 else "grande"
        r["local"] = is_local(r)
    quotas = {"BOE": round(n * (1 - boja_share)), "BOJA": n - round(n * (1 - boja_share))}
    picked: list[dict] = []
    for bulletin, quota in quotas.items():
        cells = {}
        for r in rows:
            if r["bulletin"] == bulletin:
                cells.setdefault((r["size_class"], r["local"]), []).append(r)
        keys = sorted(cells)
        for k in keys:
            rng.shuffle(cells[k])
        i = 0
        while sum(1 for p in picked if p["bulletin"] == bulletin) < quota and any(cells.values()):
            k = keys[i % len(keys)]
            if cells[k]:
                picked.append(cells[k].pop())
            i += 1
    rng.shuffle(picked)
    return picked


# ---------------------------------------------------------------- documents

def postgres_container() -> str:
    out = subprocess.run(["podman", "ps", "--format", "{{.Names}}"], capture_output=True, text=True, check=True).stdout
    names = [n for n in out.split() if n.startswith(POSTGRES_CONTAINER_PREFIX)]
    if not names:
        raise SystemExit("ERROR no hay ningún contenedor postgres-* en marcha (arranca Aspire)")
    return names[0]


def fetch_texts(ids: list[str], cache: Path) -> dict[str, str]:
    cache.mkdir(parents=True, exist_ok=True)
    missing = [i for i in ids if not (cache / f"{i}.txt").exists()]
    if missing:
        container = postgres_container()
        id_list = ",".join("'" + i.replace("'", "") + "'" for i in missing)
        # psql -A prints the JSON as is (one row per line: JSON escapes the newlines inside the text)
        sql = (f'select json_build_object(\'id\', "DocumentId", \'title\', "NormTitle", \'text\', "FullText")::text '
               f'from ingestion."ParentDocuments" where "IsActive" and "DocumentId" in ({id_list})')
        cmd = ["podman", "exec", container, "sh", "-c",
               f'PGPASSWORD=$POSTGRES_PASSWORD psql -U postgres -d {DATABASE} -At -c "$0"', sql]
        out = subprocess.run(cmd, capture_output=True, check=True).stdout.decode("utf-8")
        for line in out.split("\n"):  # not splitlines(): it also breaks on U+2028 and similar, which JSON keeps raw
            if line.strip():
                doc = json.loads(line)
                (cache / f"{doc['id']}.txt").write_text((doc.get("title") or "") + "\n\n" + (doc["text"] or ""), encoding="utf-8")
    return {i: (cache / f"{i}.txt").read_text(encoding="utf-8") for i in ids if (cache / f"{i}.txt").exists()}


def window(text: str, rng: random.Random) -> tuple[str, int]:
    if len(text) <= WINDOW:
        return text, 0
    start = rng.randrange(0, len(text) - WINDOW)
    return text[start:start + WINDOW], start


# ---------------------------------------------------------------- checks

STOP = set("de la el en y a los las del que por con para un una se su al lo como o es sus le ha han hay cual cuales "
           "cuanto cuantos cuanta cuantas que quien donde cuando qué cuál cuánto".split())


def content_words(text: str) -> set[str]:
    return {w for w in re.findall(r"[a-z0-9ñ]+", fold(text)) if len(w) > 2 and w not in STOP}


def lexical_overlap(question: str, document: str) -> float:
    """Share of the question's content words that appear in the document."""
    q = content_words(question)
    return len(q & content_words(document)) / len(q) if q else 0.0


def quote_found(quote: str, text: str) -> bool:
    return bool(quote.strip()) and fold(quote) in fold(text)


# ---------------------------------------------------------------- generation

def api_key() -> str:
    key = os.environ.get("ANTHROPIC_API_KEY")
    if not key:
        path = Path.home() / "tfg-eval" / "anthropic.key"
        if path.exists():
            key = path.read_text(encoding="utf-8").strip()
    if not key:
        raise SystemExit("ERROR falta ANTHROPIC_API_KEY (o ~/tfg-eval/anthropic.key)")
    return key


PREFERRED_TYPES = ["importe", "requisito", "fecha", "plazas", "beneficiario", "organo", "procedimiento", "plazo"]


def ask(client, model: str, doc_id: str, text: str, unanswerable: bool,
        preferred: str | None = None) -> tuple[GeneratedQuestion | None, dict]:
    content = f"<documento id=\"{doc_id}\">\n{text}\n</documento>"
    if preferred and not unanswerable:
        # Rotating the preferred kind of fact keeps the set from becoming all "¿cuál es el plazo?"
        content += (f"\n\nSi el documento lo permite, pregunta por un dato de tipo «{preferred}»; "
                    "si no lo tiene, elige el dato más útil para un ayuntamiento.")
    if unanswerable:
        content += "\n\n" + UNANSWERABLE
    resp = client.messages.parse(
        model=model, max_tokens=4000, system=SYSTEM,
        output_config={"effort": "medium"},
        messages=[{"role": "user", "content": content}],
        output_format=GeneratedQuestion,
    )
    usage = {"input_tokens": resp.usage.input_tokens, "output_tokens": resp.usage.output_tokens,
             "stop_reason": resp.stop_reason}
    if resp.stop_reason != "end_turn" or resp.parsed_output is None:
        return None, usage
    return resp.parsed_output, usage


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--n", type=int, default=72, help="documentos con pregunta con respuesta")
    ap.add_argument("--unanswerable", type=int, default=8, help="documentos con pregunta sin respuesta")
    ap.add_argument("--seed", type=int, default=20261120)
    ap.add_argument("--limit", type=int, default=None, help="genera solo las N primeras (prueba de coste)")
    ap.add_argument("--model", default=MODEL)
    ap.add_argument("--dry-run", action="store_true", help="muestrea y descarga los textos sin llamar a la API")
    args = ap.parse_args(argv)

    with (args.out / "candidates.csv").open(encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    excluded = used_ids()
    pool = [r for r in rows if r["document_id"] not in excluded]
    sample = stratified_sample(pool, args.n + args.unanswerable, args.seed)
    plan = [(r, i >= args.n) for i, r in enumerate(sample)]
    if args.limit:
        plan = plan[:args.limit]
    texts = fetch_texts([r["document_id"] for r, _ in plan], args.out / ".cache")
    print(f"Muestra: {len(plan)} documentos ({sum(u for _, u in plan)} sin respuesta); "
          f"{sum(len(t) for t in texts.values()) / 1e6:.2f} M caracteres en total", file=sys.stderr)
    if args.dry_run:
        for r, u in plan:
            print(r["document_id"], r["bulletin"], r["size_class"], "local" if r["local"] else "-", "SIN" if u else "", r["chars"])
        return 0

    import anthropic
    client = anthropic.Anthropic(api_key=api_key())
    rng = random.Random(args.seed + 1)
    items, rejected, usage_total = [], [], {"input_tokens": 0, "output_tokens": 0}
    for idx, (r, unanswerable) in enumerate(plan, 1):
        doc_id = r["document_id"]
        if doc_id not in texts:
            rejected.append({"document_id": doc_id, "reason": "texto no encontrado"})
            continue
        shown, offset = window(texts[doc_id], rng)
        try:
            gen, usage = ask(client, args.model, doc_id, shown, unanswerable,
                             PREFERRED_TYPES[(idx - 1) % len(PREFERRED_TYPES)])
        except anthropic.APIStatusError as ex:
            rejected.append({"document_id": doc_id, "reason": f"API {ex.status_code}"})
            continue
        usage_total["input_tokens"] += usage["input_tokens"]
        usage_total["output_tokens"] += usage["output_tokens"]
        cost_ledger.record("generar-preguntas", args.out.resolve().name, args.model,
                           usage["input_tokens"], usage["output_tokens"])
        if gen is None:
            rejected.append({"document_id": doc_id, "reason": f"stop_reason {usage['stop_reason']}"})
            continue
        if not unanswerable and not quote_found(gen.supporting_quote, shown):
            rejected.append({"document_id": doc_id, "reason": "la cita no aparece literalmente en el documento",
                             "question": gen.question})
            continue
        items.append({
            "id": f"e{idx:03d}",
            "question": gen.question.strip(),
            "reference": gen.reference_answer.strip(),
            "reference_document_ids": [] if unanswerable else [doc_id],
            "category": "sin-respuesta" if unanswerable else r["size_class"],
            "source_document_id": doc_id,
            "supporting_quote": gen.supporting_quote.strip(),
            "question_type": gen.question_type,
            "bulletin": r["bulletin"], "local": r["local"], "window_offset": offset,
            "lexical_overlap": round(lexical_overlap(gen.question, texts[doc_id]), 3),
        })
        cost = usage_total["input_tokens"] / 1e6 * PRICE_IN + usage_total["output_tokens"] / 1e6 * PRICE_OUT
        print(f"[{idx}/{len(plan)}] {doc_id}: {gen.question[:90]}  (acumulado {cost:.2f} USD)", file=sys.stderr)

    args.out.mkdir(parents=True, exist_ok=True)
    fields = ("id", "question", "reference", "reference_document_ids", "category")
    with (args.out / "questions.jsonl").open("w", encoding="utf-8") as f:
        for it in items:
            f.write(json.dumps({k: it[k] for k in fields}, ensure_ascii=False) + "\n")
    with (args.out / "questions-detail.jsonl").open("w", encoding="utf-8") as f:
        for it in items:
            f.write(json.dumps(it, ensure_ascii=False) + "\n")
    corpus = sorted({f"json/{it['bulletin']}/{it['source_document_id']}.json" for it in items})
    (args.out / "corpus.txt").write_text("\n".join(corpus) + "\n", encoding="utf-8")
    cost = usage_total["input_tokens"] / 1e6 * PRICE_IN + usage_total["output_tokens"] / 1e6 * PRICE_OUT
    overlaps = sorted(it["lexical_overlap"] for it in items if it["category"] != "sin-respuesta")
    (args.out / "generation.json").write_text(json.dumps({
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "model": args.model, "effort": "medium", "seed": args.seed, "window_chars": WINDOW,
        "requested": {"answerable": args.n, "unanswerable": args.unanswerable, "limit": args.limit},
        "system_prompt": SYSTEM, "unanswerable_prompt": UNANSWERABLE,
        "sample": [{"document_id": r["document_id"], "bulletin": r["bulletin"], "size_class": r["size_class"],
                    "local": r["local"], "unanswerable": u} for r, u in plan],
        "kept": len(items), "rejected": rejected,
        "usage": usage_total, "estimated_cost_usd": round(cost, 4),
        "lexical_overlap_median": overlaps[len(overlaps) // 2] if overlaps else None,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{len(items)} preguntas, {len(rejected)} descartadas, {usage_total} tokens, {cost:.2f} USD", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
