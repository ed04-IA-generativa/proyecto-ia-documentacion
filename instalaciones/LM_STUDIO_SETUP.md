# Instalación de LM Studio (IA local, sin Docker)

LM Studio es la app que usó "Bionic" (nombre interno que le da un compañero a su instalación local de IA) — es una aplicación de escritorio que corre modelos de lenguaje **directamente en tu computadora**, sin depender de un servicio en la nube como OpenAI o Gemini. Descarga modelos en formato **GGUF** y los sirve como un servidor HTTP **compatible con el formato de la API de OpenAI** (los mismos endpoints `/v1/chat/completions`, `/v1/models`, etc.).

## ¿Necesita Docker?

**No.** A diferencia de n8n, LM Studio es una app de escritorio normal (como Chrome o VS Code) que se instala directo en Windows/Mac/Linux — no corre en un contenedor. Se relaciona con Docker de la misma forma que ApiKnowledge: **n8n (en Docker) le habla a LM Studio (en el host) usando `host.docker.internal`**, exactamente el mismo mecanismo que ya está documentado para ApiKnowledge en `documentacion/N8N-Workflow-IA-Generativa.md` (sección "Red").

## Instalación

**Opción rápida (con `winget`, Windows 10/11):**

```powershell
winget install --id ElementLabs.LMStudio
```

**Opción manual**: descargar el instalador desde [lmstudio.ai](https://lmstudio.ai/download) y correrlo como cualquier programa.

Cualquiera de las dos formas deja el CLI `lms` disponible en una terminal nueva (se agrega automáticamente al PATH del usuario) — no hace falta abrir la ventana de LM Studio para lo que sigue, aunque abrirla al menos una vez la primera vez ayuda a que todo quede inicializado.

## Descargar un modelo

Por CLI (recomendado, no requiere usar la interfaz gráfica):

```powershell
lms get "https://huggingface.co/ggml-org/Qwen2.5-VL-3B-Instruct-GGUF"
```

Ese es el modelo que usó "Bionic": **Qwen2.5-VL-3B-Instruct** — un modelo de 3 mil millones de parámetros, "VL" (Vision-Language, entiende texto e imágenes), cuantizado a formato GGUF para correr en hardware normal (~3.3 GB en disco).

⚠️ **Si van a usar el modelo como cerebro de un AI Agent (con herramientas/tools en n8n), no usen este modelo VL** — se probó y **no hace tool-calling de forma confiable** (nunca llega a ejecutar la herramienta, solo dice en texto que "la usaría"). El detalle completo de esa prueba está en `documentacion/N8N-Workflow-Copiloto-IA-Agent.md`. Para eso, descarguen también la versión de solo texto, mismo tamaño:

```powershell
lms get "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF"
```

**`Qwen2.5-3B-Instruct`** (sin la "VL", sin visión) — mismos 3B de parámetros, pero sí confirmado funcionando para tool-calling con el AI Agent de n8n. Resumen de cuándo usar cada uno:

| Modelo | Úsalo para |
|---|---|
| `qwen2.5-vl-3b-instruct` | Chat simple, o tareas que necesiten entender imágenes |
| `qwen2.5-3b-instruct` | AI Agent con herramientas (tool-calling) — el que usa el workflow "Copiloto IA Generativa" |

Verificar que quedaron descargados:

```powershell
lms ls
```

## Modelo de embeddings (viene incluido)

LM Studio trae integrado un modelo de embeddings, no hace falta descargarlo aparte: `text-embedding-nomic-embed-text-v1.5` (~84 MB). Sale listado junto al resto de modelos en `lms ls`, bajo la sección `EMBEDDING`.

⚠️ **Genera vectores de 768 dimensiones**, no 1536 como Gemini — no es compatible tal cual con la tabla de Postgres que usa el módulo de Conocimiento de ApiKnowledge. Ver el detalle de esto y el error real que produce en `documentacion/N8N-Workflow-LM-Studio.md`.

## Levantar el servidor y cargar los modelos

```powershell
lms server start
lms load "qwen2.5-3b-instruct"
lms load "text-embedding-nomic-embed-text-v1.5"
```

(Carga `qwen2.5-3b-instruct`, no el VL, si lo van a usar con el AI Agent — ver la tabla de arriba. Si solo necesitan chat simple, pueden cargar `qwen2.5-vl-3b-instruct` en su lugar.)

El primer comando levanta el servidor HTTP (por defecto en el puerto **1234**, aunque puede variar — revisar con `lms server status` o en la pestaña "Developer" de la app). Los siguientes cargan cada modelo en memoria; sin este paso el servidor está arriba pero no puede responder peticiones para ese modelo. Se pueden tener varios modelos cargados a la vez (uno de chat y uno de embeddings, por ejemplo).

⚠️ El servidor **no se queda corriendo solo** de forma indefinida — si la máquina se reinicia o el proceso se detiene, hay que volver a correr `lms server start` (y recargar los modelos) antes de usar el workflow de n8n.

## Probar que funciona

```powershell
curl http://localhost:1234/v1/models
```

Y una petición de chat real:

```powershell
curl http://localhost:1234/v1/chat/completions -H "Content-Type: application/json" -d "{\"model\": \"qwen2.5-3b-instruct\", \"messages\": [{\"role\": \"user\", \"content\": \"Responde solo con la palabra: funciona\"}], \"max_tokens\": 20}"
```

Si responde con un JSON tipo `chat.completion` y el texto generado, todo está funcionando.

Y para probar el modelo de embeddings:

```powershell
curl http://localhost:1234/v1/embeddings -H "Content-Type: application/json" -d "{\"model\": \"text-embedding-nomic-embed-text-v1.5\", \"input\": \"prueba\"}"
```

Debe responder con un arreglo de 768 números (`data[0].embedding`).

## Sobre la autenticación por API Key

Por defecto, el servidor de LM Studio **no pide ninguna API Key** — cualquiera que le llegue al puerto puede usarlo (por eso solo tiene sentido exponerlo dentro de tu propia red/máquina, nunca a internet). "Bionic" mencionó que la suya sí tenía autenticación por API Key — eso es una opción que se activa manualmente en la app: pestaña **Developer** → configuración del servidor → activar "Require API Key". Si la activan, hay que mandar el header `Authorization: Bearer <API_KEY>` en cada petición (mismo patrón que ya usamos con la credencial de Gemini en n8n).

## Puerto dinámico

Si el puerto 1234 ya está ocupado (por ejemplo, si tienen otra instancia corriendo), LM Studio puede asignar uno distinto. Antes de configurar la URL en n8n, confirmen el puerto real con:

```powershell
lms server status
```
