# proyecto-ia-documentacion

Documentación técnica del proyecto de IA: instalaciones, arquitectura de la API (`ApiKnowledge`), la investigación sobre sincronización SQL Server/PostgreSQL, y el código fuente relacionado.

## Resumen

Este repositorio centraliza todo lo versionado del proyecto que hasta ahora vivía disperso en carpetas locales sin control de versiones:

- **Código fuente** de `ApiKnowledge` — la API en .NET 8 que expone autenticación (SQL Server), consulta de ventas por vistas (SQL Server) y búsqueda semántica/RAG (PostgreSQL + pgvector).
- **Documentación técnica** de esa API: cómo está armada, sus modelos, su estándar de respuesta (`ApiResponseModel<T>`), etc.
- **Workflows de n8n**: pruebas con Gemini, con LM Studio (IA 100% local), y el **Copiloto con Chat + AI Agent** que decide solo qué herramienta usar — con toda la lista de problemas reales encontrados y cómo se resolvieron (léanla si algo similar les falla a ustedes).
- **Documentación de instalaciones**: cómo levantar el entorno local (Docker, PostgreSQL + pgvector, LM Studio, etc.).
- **Investigación abierta** sobre cómo se comunican en línea la base transaccional (SQL Server) y la base vectorial (Postgres) — todavía **sin decisión cerrada**.

**¿Algo te está fallando con n8n, LM Studio o el AI Agent?** Antes de investigar desde cero, revisa `documentacion/N8N-Workflow-Copiloto-IA-Agent.md` — tiene una lista de problemas reales ya encontrados (con el mensaje de error exacto de cada uno) y su solución.

## Estructura del repositorio

```
proyecto-ia-documentacion/
├── README.md
├── .gitignore
├── documentacion/
│   ├── ApiKnowledge-Guia-Implementacion.md       (arquitectura real de ApiKnowledge)
│   ├── SINCRONIZACION_SQLSERVER_POSTGRES.md      (investigación abierta, no una decisión cerrada)
│   ├── PROPUESTA-Sincronizacion-Ventas-Vectorial.md (propuesta concreta: watermark + diagrama, no implementada)
│   ├── N8N-Workflow-IA-Generativa.md             (workflow de prueba con Gemini: login, embeddings, busqueda semantica, ventas)
│   ├── N8N-Workflow-LM-Studio.md                 (misma prueba pero con LM Studio, IA 100% local — choque de dimensiones con Postgres)
│   └── N8N-Workflow-Copiloto-IA-Agent.md         (Chat + AI Agent que decide solo que herramienta usar — 7 problemas reales documentados con su solucion)
├── instalaciones/
│   ├── POSTGRES_SETUP.md                         (cómo levantar Postgres + pgvector en Docker)
│   ├── docker-compose.yml
│   └── N8N_SETUP.md                              (cómo levantar n8n en Docker)
├── n8n/
│   ├── ia-generativa-local.workflow.json
│   ├── lm-studio-local.workflow.json
│   ├── copiloto-ia-generativa.workflow.json
│   ├── tool-buscar-conocimiento.workflow.json
│   └── tool-consultar-ventas.workflow.json
└── ApiKnowledge/
    └── ApiKnowledge/                              (código fuente del proyecto .NET 8)
```

## Cómo correr `ApiKnowledge` localmente

El repositorio **no** trae credenciales reales. `appsettings.json` solo tiene placeholders (`<usuario>`, `<contraseña>`, etc.) para que quede claro qué va en cada campo — ver la sección "Configuración de las cadenas de conexión" en `documentacion/ApiKnowledge-Guia-Implementacion.md`.

Para desarrollo local:

1. Crea un `ApiKnowledge/ApiKnowledge/appsettings.Development.json` (no se versiona, está en `.gitignore`) con tus credenciales reales de SQL Server y Postgres, más la clave de firma JWT. ASP.NET Core lo combina automáticamente con `appsettings.json` en el entorno `Development` (ya configurado en `Properties/launchSettings.json`).
2. Levanta Postgres + pgvector siguiendo `instalaciones/POSTGRES_SETUP.md`.
3. `dotnet run` desde `ApiKnowledge/ApiKnowledge`.

**Nunca** se sube un `appsettings.Development.json` (ni ningún archivo con credenciales reales) a este repositorio, aunque sea privado.

## Convención de ramas

- **`main`** — protegida, es la versión vigente de cada documento/módulo.
- Ramas de trabajo por tema, que se integran a `main` vía Pull Request:
  - `docs/apiknowledge`
  - `docs/instalaciones`
  - `docs/sincronizacion-sqlserver-postgres` — vive en su propia rama mientras la investigación siga abierta, para que no aparezca en `main` como si ya fuera una decisión tomada.
  - `feature/apiknowledge-source` — código fuente de la API.

## Acceso

Repositorio **privado**. Los colaboradores agregados con rol **Read** pueden clonar y ver todo, pero no pueden hacer `push` ni abrir ramas — solo quien tenga rol `Write`/`Admin` puede modificar el contenido.
