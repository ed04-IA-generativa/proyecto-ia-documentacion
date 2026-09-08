# Sincronización de datos: SQL Server (transaccional) ↔ Postgres (vectorial)

## Contexto

Hay una duda abierta sobre cómo se comunican en línea la base de datos transaccional
(SQL Server) y la base de datos vectorial (Postgres + pgvector): si se sincronizan en tiempo
real, si Postgres también maneja transacciones, y con qué mecanismo técnico.

**Esto es una sugerencia y una investigación realizada al respecto, no una decisión cerrada.**
La idea central es una solución **a nivel de API**, no a nivel de base de datos: en vez de
buscar cómo sincronizar o replicar datos para que Postgres termine teniendo tanto la
información transaccional como la semántica, se evita ese problema por completo separando el
acceso por API — cada base de datos guarda únicamente el tipo de dato que le corresponde, y
nunca hace falta que Postgres conozca ni almacene datos transaccionales.

**Nota sobre nombres**: en este documento, **`ApiAsp`** es un nombre provisional para "el API
ASP.NET que expondría los datos transaccionales del ERP con sus reglas de negocio" — todavía
no sabemos su nombre real, su estructura, ni si maneja JWT u otro esquema de autenticación. No
se debe asumir ninguna estructura concreta a partir de este nombre. Cuando se confirme cuál es
el API real a integrar, hay que reemplazar `ApiAsp` por su nombre verdadero en este documento y
en el código.

## Principio de diseño: las dos bases nunca se conectan directamente

**No hay conexión directa entre SQL Server y Postgres** — nada de cadena de conexión cruzada,
linked server, ni replicación nativa entre las dos. Cada base vive detrás de su propia API, y
esa es la única puerta de entrada:

- **SQL Server** ↔ **ApiAsp** (nombre provisional, ver nota arriba) — dueño de los datos
  transaccionales, aplicaría las reglas de negocio reales del ERP.
- **Postgres + pgvector** ↔ **ApiKnowledge** — dueño del conocimiento semántico (embeddings,
  búsqueda por significado).

n8n sería el componente que le habla a ambas APIs. Las bases de datos nunca se hablan entre
sí. Esto es intencional, no un descuido: evita duplicar reglas de negocio, evita que Postgres
necesite conocer el esquema real del ERP, y respeta el aislamiento por tenant que ya tiene
SQL Server (cada cliente con su propia base física).

## Dos opciones para el API transaccional (según se quiera reusar el existente o no)

Sobre quién expone el lado transaccional (`ApiAsp` en este documento), hay dos caminos
posibles:

1. **Reusar algún API ASP.NET que ya exista para exponer estos datos transaccionales**, si lo
   hay y es adecuado para este propósito. Ventaja: no se construye nada nuevo. Riesgo: no se
   debe asumir su estructura, autenticación, ni si expone justo lo que un agente de IA
   necesitaría consultar de forma segura y acotada, sin verificarlo primero.
2. **Crear un API nueva, propia para este propósito, con vistas de solo lectura** sobre la base
   transaccional. Esto aplicaría en caso de que no se quiera depender de un API existente (por
   ejemplo, si su alcance no es el adecuado para exponérselo a un agente de IA, o si se
   prefiere no acoplar ese consumo con otros ya existentes). Quedarían entonces dos APIs
   separadas y con responsabilidades claras: una para lo semántico (ApiKnowledge, ya
   construida) y otra para lo transaccional (nueva, solo vistas/lectura).

Cuál de las dos conviene es, en sí mismo, otra decisión pendiente — no forma parte de este
documento resolverla, solo dejar planteadas las opciones.

## Es irrelevante para esta investigación qué orquestador se use

Sea que el orquestador termine siendo n8n, ChatGPT (o su API) u otro agente, **no cambia nada
de lo planteado aquí**: cualquiera de ellos tendría que consumir estas mismas APIs por HTTP de
la misma forma. La elección de orquestador es un tema aparte, de otra investigación — no
condiciona ni depende de cómo se resuelva la sincronización/separación entre lo transaccional y
lo semántico.

## Postgres nunca maneja transacciones

Postgres/pgvector es **exclusivamente de lectura para búsqueda semántica**. No escribe de
vuelta hacia el ERP, no participa en ventas, cobros, ni ninguna operación transaccional real.
Todas las escrituras de negocio seguirían pasando únicamente por ApiAsp/SQL Server. Esto es
consistente con el alcance de solo lectura definido para el MVP.

## ¿Qué es "indexar"?

