# Preguntas candidatas: batería variada (tamaño y distractores)

Borrador para revisar. Sale del corpus completo de R2 que está ingiriéndose en TEMPO1: 5.994 documentos, que son 5.656 BOE (1/6–5/9/2026) y 338 BOJA. Las respuestas de referencia están copiadas del propio documento. Marca las que valen, corrige las que no te cuadren y descarta el resto. Las que aceptes pasan a `questions.jsonl`. Ahora mismo contiene solo las del bloque A.

Columnas: **Q** = id, **Docs** = documentos que la responden, **Distractores** = documentos parecidos que pueden confundir al buscador.

Categorías: `pequeno` (≤ 2 KB), `mediano` (15–25 KB), `grande` (85–180 KB), `distractor` (hay documentos casi iguales en el corpus), `mixto` (varios documentos de tamaños distintos) y `sin-respuesta`.

## A. Por tamaño (ya en `questions.jsonl`)

En los grandes la respuesta está a propósito en mitad del texto (tablas, artículos, anexos) y no en el título.

| Q | Tamaño | Pregunta | Respuesta de referencia | Docs |
|---|---|---|---|---|
| s01 | 1 KB | ¿Qué plazas ha convocado el Ayuntamiento de Cerdedo-Cotobade y por qué sistema? | Tres de Limpiador/a (dos de funcionario y una laboral fija) y una de Oficial electricista, todas por concurso-oposición libre. BOP de Pontevedra n.º 87 (11/05/2026). | BOE-A-2026-15708 |
| s02 | 1 KB | ¿Cuántas plazas de Agente de Policía Local convoca Sollana y por qué sistemas? | Tres: dos por oposición libre y una por concurso de movilidad. BOP de Valencia n.º 145 (31/07/2026). | BOE-A-2026-18238 |
| s03 | 2 KB | ¿Cuántas plazas de Subalterno/a ha convocado Barcelona y cuál es el plazo? | 146 (136 libres y 10 para personas con discapacidad). Veinte días naturales desde la publicación en el DOGC. | BOE-A-2026-14620 |
| s04 | 1 KB | ¿Qué plazo hay para las plazas de Ingeniero Técnico Industrial de la Diputación de Sevilla? | Diez días hábiles desde la publicación en el BOE. Son dos plazas; extracto en el BOJA n.º 152. | BOE-A-2026-18397 |
| s05 | 1 KB | ¿Cuántas plazas de Administrativo convoca Abanto y Ciérvana-Abanto Zierbena? | Cinco por oposición: cuatro libres y una de promoción interna. | BOE-A-2026-12763 |
| s06 | 1 KB | ¿Qué plazas de Inspector de Policía Local ha convocado Almassora? | Tres por concurso-oposición: una libre y dos de promoción interna. | BOE-A-2026-12352 |
| s07 | 1 KB | ¿Qué plaza convoca Garafía y en qué boletines se rectificaron sus bases? | Director/a de Residencia de Mayores (laboral fijo). Rectificada en los BOP n.º 89, 94 y 101. | BOE-A-2026-18651 |
| s08 | 2 KB | ¿Qué error se corrigió en el código de la plaza de Catedrático de Economía Aplicada de la Universidad de Málaga? | 016CUN25 debe decir 016CUN26. | BOE-A-2026-16305 |
| m01 | 21 KB | ¿Cuánto paga Carreño en el convenio con la CH del Cantábrico para conservar cauces? | 80.000 € (70,18 % de las zonas urbanas). La Confederación pone 120.000 €; cuatro años prorrogables otros cuatro. | BOE-A-2026-13005 |
| m02 | 23 KB | ¿Qué potencia tiene el almacenamiento Valdecaballero en Casas de Don Pedro? | 38,5 MW con el parque solar de 42,299 MW: híbrida de 50 MW (Enel Green Power). | BOE-A-2026-12863 |
| m03 | 15 KB | ¿Qué conjunto de Madrid es Lugar de Memoria Democrática por las fotos de Robert Capa? | Peironcely 10, la plaza del fotógrafo Robert Capa, la zona ajardinada y la parroquia de San Carlos Borromeo (Vallecas). | BOE-A-2026-12788 |
| m04 | 17 KB | ¿Desde cuándo se explotaron las minas de wolframio de la Peña del Seo y qué protección se propone? | Desde 1940 (concesión oficial en 1944). Se incoa su declaración como BIC, Conjunto Industrial. | BOE-A-2026-15298 |
| m05 | 21 KB | ¿Qué plazo hay para la declaración responsable de la ayuda del art. 46 del plan de Oriente Medio? | Quince días hábiles desde la publicación de la Orden APA/568/2026. | BOE-A-2026-12279 |
| m06 | 17 KB | ¿Cuántos días hay ahora para comunicar la baja de un trabajador a la Seguridad Social? | Seis (antes tres días naturales); Real Decreto 643/2026. | BOE-A-2026-16557 |
| m07 | 23 KB | ¿Cuál es la dotación del Premio Comunicación de Protección de Datos 2026 y hasta cuándo hay plazo? | 3.000 €; hasta el 15/10/2026. | BOE-A-2026-15843 |
| m08 | 23 KB | ¿Cuánto dura el convenio Aula Mentor con la Mancomunidad Alto Águeda? | Cuatro años, prorrogables otros cuatro. | BOE-A-2026-13634 |
| l01 | 166 KB | ¿Qué ayuda recibe un autónomo de Ceuta por el RDL 22/2026 y hasta cuándo puede pedirla? | 5.000 € de pago único; formulario en la AEAT hasta el 30/11/2026. | BOE-A-2026-18429 |
| l02 | 106 KB | ¿Hasta cuándo se puede pedir Tardes con Plan 2 y con qué presupuesto? | Hasta el 30/09/2026; 106.113.934,64 € (FSE+). | BOE-A-2026-14638 |
| l03 | 123 KB | ¿A qué ministerio está adscrito el Instituto de las Mujeres y dónde tiene su sede? | Ministerio de Igualdad (a través de la Secretaría de Estado); sede en Madrid. | BOE-A-2026-16550 |
| l04 | 106 KB | ¿Cuánto recibe la Fundación ONCE para el Observatorio Estatal de la Soledad No Deseada? | 100.000 € (RD 642/2026). | BOE-A-2026-16556 |
| l05 | 110 KB | ¿Qué plazo tienen las pymes andaluzas para las ayudas de prevención de riesgos de 2026 y cuánto tiene la línea 1? | Un mes desde el extracto en el BOJA; línea 1: 3.508.000 € (de 4.010.000 €). | BOJA-2026-113-3 |
| l06 | 84 KB | En las subvenciones de conciliación de la Junta, ¿qué se anticipa si la ayuda es de 6.000 € o menos? | El 100 %. | BOJA-2026-155-3 |
| l07 | 174 KB | En la FP para el empleo andaluza de 2026, ¿cuándo hay que pedir tres ofertas? | Cuando el gasto con un mismo proveedor supere los 15.000 €. | BOJA-2026-111-2 |
| l08 | 108 KB | ¿Cuánto recibe al mes una familia de cuatro en la Asistencia Material Básica FSE+ de Andalucía? | 190 €/mes (2.280 €/año). | BOJA-2026-124-3 |

