# Anotación humana de respuestas

Muestra ciega de 60 respuestas del estudio para comprobar si el juez automático coincide con una persona. Hay 5 respuestas por variante en cada uno de estos conjuntos: E3 (conjunto principal, colección completa), E4 (batería variada) y E5 (preguntas generadas con Claude Sonnet 5.5). Todas son preguntas que tienen respuesta en la colección.

Cada anotador rellena **su propio fichero**, sin consultar el del otro: el anotador A, `anotacion-A.csv`, y el anotador B, `anotacion-B.csv`. Los ficheros se abren con Excel, que usa el punto y coma como separador. No se indica qué variante escribió cada respuesta, y el orden es aleatorio. La correspondencia está en `clave.csv`: no la abras hasta haber terminado.

Para cada fila, lee la pregunta, la respuesta de referencia, la respuesta del sistema y el texto que recibió el modelo (columna `context`, recortada a 6.000 caracteres). Después rellena las tres columnas:

| Columna | Valores | Criterio |
|---|---|---|
| `correcta` | `si` / `parcial` / `no` | `si` si la respuesta da el dato de la referencia sin errores; `parcial` si da solo una parte o con un error menor; `no` si no lo da, da otro o se abstiene. |
| `fiel` | `si` / `no` | `si` si todo lo que afirma la respuesta está en el texto que recibió el modelo. Una frase de cortesía sin contenido no cuenta, pero un dato inventado o atribuido a otro documento sí. |
| `se_abstiene` | `si` / `no` | `si` si la respuesta dice que no tiene la información, aunque añada algo más. |

No hace falta ser experto en los temas de los boletines: basta con comparar lo que dice la respuesta con la referencia y con el texto.

Al terminar, el acuerdo entre los dos anotadores, y el de los dos con el juez, se calcula con:

```bash
cd eval/ragas
uv run python annotation_sample.py agree ../annotation/anotacion-A.csv ../annotation/anotacion-B.csv \
  --judge ../runs/20261115-004843-sonnet ../runs/20261115-010523-sonnet ../runs/20261120-202058-sonnet
```