**Indexar** es el proceso de tomar un pedazo de información (un texto), convertirlo en un
vector mediante un modelo de embeddings, y guardar ese vector (junto con el texto original) en
la base de datos vectorial, para que después se pueda encontrar por significado en una
búsqueda semántica.

Es un proceso de tres pasos, siempre en el mismo orden:

1. **Obtener el contenido** — el texto real que se quiere hacer buscable (por ejemplo, la
   descripción de un documento, una política, o cualquier información relevante).
2. **Generar el embedding** — un modelo de embeddings convierte ese texto en un vector (una
   lista de números que representa su significado).
3. **Guardar el resultado** — el texto y su vector quedan almacenados juntos en la base de
   datos vectorial, listos para que una búsqueda semántica posterior los pueda encontrar.

Indexar es distinto de "buscar": indexar es lo que se hace una vez (o cada vez que cambia el
dato) para dejar la información disponible; buscar es lo que se hace cada vez que alguien
pregunta algo, comparando su pregunta (ya convertida en vector) contra todo lo que ya se
indexó.

## Cómo llegarían los datos de SQL Server a Postgres

No sería una conexión en vivo — sería un proceso de indexado que copia y transforma la
información:

1. n8n lee datos desde **ApiAsp** (nunca directo de SQL Server, para no duplicar sus reglas de
   negocio ni sus validaciones).
2. Convierte ese contenido en texto legible (el "documento" a indexar).
3. Calcula el embedding de ese texto (vector).
4. Llama a `POST /api/v1/conocimiento/documentos` en ApiKnowledge, que lo guarda en Postgres.

El resultado es que Postgres terminaría con una **copia semántica** de una parte de los datos
de SQL Server — no una conexión viva a ellos. Esa copia se queda desactualizada hasta que se
vuelva a correr el indexado de ese dato.

## Separación de responsabilidades en las consultas (importante para evitar errores)

Las preguntas del usuario final se resolverían por dos caminos distintos, que no deben
mezclarse:

| Tipo de pregunta | Ejemplo | Camino correcto |
|---|---|---|
| Dato exacto / estructurado | "¿Cuánto debe el cliente X?" | AI Tool → llamada en vivo a ApiAsp → SQL Server |
| Conocimiento / contexto | "¿Qué política tenemos para descuentos?" | Búsqueda semántica → ApiKnowledge → Postgres/pgvector |

Los montos y cifras exactas **nunca** deben resolverse vía búsqueda semántica. Si un monto se
respondiera desde un documento indexado en Postgres, existe riesgo real de que esté
desactualizado o de que el LLM "redondee"/invente un valor parecido en vez de dar el dato real.

## Lo que sigue pendiente de decidir

La frecuencia y el mecanismo de sincronización todavía no están definidos. Dos opciones,
de más simple a más completa:

1. **Sincronización por lotes (batch)** — un flujo de n8n corre periódicamente (por ejemplo
   cada hora o cada noche) y re-indexa lo que cambió desde la última corrida. Simple de
   construir; el conocimiento puede quedar desactualizado durante ese intervalo.
2. **Basada en eventos (casi en tiempo real)** — cuando un dato cambia en SQL Server, algo se
   entera de inmediato (un mecanismo de change-tracking, o que ApiAsp publique un evento al
   procesar el cambio) y dispara la re-indexación de ese registro puntual. Mantiene el
   conocimiento más al día, pero requiere que ApiAsp sepa avisar cuándo cambian sus datos — no
   sabemos todavía si eso es posible con el API real.

**Recomendación**: empezar con sincronización por lotes. El primer contenido a indexar según
el plan es documentación del sistema, que no cambia con frecuencia, así que no hay urgencia de
resolver el mecanismo en tiempo real todavía. Migrar a un esquema basado en eventos cuando se
indexen datos que sí cambien seguido (por ejemplo Finanzas o Inventario).

## Preguntas para decidir en equipo

- Primero: ¿se va a **reusar un API existente** (opción 1) o **crear uno nuevo** con vistas de
  solo lectura (opción 2)? Solo si se elige la opción 1 importa investigar cuál es ese API, qué
  estructura tiene y qué autenticación maneja — si se elige la opción 2, esa investigación no
  hace falta, porque el API nuevo se construye a la medida de lo que se necesite.
- ¿Cada cuánto se considera "aceptable" que el conocimiento esté desactualizado, según el tipo
  de dato indexado?
- ¿Quién dispara la re-indexación — un flujo programado en n8n, o un endpoint que ese API
  llama cuando detecta un cambio relevante?
- ¿Se necesita un mecanismo para saber qué documentos ya están indexados y evitar volver a
  procesar todo desde cero en cada corrida?