## B. Con distractores (documentos casi iguales en el corpus)

Estas preguntas comprueban si cada variante distingue el documento correcto entre otros con la misma plantilla. En el corpus hay **35 convenios Aula Mentor** casi idénticos (ayuntamientos y mancomunidades), **5 convocatorias de Almassora** y **3 de Garafía del mismo día**.

| Q | Pregunta | Respuesta de referencia | Docs | Distractores |
|---|---|---|---|---|
| d01 | ¿Qué plazas ha convocado el Ayuntamiento de Garafía el 24 de agosto de 2026? | Cuatro, todas de personal laboral fijo por concurso-oposición libre: Palista y Técnico de Educación Infantil (una resolución), Director/a de Residencia de Mayores y Fisioterapeuta de Centros de Mayores (otra cada una). Plazo de veinte días hábiles. | BOE-A-2026-18650, BOE-A-2026-18651, BOE-A-2026-18652 | — (las tres son la respuesta) |
| d02 | ¿Cuántas plazas de Auxiliar Administrativo en turno libre convoca Almassora? | Cinco por oposición libre, más dos reservadas a personas con discapacidad. Bases en el BOP de Castellón n.º 61, rectificadas en el n.º 84; diez días hábiles de plazo. | BOE-A-2026-18470 | BOE-A-2026-14053 y BOE-A-2026-17029 (tres plazas de promoción interna), BOE-A-2026-12352 |
| d03 | ¿Qué plazo hay para presentarse a Asesor Jurídico en Almassora? | Diez días hábiles desde la publicación en el BOE. Es una plaza por oposición libre, escala de Administración Especial, subescala Técnica. | BOE-A-2026-17541 | Las otras cuatro de Almassora |
| d04 | ¿Qué plaza convocó Abanto y Ciérvana-Abanto Zierbena en agosto de 2026? | Una de Bibliotecario/a por concurso-oposición libre. Bases en el Boletín Oficial de Bizkaia n.º 150 (10/08/2026); veinte días hábiles. | BOE-A-2026-18104 | BOE-A-2026-12763 (Administrativos, junio) |
| d05 | ¿Dónde está el Aula Mentor de la Mancomunidad Alto Águeda y cuánto dura su convenio? | En la calle San Sebastián, 37-39, de Fuenteguinaldo (Salamanca). El convenio dura cuatro años desde que es eficaz, prorrogables otros cuatro. | BOE-A-2026-13634 | Los otros 34 convenios Aula Mentor (Huétor Vega, Bonete, Morella, Teba, Guadajoz…) |
| d06 | ¿Ha firmado la Mancomunidad Integral de la Comarca de Olivenza un convenio Aula Mentor? | Sí. El convenio con el Ministerio de Educación, Formación Profesional y Deportes para el programa Aula Mentor se publicó por resolución de 3 de julio de 2026. | BOE-A-2026-15005 | Los otros 34 convenios Aula Mentor |

