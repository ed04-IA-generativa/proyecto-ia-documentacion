# Workflow de n8n: `lm-studio-local`

Réplica del workflow `ia-generativa-local` (ver `N8N-Workflow-IA-Generativa.md`), pero generando los embeddings con **LM Studio, corriendo 100% local**, en vez de con Gemini. Mismo consumo de las APIs de ApiKnowledge (`Login`, `Indexar`, `Buscar`, `Consultar Ventas`) — el único cambio real es de dónde sale el vector de embedding.

El archivo exportable está en [`n8n/lm-studio-local.workflow.json`](../n8n/lm-studio-local.workflow.json).

Antes de correr esto, hay que tener LM Studio instalado, el modelo de chat y el de embeddings cargados — ver `instalaciones/LM_STUDIO_SETUP.md`.

## Qué hace el workflow

```
Start (trigger manual)
  └─▶ Login
        ├─▶ Generar Embedding LM Studio (Indexar) ─▶ Indexar ─▶ Generar Embedding LM Studio (Buscar) ─▶ Buscar
        └─▶ Consultar Ventas
```

Idéntico al de Gemini, nodo por nodo, salvo que **"Generar Embedding (Indexar/Buscar)"** ahora llama a `POST http://host.docker.internal:1234/v1/embeddings` (LM Studio, modelo `text-embedding-nomic-embed-text-v1.5`) en vez de a la API de Gemini.

## ⚠️ Resultado real de la prueba: falla en `Indexar`, y por qué

Se corrió el workflow completo contra los servicios reales (ApiKnowledge, Postgres, LM Studio, todo corriendo localmente). Resultado:

- **Login**: ✅ exitoso, token generado normalmente.
- **Generar Embedding LM Studio (Indexar)**: ✅ exitoso — LM Studio devolvió un vector real.
- **Indexar**: ❌ **falló con error 400**:
  ```json
  {
    "status": false,
    "message": "Error en la base de datos.",
    "error": "22000: expected 1536 dimensions, not 768",
    "errorCode": "1"
  }
  ```
- **Consultar Ventas**: no llegó a ejecutarse — n8n detiene **toda** la ejecución cuando cualquier nodo falla (comportamiento por defecto), aunque esa rama no dependía del nodo que falló.

### La causa

El modelo de embeddings que trae LM Studio integrado (`text-embedding-nomic-embed-text-v1.5`) genera vectores de **768 dimensiones**. La tabla de Postgres (`conocimiento_documento`) tiene la columna `embedding` declarada como `VECTOR(1536)` — la dimensión exacta que produce Gemini (`gemini-embedding-001`). Postgres/pgvector exige que la dimensión coincida exactamente con la declarada en la columna; si no coincide, rechaza el insert. **No es un error de configuración de n8n ni de ApiKnowledge — es una incompatibilidad real entre los dos modelos de embeddings.**

### Qué se necesitaría para que funcione de verdad

**Si es solo para pruebas de embeddings con LM Studio** (como este workflow), no hace falta cambiar nada — basta con tener en cuenta el choque de dimensiones y esperar que `Indexar`/`Buscar` fallen con el error 400 de arriba. Es el comportamiento esperado, no un bug.

**Si se quiere usar LM Studio de verdad** (no solo probar) como proveedor de embeddings, sí hay que cambiar la base: la columna `embedding` de `conocimiento_documento` pasa de `VECTOR(1536)` a `VECTOR(768)`. Una columna `vector` de pgvector solo acepta una dimensión fija para todas las filas — **si ya hay documentos indexados con Gemini (1536 dims), ese cambio los rompe**, porque no se pueden mezclar vectores de distinta dimensión en la misma columna; hay que re-indexarlos con el nuevo modelo, o mantener tablas separadas (una por proveedor).

**Estado actual (a la fecha de este documento)**: la columna **ya se cambió a `VECTOR(768)`** para probar esto de verdad — se confirmó que el flujo completo (`Login → Indexar → Buscar`) funciona igual de bien con LM Studio que con Gemini, similitud semántica real incluida. Como consecuencia, **el workflow de Gemini (`ia-generativa-local`) no va a funcionar mientras la columna siga en 768** (mismo error, al revés: esperaría 1536 y Gemini se lo daría). Si van a seguir con Gemini como proveedor principal, hay que volver a poner la columna en `VECTOR(1536)` (y vaciar la tabla primero, por la misma razón de arriba) — todavía no se ha decidido/revertido.

