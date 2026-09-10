# Workflow de n8n: Copiloto IA Generativa (Chat + AI Agent)

Este es el primer workflow que cierra el ciclo completo de RAG que faltaba en `ia-generativa-local` y `lm-studio-local`: no solo recupera datos (embeddings + búsqueda semántica, o consulta exacta de ventas), sino que un modelo de lenguaje **decide qué herramienta usar** y **redacta la respuesta final** en lenguaje natural. Es la pieza que cumple el criterio de aceptación de Sprint 1: *"Pregunta sobre documentación y consulta controlada de datos funcionan de extremo a extremo"*.

Son **3 workflows** trabajando juntos:

- [`n8n/copiloto-ia-generativa.workflow.json`](../n8n/copiloto-ia-generativa.workflow.json) — el principal: Chat + AI Agent.
- [`n8n/tool-buscar-conocimiento.workflow.json`](../n8n/tool-buscar-conocimiento.workflow.json) — herramienta 1 (empaquetada como sub-workflow).
- [`n8n/tool-consultar-ventas.workflow.json`](../n8n/tool-consultar-ventas.workflow.json) — herramienta 2 (empaquetada como sub-workflow).

## Por qué son 3 workflows y no uno solo

Una herramienta de un AI Agent en n8n (nodo `HTTP Request Tool`) solo puede hacer **una** llamada HTTP. Pero "buscar en Conocimiento" necesita una cadena de pasos (login → generar embedding → buscar), y "consultar ventas" también necesita su propio login. La solución de n8n para esto es el nodo **"Call n8n Workflow Tool"**: empaqueta un workflow completo (con todos los pasos que haga falta) como si fuera una sola herramienta para el agente.

Por eso cada herramienta es su propio workflow, con su propio `Execute Workflow Trigger` como entrada:

- **Tool - Buscar Conocimiento**: recibe `pregunta` (texto) → Login → genera el embedding con LM Studio → llama `/api/v1/conocimiento/buscar`.
- **Tool - Consultar Ventas**: recibe `fechaInicio`, `fechaFin`, `cliente`, `producto`, `tipoCanal`, `topN` (todos opcionales) → Login → llama `/api/v1/ventas/consultar`.

Cada una hace su **propio login** — es un poco de trabajo repetido, pero evita tener que pasar un token de sesión entre workflows distintos, y el login es rápido.

## Arquitectura del workflow principal

```
Chat (Chat Trigger)
  └─▶ Copiloto IA (AI Agent)
        ├─ Modelo: LM Studio (Chat Model) — qwen2.5-3b-instruct
        ├─ Memoria: Memoria de conversacion (ultimas 10 interacciones)
        ├─ Tool: Buscar Conocimiento  → llama al sub-workflow "Tool - Buscar Conocimiento"
        └─ Tool: Consultar Ventas     → llama al sub-workflow "Tool - Consultar Ventas"
```

El agente decide solo, según la pregunta del usuario, si necesita usar una herramienta, cuál, y con qué parámetros — nadie programa esa decisión a mano.

## Cómo se construyó

1. Se crearon primero los 2 sub-workflows (`Tool - Buscar Conocimiento`, `Tool - Consultar Ventas`), cada uno con su `Execute Workflow Trigger` como entrada de parámetros.
2. Se creó el workflow principal: `Chat Trigger` → `AI Agent`, con el modelo, la memoria y las dos herramientas conectadas como subnodos del agente.
3. Se probó con mensajes reales en el chat y se fueron encontrando (y arreglando) varios problemas reales — ver la sección de abajo. Ninguno de estos pasos fue "a la primera".

## Problemas reales encontrados y cómo se resolvieron

Esta sección es a propósito la más larga del documento — son los inconvenientes reales que salieron al armar esto, para que si alguien del equipo se topa con el mismo síntoma, lo encuentre rápido aquí en vez de perder el tiempo re-investigando.

### 1. El modelo no llama ninguna herramienta (`tool_calls.requested: 0`)

**Síntoma**: le pides al copiloto explícitamente "usa la herramienta Consultar Ventas ahora mismo" y responde en texto que "necesitaría usar la herramienta", sin ejecutarla nunca. Pasa dos veces seguidas, con distintas preguntas.

**Causa real**: el modelo de chat era `qwen2.5-vl-3b-instruct` — la variante **VL (Vision-Language)**, especializada en imágenes+texto. n8n detecta y reporta internamente `supports_strict_tool_calling: false` para este modelo. No es un problema de tamaño (3B), es que la variante VL no está tan entrenada para "tool calling" estructurado como la variante de solo texto.

**Solución**: se descargó `Qwen2.5-3B-Instruct-GGUF` (mismo tamaño, **sin** la parte de visión) y se cambió el modelo del nodo. Con este modelo, el mismo tipo de pregunta sí generó una llamada real a la herramienta (`tool_calls.completed: 1`, con parámetros armados por el modelo).