## C. Mixtas (varios documentos de tamaños distintos)

| Q | Pregunta | Respuesta de referencia | Docs |
|---|---|---|---|
| x01 | ¿Qué plazas de Policía Local se han convocado en Sollana y Almassora? | Sollana: tres de Agente (dos por oposición libre y una por movilidad). Almassora: tres de Inspector (una libre y dos de promoción interna). | BOE-A-2026-18238, BOE-A-2026-12352 |
| x02 | ¿Qué ayudas para familias y autónomos con importe fijo hay en los boletines de este verano? | Autónomos de Ceuta: 5.000 € de pago único (RDL 22/2026). Asistencia Material Básica en Andalucía: 130–220 €/mes según el tamaño de la familia. | BOE-A-2026-18429, BOJA-2026-124-3 |
| x03 | ¿Qué convenios con entidades locales duran cuatro años prorrogables otros cuatro? | El de la CH del Cantábrico con Carreño (cauces) y el de Aula Mentor con la Mancomunidad Alto Águeda, entre otros convenios Aula Mentor con la misma cláusula. | BOE-A-2026-13005, BOE-A-2026-13634 |

> x02 y x03 son abiertas y el corpus tiene más documentos que podrían responderlas. Sirven para ver cómo se comporta el sistema, pero el recall será bajo por construcción. Recomiendo quedarse solo con x01 si se quiere una métrica limpia.

## D. Sin respuesta (trampas cerca de documentos reales)

Comprobadas con una búsqueda de texto sobre los 5.994 documentos.

| Q | Pregunta | Respuesta de referencia | Documento cercano |
|---|---|---|---|
| n01 | ¿Convoca Sollana plazas de Bombero? | No consta. Sollana solo ha convocado plazas de Agente de Policía Local. | BOE-A-2026-18238 |
| n02 | ¿Convoca Garafía plazas de Policía Local? | No consta. Garafía ha convocado Palista, Técnico de Educación Infantil, Director/a de Residencia de Mayores y Fisioterapeuta. | BOE-A-2026-18650/51/52 |
| n03 | ¿Cuánto aporta el Ayuntamiento de Carreño al programa Aula Mentor? | No consta: Carreño no tiene convenio Aula Mentor en el corpus. Su convenio publicado es con la CH del Cantábrico para cauces (80.000 €). | BOE-A-2026-13005 (mezcla dos documentos) |
| n04 | ¿Cuál es la dotación del Premio Comunicación de Protección de Datos de 2027? | No consta. Solo se ha convocado el de 2026 (3.000 €). | BOE-A-2026-15843 |

## Cosas que he visto y afectan al estudio

- **Plantillas repetidas:** casi todo el BOE de Administración Local son anuncios de ≤ 2 KB con la misma plantilla, y hay series como los 35 convenios Aula Mentor. Ahí las cuatro variantes trocean igual y la diferencia la marca el embedding del municipio. Por eso el bloque B es el más exigente para la recuperación.
- **HTML en el BOJA:** los BOJA nuevos siguen guardando la página web completa (`<!DOCTYPE html>`, Google Tag Manager, menú del portal), por ejemplo `BOJA-2026-109-1`. Eso infla su tamaño y mete ruido en los fragmentos. Si se limpia en el scraper, habría que reingerir los 338 BOJA.
- **Tamaño de la muestra:** con las 37 preguntas (24 por tamaño, 6 con distractores, 3 mixtas y 4 sin respuesta) la muestra sigue siendo pequeña para separar variantes con significación. Si hace falta, se puede duplicar el bloque A con otras 8 preguntas por tamaño.
