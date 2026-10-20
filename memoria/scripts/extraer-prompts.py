#!/usr/bin/env python3
"""Extrae los prompts de PromptCatalog.cs a memoria/apendices/prompts/<Nombre>.txt.

El apéndice E los incluye con \\lstinputlisting, así que basta con volver a ejecutar
este script cuando cambien los prompts:

    python3 memoria/scripts/extraer-prompts.py
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / "src/AsistenteAyuntamiento.Application/Common/Prompts/PromptCatalog.cs"
OUT = ROOT / "memoria/apendices/prompts"

DECL = re.compile(r"public const string (\w+)\s*=\s*", re.M)


def parse_value(src: str, pos: int) -> str:
    """Devuelve el valor del literal C# que empieza en pos."""
    if src.startswith('"""', pos):  # raw string literal: la sangría la marca el cierre
        end = src.index('"""', pos + 3)
        body = src[pos + 3:end]
        lines = body.split("\n")[1:]  # la primera línea (tras las comillas) está vacía
        indent = lines[-1]  # la línea del cierre solo contiene la sangría
        lines = lines[:-1]
        return "\n".join(l[len(indent):] if l.startswith(indent) else l.strip() for l in lines)
    if src.startswith('@"', pos):  # verbatim string: "" es una comilla
        i = pos + 2
        out = []
        while True:
            if src[i] == '"':
                if src[i + 1] == '"':
                    out.append('"')
                    i += 2
                    continue
                break
            out.append(src[i])
            i += 1
        return "".join(out).strip("\n")
    if src.startswith('"', pos) or src[pos] == "\n":  # cadena normal, quizá en la línea siguiente
        start = src.index('"', pos)
        m = re.compile(r'"((?:[^"\\]|\\.)*)"').match(src, start)
        return m.group(1).encode().decode("unicode_escape").encode("latin-1").decode("utf-8")
    raise ValueError(f"literal no reconocido en la posición {pos}")


def main() -> None:
    src = CATALOG.read_text(encoding="utf-8")
    OUT.mkdir(parents=True, exist_ok=True)
    for m in DECL.finditer(src):
        name = m.group(1)
        text = parse_value(src, m.end())
        # Las líneas largas las parte listings al componer (breaklines), sin tocar el texto.
        (OUT / f"{name}.txt").write_text(text + "\n", encoding="utf-8")
        print(f"{name}.txt")


if __name__ == "__main__":
    main()
