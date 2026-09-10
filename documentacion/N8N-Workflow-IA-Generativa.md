# Workflow de n8n: `ia-generativa-local`

Documenta el workflow de n8n que prueba, de punta a punta, las dos formas en que ApiKnowledge expone datos: **búsqueda semántica** (RAG sobre PostgreSQL + pgvector) y **consulta exacta** (SQL Server, vista `proyIA.viwConsultaVentas`). No es todavía un chat/agente — es la cadena de prueba manual que se usó para validar que cada pieza funciona antes de construir algo más grande encima.

El archivo exportable está en [`n8n/ia-generativa-local.workflow.json`](../n8n/ia-generativa-local.workflow.json) de este mismo repositorio.

## Qué hace el workflow (orden real de ejecución)

```
Start (trigger manual)
  └─▶ Login
        ├─▶ Generar Embedding (Indexar) ─▶ Indexar ─▶ Generar Embedding (Buscar) ─▶ Buscar
        └─▶ Consultar Ventas
```

1. **Start** — trigger manual (`n8n-nodes-base.manualTrigger`). Se ejecuta a mano, no hay schedule ni webhook todavía.
2. **Login** — `POST https://host.docker.internal:7225/api/auth/login` contra ApiKnowledge. Devuelve el JWT que usan todos los demás nodos vía `{{ $('Login').item.json.data[0].token }}`.
3. **Generar Embedding (Indexar)** — `POST` al endpoint de Gemini (`gemini-embedding-001:embedContent`, 1536 dimensiones) con un texto de prueba fijo ("Sección de ferretería..."). Usa la credencial `Gemini API Key` (query auth).
4. **Indexar** — `POST /api/v1/conocimiento/documentos` de ApiKnowledge, con el embedding recién generado. Guarda el documento vectorizado en Postgres/pgvector (módulo `Inventario`).
5. **Generar Embedding (Buscar)** — genera el embedding de una **pregunta en lenguaje natural**, deliberadamente sin palabras en común con el texto indexado ("¿cuántos productos tenemos registrados...?" vs. "martillos, destornilladores..."), para probar que la coincidencia es semántica y no por palabras clave.
6. **Buscar** — `POST /api/v1/conocimiento/buscar`, manda ese embedding y regresa el documento indexado como resultado más similar — prueba que la búsqueda semántica funciona de verdad.
7. **Consultar Ventas** *(agregado en esta sesión)* — `POST /api/v1/ventas/consultar`, reusa el mismo JWT de `Login`, pero **no pasa por Gemini ni por Postgres**: es una consulta exacta contra la vista `proyIA.viwConsultaVentas` de SQL Server. Body de prueba: `{ "topN": 10 }` (sin filtros, trae las 10 ventas más recientes).

El punto clave del diseño: **Conocimiento** (semántico) y **Ventas** (exacto) son dos ramas independientes que cuelgan del mismo `Login` — ninguna depende de la otra, y Ventas no necesita generar ningún embedding.

## Red: cómo n8n (en Docker) le llega a ApiKnowledge (en el host)

Todos los nodos que llaman a ApiKnowledge (`Login`, `Buscar`, `Indexar`, `Consultar Ventas`) usan la URL `https://host.docker.internal:7225/...`, no `https://localhost:7225/...`. Esto no es arbitrario — es la única forma en que funciona dada la arquitectura real:

- **n8n corre dentro de un contenedor Docker**; **ApiKnowledge corre directo en Windows** (fuera de Docker, con `dotnet run`). Son dos procesos en "redes" distintas.
- Dentro de un contenedor, `localhost` apunta **al propio contenedor**, no a la máquina donde vive Docker. Si el nodo `Login` usara `https://localhost:7225`, estaría intentando hablarle a un puerto 7225 dentro del contenedor de n8n (donde no hay nada escuchando), no al ApiKnowledge que corre en Windows.
- `host.docker.internal` es un nombre DNS especial que **Docker Desktop** resuelve automáticamente a la IP de la máquina host, visible desde dentro de cualquier contenedor. Por eso todas las URLs del workflow lo usan en vez de `localhost`.
- `options.allowUnauthorizedCerts: true` está presente en los cuatro nodos por una razón puntual: el perfil `https` de ApiKnowledge en desarrollo usa el certificado autofirmado de ASP.NET Core (`dotnet dev-certs`), que el contenedor de n8n no reconoce como válido. Sin esa opción, la llamada falla por validación de certificado TLS antes de llegar siquiera a la API.

**Requisito práctico**: ApiKnowledge tiene que estar corriendo con el perfil `https` (`dotnet run --launch-profile https` desde `ApiKnowledge/ApiKnowledge`, o el perfil `https` de Visual Studio) para que el puerto `7225` esté escuchando. Si no está corriendo, el nodo `Login` falla con `ECONNREFUSED` — es exactamente el error que salió la primera vez que se probó este workflow en esta sesión, antes de levantar la API.

## Cómo se construyó (pasos seguidos)

1. Se armó la cadena `Start → Login → Generar Embedding (Indexar) → Indexar → Generar Embedding (Buscar) → Buscar` para probar el flujo de indexado + búsqueda semántica end-to-end, con texto de indexado y de búsqueda sin overlap de palabras (prueba real de similitud semántica, no de coincidencia léxica).
2. Una vez que ApiKnowledge agregó el módulo Ventas (`ViewQueryExecutor` + `VentasController`, ver `ApiKnowledge-Guia-Implementacion.md`), se agregó el nodo **Consultar Ventas** vía el MCP de n8n (`update_workflow`, operaciones `addNode` + `addConnection`), colgado directamente de `Login` — sin tocar ningún nodo existente.

## Cómo importar este workflow en otra instancia de n8n

1. En n8n: **Workflows → Import from File** → selecciona `n8n/ia-generativa-local.workflow.json`.
2. **Credencial de Gemini**: el archivo exportado solo trae el *nombre* de la credencial (`Gemini API Key`), nunca la API key en sí — n8n nunca exporta secretos de credenciales. Hay que crear/enlazar esa credencial manualmente en la instancia destino (tipo *Query Auth*, con la API key real de Gemini).
3. **Usuario/contraseña de `Login`**: el nodo trae placeholders (`<usuario>`/`<contraseña>`) a propósito. Hay que reemplazarlos por un usuario válido de `test_cuenta_corriente_20250828` antes de ejecutar.
4. **Requisitos para que corra de verdad**:
   - ApiKnowledge corriendo localmente con el perfil `https` (puerto `7225`) — ver la sección "Red" más abajo para el porqué de `host.docker.internal` y `allowUnauthorizedCerts`.
   - PostgreSQL + pgvector arriba (`docker compose up -d` en `instalaciones/`, ver `POSTGRES_SETUP.md`).
   - SQL Server accesible con la `ConnectionString` real configurada en `appsettings.Development.json` de ApiKnowledge (no versionado, ver `ApiKnowledge-Guia-Implementacion.md`).

## Qué falta (no incluido en este workflow)

Este workflow es una cadena de prueba fija — no hay chat ni decisión automática de qué herramienta usar. El siguiente paso pendiente es reemplazar el trigger manual por un **Chat Trigger** y envolver `Buscar` y `Consultar Ventas` como **Tools** de un nodo **AI Agent**, para que un modelo de lenguaje decida solo cuál usar según la pregunta del usuario (semántica vs. dato exacto). No se ha construido todavía.