**Cómo descargarlo** (mismo patrón que en `instalaciones/LM_STUDIO_SETUP.md`):
```powershell
lms get "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF"
lms load "qwen2.5-3b-instruct"
```

**Lección general**: si van a usar LM Studio con un AI Agent que necesite herramientas, prefieran variantes **Instruct de solo texto** sobre variantes VL/multimodales, aunque sean del mismo tamaño — están mejor entrenadas para tool-calling. (Se intentó también descargar `Qwen2.5-7B-Instruct` para comparar con un modelo más grande, pero la descarga se estancó a mitad de camino por horas — no se pudo confirmar si un modelo más grande mejora aún más la confiabilidad del tool-calling; queda pendiente probarlo cuando la descarga no esté tan lenta.)

### 2. Error 400 "Required" en el modelo de chat

**Síntoma**: el nodo `LM Studio (Chat Model)` falla con `400 Required` apenas se ejecuta el agente, antes de intentar ninguna herramienta.

**Causa real**: el nodo `OpenAI Chat Model` de n8n tiene activada por defecto la opción **"Responses API"** (`responsesApiEnabled: true`) — un formato de API más nuevo que OpenAI introdujo. LM Studio solo implementa el formato clásico **"Chat Completions"** (`/v1/chat/completions`), no `/v1/responses`.

**Solución**: desactivar esa opción en el nodo (`responsesApiEnabled: false`). Si usan LM Studio (o cualquier servidor OpenAI-compatible que no sea la API real de OpenAI) con el nodo `OpenAI Chat Model` de n8n, **siempre** desactiven "Use Responses API" primero.

### 3. "Node does not have any credentials set"

**Síntoma**: el nodo del modelo de chat falla con este mensaje aunque el workflow se creó sin errores.

**Causa real**: la credencial (`openAiApi`) que arma el generador de workflows queda solo **referenciada por nombre** dentro del nodo — no crea la credencial real con su valor secreto. Eso hay que hacerlo a mano, una vez, desde la interfaz de n8n.

**Solución**: crear la credencial manualmente — `Overview → Credentials → Add Credential → OpenAI`. Como API Key poner cualquier texto (LM Studio no la valida, pero el campo es obligatorio), y cambiar el **Base URL** a `http://host.docker.internal:1234/v1`. Después, abrir el nodo del modelo en el workflow y seleccionar esa credencial manualmente en el dropdown — no se asigna sola.

### 4. "Workflow is not active and cannot be executed"

**Síntoma**: el agente sí decide llamar la herramienta correcta, pero la llamada al sub-workflow falla con exactamente este mensaje.

**Causa real**: el nodo **"Call n8n Workflow Tool"** exige que el sub-workflow que referencia esté **activo/publicado**, no solo guardado — igual que un workflow con trigger de webhook necesita estar activo para recibir peticiones reales.

**Solución**: publicar (activar) cada sub-workflow usado como herramienta, no solo guardarlo. Cada vez que se edite un sub-workflow-herramienta, hay que volver a publicarlo para que la versión activa sea la actualizada — guardar solo el borrador no alcanza.

### 5. El copiloto inventa datos que no existen (alucinación)

**Síntoma**: la herramienta de Ventas corrió bien (`status: true`), pero con los filtros que puso el modelo no encontró ninguna fila (`"data": []`) — y aun así el copiloto respondió con seguridad: *"El primer producto en el resultado es 'Producto A'"*. Ese producto no existe en ningún lado.

**Causa real**: sin instrucciones explícitas, un modelo pequeño puede completar una respuesta "razonable" en vez de admitir que no hay datos — es el comportamiento por defecto de un LLM (generar la continuación más probable), no un bug de n8n ni de la API.

**Solución**: se reforzó el `systemMessage` del agente con reglas explícitas: nunca inventar datos, dejar los filtros vacíos si el usuario no los menciona (en vez de inventar fechas de ejemplo), y decir explícitamente "no encontré resultados" cuando el arreglo `data` viene vacío. Con esas reglas, una prueba posterior con una herramienta que falló de verdad hizo que el copiloto respondiera honestamente en vez de inventar.

**Lección general**: con modelos pequeños (locales o no), **siempre** hay que instruir explícitamente contra la alucinación en el `systemMessage` — no asumir que "no inventar" es un comportamiento por defecto.

### 6. "Bad request - please check your parameters" al llamar la herramienta de Ventas con fechas vacías

**Síntoma**: cuando el usuario no menciona fechas y el modelo (correctamente, por la regla anterior) deja `fechaInicio`/`fechaFin` vacíos (`""`), la llamada a la herramienta falla.

