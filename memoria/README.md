# Memoria del TFG

Memoria en LaTeX con la plantilla de la ETSI Informática (UMA). Las notas de la plantilla original están en [`README.txt`](README.txt).

## Estructura

```
memoria/
  main.tex               preámbulo, portadas e \include de cada parte
  preliminares/          resumen, abstract, agradecimientos, acrónimos
  capitulos/             01-introduccion.tex … 09-conclusiones.tex
  apendices/             a-resultados-chunking.tex … e-prompts.tex
  figuras/               imágenes (\includegraphics{nombre} las busca aquí)
  references.bib         bibliografía (biber)
  .latexmkrc             receta de compilación
  memoria.code-workspace configuración de VS Code
  template/              plantilla UMA (no tocar)
```

Cada sección tiene un `% TODO` con lo que debe contener, según el guion acordado a partir del anteproyecto.

## 1. Instalar LaTeX

La plantilla usa `fontspec`, así que hay que compilar con **XeLaTeX** (no vale pdfLaTeX). También hacen falta `biber` (bibliografía) y `latexmk` (orquesta las pasadas).

| Sistema | Qué instalar |
|---|---|
| Windows | [MiKTeX](https://miktex.org/download) (con instalación automática de paquetes) y [Strawberry Perl](https://strawberryperl.com/), que `latexmk` necesita. También vale [TeX Live](https://tug.org/texlive/windows.html), que ya trae Perl. |
| macOS | [MacTeX](https://tug.org/mactex/) |
| Linux (Debian/Ubuntu) | `sudo apt install texlive-xetex texlive-latex-extra texlive-lang-spanish texlive-science texlive-bibtex-extra biber latexmk` |

Comprueba la instalación con:

```bash
xelatex --version && biber --version && latexmk --version
```

## 2. Configurar VS Code

1. Abre **`memoria/memoria.code-workspace`** (Archivo → Abrir área de trabajo desde archivo). Es importante abrir este fichero y no la raíz del repositorio, porque contiene la configuración de compilación.
2. Instala las extensiones recomendadas cuando VS Code lo proponga:
   - **LaTeX Workshop** (`james-yu.latex-workshop`): compilación, vista previa del PDF y navegación.
   - **LTeX+** (`ltex-plus.vscode-ltex-plus`): corrector ortográfico y gramatical. Opcional; para revisar en castellano, pon `"ltex.language": "es"` en la configuración.

## 3. Compilar y ver el PDF

- **Compilar:** al guardar cualquier `.tex` se recompila solo. También puedes hacerlo a mano con `Ctrl+Alt+B` (`Cmd+Option+B` en macOS).
- **Ver el PDF:** `Ctrl+Alt+V` abre `main.pdf` en una pestaña junto al código y se refresca en cada compilación.
- **Del código al PDF:** `Ctrl+Alt+J` con el cursor en el `.tex` salta a ese punto del PDF.
- **Del PDF al código:** `Ctrl+clic` en el PDF lleva a la línea correspondiente del `.tex`.
- **Errores:** aparecen en el panel *Problemas*. El log completo está en la pestaña de LaTeX Workshop (icono TeX en la barra lateral → *View LaTeX compiler logs*).

Funciona igual editando un capítulo: LaTeX Workshop detecta que la raíz es `main.tex`.

> La primera compilación tarda (sobre todo con MiKTeX, que descarga paquetes). Las siguientes son mucho más rápidas.

## Compilar desde la terminal

```bash
cd memoria
latexmk          # compila main.pdf
latexmk -c       # borra ficheros auxiliares (conserva el PDF)
```

## Overleaf

Si se sube a Overleaf: *Menu → Compiler → XeLaTeX*.

## Convenciones

- **Etiquetas:** `cap:` para capítulos, `sec:` para secciones, `fig:`, `tab:` y `ap:` para apéndices. Referencias con `\cref{...}`.
- **Figuras:** en `figuras/`, preferiblemente PDF o SVG exportado a PDF para los diagramas.
- **Citas:** `\cite{clave}` con las claves de `references.bib`. Cuando los capítulos ya citen, hay que quitar `\nocite{*}` de `main.tex`.
- **Acrónimos:** cada uno nuevo va en `preliminares/acronimos.tex`.
- **Ficheros generados:** no se suben al repositorio (`.gitignore`). Tampoco `main.pdf`.