Otras alternativas que no requieren tocar Postgres, ninguna implementada todavía:

1. **Buscar un modelo de embeddings local que sí produzca 1536 dimensiones** (compatible con la tabla actual, sin cambiar nada de Postgres).
2. Usar LM Studio **solo para el modelo de chat/razonamiento** (ej. como LLM de un AI Agent en n8n), dejando a Gemini como el único proveedor de embeddings — son roles distintos y no chocan entre sí.

### Comparación de proveedores de embeddings

Importante no confundir **LM Studio** con **"OpenAI API"** — aunque LM Studio imita el formato de la API de OpenAI (mismos endpoints `/v1/...`), nunca le habla a los servidores reales de OpenAI: corre modelos abiertos (como Qwen) **100% local**. "OpenAI API" es llamar de verdad a los servidores de OpenAI en la nube — dos cosas completamente distintas, aunque el plan de negocio (`Business AI Plan Sprints DS - PO.xlsx`, hoja "Herramientas") mencione "OpenAI API" como herramienta LLM recomendada para el POC, con costo marcado como "Variable" (esa hoja es un plan inicial genérico — ya se desvió en varias decisiones reales del proyecto, como usar n8n en vez de Semantic Kernel).

| Proveedor | Costo | Dimensión del embedding | ¿Compatible con Postgres hoy? |
|---|---|---|---|
| **Gemini** (el que se usa actualmente) | Gratis (con límite, ver `N8N-Workflow-IA-Generativa.md`) | 1536 | ✅ Sí |
| **OpenAI real** (`text-embedding-3-small`) | De pago, sin capa gratuita permanente | 1536 (nativo) | ✅ Sí, pero tiene costo |
| **LM Studio** (local, `text-embedding-nomic-embed-text-v1.5`) | Gratis (usa tu hardware) | 768 | ❌ No, sin cambiar el esquema de Postgres |

**Recomendación**: para avanzar con las pruebas, se recomienda seguir con **Gemini** — ya funciona, es gratis y coincide con la dimensión configurada, sin nada que resolver. Eso no significa que usar **LM Studio esté mal** — es una opción igual de válida (y sin costo de API, corre en tu propia máquina), solo que hoy requiere resolver primero el choque de dimensiones antes de poder indexar/buscar con él de verdad. Tampoco es cuestión de "usar OpenAI en su lugar" — son ejes de decisión distintos (LM Studio vs. OpenAI real, y por separado, qué proveedor de embeddings usar).

## Cómo se construyó

1. Se instaló LM Studio y se descargaron dos modelos: `Qwen2.5-VL-3B-Instruct-GGUF` (chat) y el de embeddings que trae integrado, `text-embedding-nomic-embed-text-v1.5` (ver `instalaciones/LM_STUDIO_SETUP.md`).
2. Se probó primero un workflow simple de un solo nodo (`Consultar LM Studio` → chat), para confirmar conectividad n8n ↔ LM Studio vía `host.docker.internal:1234` — funcionó, con una respuesta coherente del modelo.
3. Se reemplazó ese nodo único por la réplica completa del flujo de `ia-generativa-local`: mismo `Login`, mismo `Indexar`, mismo `Buscar`, mismo `Consultar Ventas` — solo los dos nodos de embeddings se apuntaron a LM Studio en vez de a Gemini (cambiando también cómo se lee el vector de la respuesta: LM Studio devuelve `data[0].embedding`, Gemini devuelve `embedding.values` — formatos de respuesta distintos aunque ambos son "compatibles con OpenAI" para el chat).
4. Se ejecutó el workflow completo y se confirmó el error real de dimensión descrito arriba.

## Notas

- El usuario de prueba (`Login`) usado aquí es el mismo `deved04` que se comparte con el equipo (ver el link de un solo uso) — confirmado funcionando.
- **Actualización**: después de este workflow se cambió la columna de Postgres a `VECTOR(768)` para probar el flujo completo de verdad, y se armó un **AI Agent con Chat** que sí usa LM Studio de punta a punta (modelo de chat + embeddings + herramientas) — ver `N8N-Workflow-Copiloto-IA-Agent.md`, que también documenta varios problemas reales encontrados al armar eso (tool-calling, credenciales, activación de sub-workflows, alucinaciones, etc.) — es el documento más completo de "cosas que salieron mal y cómo se arreglaron" de todo el repo.