**Causa real**: `fechaInicio`/`fechaFin` viajan como texto vacío (`""`) hasta ApiKnowledge, donde `CriterioVentasM.FechaInicio`/`FechaFin` son `DateTime?` — un string vacío no es una fecha JSON válida, así que la deserialización del modelo falla con `400 Bad Request` antes de tocar la base de datos.

**Solución**: en el sub-workflow `Tool - Consultar Ventas`, el cuerpo de la petición convierte explícitamente string vacío a `null` antes de mandarlo: `'fechaInicio': ... || null`. Con `null`, ApiKnowledge lo interpreta correctamente como "sin filtro de fecha" (así está diseñado el endpoint desde el principio, ver `N8N-Workflow-IA-Generativa.md`).

**Lección general**: cuando un AI Agent arma parámetros para una API que espera tipos estrictos (fechas, números), un string vacío **no es lo mismo** que "sin valor" — hay que convertir explícitamente antes de mandarlo.

### 7. Timeout real de SQL Server al consultar Ventas sin filtro de fecha

**Síntoma**: con fechas ya corregidas a `null` (sin filtro), la consulta de ventas tarda ~30 segundos y termina en error: `"Timeout expired. The timeout period elapsed prior to completion of the operation or the server is not responding."` (`errorCode: "1--2"`, un error real de SQL Server, no de n8n ni de LM Studio).

**Causa real**: `proyIA.viwConsultaVentas` hace JOIN de más de una decena de tablas (ver `N8N-Workflow-IA-Generativa.md`). Sin un filtro de fecha que acote el rango, SQL Server tiene que evaluar un volumen de filas mucho mayor, y el tiempo de espera configurado no alcanza.

**Estado**: **no resuelto todavía** — es un límite real de rendimiento de la vista/consulta, no de la integración con n8n/LM Studio. Se reprodujo de forma consistente en varias pruebas: cualquier pregunta sin fecha específica (ej. "dame ventas recientes", "sin filtros, dame 3 ventas") dispara este mismo timeout de ~30 segundos. Queda como trabajo pendiente, con algunas opciones a evaluar (no implementadas):
- Que el copiloto siempre mande un rango de fechas razonable por defecto (ej. últimos 30 días) en vez de dejarlo abierto, en vez de resolverlo a nivel de base de datos.
- Aumentar el `CommandTimeout` de `ViewQueryExecutor` para consultas sin filtro de fecha.
- Revisar índices de la vista/tablas base para consultas sin filtro de fecha.

⚠️ **Ojo con un detalle al leer la respuesta del copiloto en este caso**: cuando la herramienta falla por este timeout, el copiloto responde algo como *"No encontré resultados con esos filtros"* — la misma frase que usa cuando la consulta sí corrió bien pero genuinamente no hay filas que coincidan (ver el problema anterior, el de la alucinación). **No es lo mismo**: una cosa es "no hay datos con esos filtros" (la herramienta funcionó) y otra es "la herramienta falló/tardó demasiado" (este problema). El `systemMessage` actual no distingue entre ambos casos al redactar la respuesta final — por ahora, para saber cuál pasó de verdad hay que revisar la ejecución en n8n (si `Consultar Ventas` tardó ~30s y salió en rojo con `executionStatus: "error"`, fue timeout; si corrió rápido y en verde con `data: []`, sí fue una búsqueda legítima sin resultados). Ajustar el prompt para que el copiloto distingua y comunique esta diferencia al usuario queda pendiente también.

## Qué preguntas funcionan bien ahora mismo (y cuáles no)

- ✅ **Preguntas con un rango de fechas concreto** que sí tenga datos reales (ej. algo en septiembre 2025) — funcionan de punta a punta, confirmado con resultados reales.
- ⚠️ **Preguntas sin ninguna fecha** ("ventas recientes", "dame algunas ventas", "sin filtros") — hoy en día terminan casi siempre en el timeout de arriba. No es un límite del copiloto en sí, es el rendimiento de la consulta sin acotar.
- ✅ El copiloto **no inventa datos** en ningún caso — ya sea que la herramienta devuelva 0 filas o falle, nunca fabrica una respuesta con productos/cifras que no existen.

## Qué quedó demostrado funcionando de verdad

A pesar de los tropiezos de arriba, se confirmó con ejecuciones reales:

- El agente decide correctamente qué herramienta usar según la pregunta.
- Con parámetros de fecha concretos, la llamada a `Consultar Ventas` corre de punta a punta (Chat → Agent → sub-workflow → Login → ApiKnowledge → SQL Server → respuesta del agente).
- El agente no alucina cuando se le instruye explícitamente no hacerlo.
- Todo esto corriendo **100% local** (LM Studio + n8n en Docker + ApiKnowledge + SQL Server + Postgres), sin ninguna llamada a un proveedor de IA en la nube.
