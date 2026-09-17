# Probar ApiKnowledge con Postman

Esta guía deja Postman listo para llamar los endpoints de `ApiKnowledge` directo — sin
pasar por n8n ni escribir código — útil para confirmar que un endpoint funciona antes de
conectarlo a un workflow, o para diagnosticar un error real contra la API en aislado.

## Requisitos

- Postman instalado.
- `ApiKnowledge` corriendo y accesible: en modo desarrollo (`dotnet run --launch-profile
  https`) o publicada en IIS (ver `instalaciones/IIS_SETUP.md`).
- Un usuario válido de SQL Server para hacer login (ej. `deved04` / `2026`, ver la sección
  de usuarios de prueba en `documentacion/ApiKnowledge-Guia-Implementacion.md`).

## 1. Instalar Postman

```powershell
winget install Postman.Postman
```

O descargarlo desde su sitio oficial. Crear una cuenta es opcional — sirve para
sincronizar/compartir colecciones con el equipo desde la nube de Postman, pero no hace
falta para lo de esta guía; se puede usar sin iniciar sesión ("Skip and go to the app" /
modo *Lightweight*).

## 2. Dos conceptos antes de empezar: Collection y Environment

- **Collection** — un grupo de peticiones guardadas (un request por cada endpoint de la
  API), para no reescribir cada URL/body desde cero cada vez.
- **Environment** — un conjunto de variables (ej. `baseUrl`, `token`) que cambian según
  dónde esté corriendo la API en ese momento, sin tener que editar cada request a mano.
  Se referencian en cualquier campo con `{{nombreVariable}}`.

Con ambos, cambiar de "estoy probando contra mi `dotnet run` local" a "estoy probando
contra la IIS publicada" es solo cambiar el valor de **una** variable, no editar cada
request.

## 3. Crear el Environment

**Environments** (panel izquierdo) → **+** → nombre `ApiKnowledge - Local`. Agregar estas
variables (columna *Initial value* y *Current value* iguales):

| Variable  | Valor inicial                | Para qué sirve |
|---|---|---|
| `baseUrl` | `https://localhost:7225`     | La raíz de la API. Ver la nota de abajo según cómo esté corriendo. |
| `token`   | *(vacío)*                    | Se llena solo después del login (paso 6) — no escribir nada aquí a mano. |

Guardar, y seleccionar `ApiKnowledge - Local` en el desplegable de Environments arriba a
la derecha de Postman (si queda en "No Environment", ninguna variable se resuelve y todos
los requests van a fallar).

**Qué poner en `baseUrl` según cómo esté corriendo la API:**

- Modo desarrollo (`dotnet run --launch-profile https`, en tu propia máquina):
  `https://localhost:7225`
- Publicada en IIS, en la misma máquina donde corre Postman:
  `http://localhost/host/<NombreCliente>/ED04/Apis`
- Publicada en IIS, en otra máquina de la red (ver `instalaciones/IIS_SETUP.md`):
  `http://<IP-de-esa-máquina>/host/<NombreCliente>/ED04/Apis`

## 4. Desactivar la verificación de certificado SSL (solo para el modo `dotnet run` con HTTPS)

El perfil `https` de desarrollo usa el certificado autofirmado de ASP.NET Core (`dotnet
dev-certs`) — el mismo motivo por el que n8n necesita `allowUnauthorizedCerts: true` (ver
`documentacion/N8N-Workflow-IA-Generativa.md`). Postman, por defecto, rechaza ese
certificado con un error de tipo *"Error: self signed certificate"*.

Ícono de engranaje (arriba a la derecha) → **Settings** → pestaña **General** → apagar
**SSL certificate verification**.

Esto **no hace falta** si `baseUrl` apunta a la IIS publicada en HTTP (paso 3, segunda o
tercera opción) — ahí no hay ningún certificado de por medio.

## 5. Crear la Collection

**Collections** (panel izquierdo) → **+** → nombre `ApiKnowledge`. Los requests de los
siguientes pasos se guardan dentro de esta collection (botón **Save** al crear cada uno).

## 6. Request: Login (obtener el JWT)

Este es el primer request que hay que correr siempre — todos los demás (excepto Status)
necesitan el token que este devuelve.

- **Método**: `POST`
- **URL**: `{{baseUrl}}/api/auth/login`
- **Body** → pestaña **raw**, tipo **JSON** (desplegable al lado de "raw", no dejarlo en
  "Text"):

```json
{
  "pOpcion": 1,
  "pUserName": "deved04",
  "pPass": "2026"
}
```

**Para no copiar el token a mano cada vez**: pestaña **Scripts** del request → sección
**Post-response** (en versiones de Postman más viejas aparece como pestaña **Tests**) →
pegar:

```javascript
const respuesta = pm.response.json();
if (respuesta.data && respuesta.data[0] && respuesta.data[0].token) {
    pm.environment.set("token", respuesta.data[0].token);
    console.log("Token guardado en la variable de entorno 'token'.");
} else {
    console.log("Login no devolvió token, revisar la respuesta:", respuesta);
}
```

Esto hace exactamente lo mismo que hacen los workflows de n8n con
`$('Login').item.json.data[0].token` — guarda el token en la variable `token` del
Environment automáticamente cada vez que se manda este request, para que los demás
requests lo usen sin intervención manual.

**Enviar (Send)** y confirmar que la respuesta trae `"status": true` y un `token` no
vacío dentro de `data[0]`.

## 7. Requests protegidos (necesitan el JWT del paso 6)

En cada uno: pestaña **Authorization** del request → **Type**: `Bearer Token` → **Token**:
`{{token}}` (o, a mano, un header `Authorization` con valor `Bearer {{token}}` en la
pestaña **Headers** — es equivalente).

### Consultar Ventas

- `POST {{baseUrl}}/api/v1/ventas/consultar`
- Body raw/JSON:

```json
{
  "fechaInicio": null,
  "fechaFin": null,
  "cliente": null,
  "producto": null,
  "tipoCanal": null,
  "topN": 10
}
```

⚠️ Enviar `null` explícito (como en el ejemplo) en los filtros que no se quieran usar —
**nunca** `""` (string vacío). Con `tipoCanal: ""` la consulta responde `0` filas de
forma silenciosa en vez de ignorarse el filtro — es un bug real que ya se dio con el
Copiloto, documentado en el problema #9 de
`documentacion/N8N-Workflow-Copiloto-IA-Agent.md`.

### Buscar Conocimiento (búsqueda semántica)

- `POST {{baseUrl}}/api/v1/conocimiento/buscar`
- Body raw/JSON:

```json
{
  "embedding": [0.01, 0.02, 0.03],
  "modulo": "Inventario",
  "topN": 3
}
```

`embedding` tiene que ser un vector real generado por un modelo de embeddings (Gemini o
LM Studio, ver `documentacion/N8N-Workflow-LM-Studio.md`) para que la búsqueda encuentre
algo con sentido — Postman no genera embeddings por sí solo. Un arreglo corto cualquiera
(como el del ejemplo) sirve solo para confirmar que el endpoint responde sin error, no
para validar resultados de similitud reales.

### Guardar Documento (indexar en Conocimiento)

- `POST {{baseUrl}}/api/v1/conocimiento/documentos`
- Body raw/JSON:

```json
{
  "modulo": "Inventario",
  "tipoDocumento": "Categoria",
  "referenciaId": "1",
  "contenido": "Texto de prueba para indexar",
  "embedding": [0.01, 0.02, 0.03]
}
```

## 8. Request sin autenticación: Status

- `GET {{baseUrl}}/api/status`
- Sin pestaña Authorization, sin body.

Útil como primer chequeo rápido de "¿la API está arriba?" antes de perder tiempo
diagnosticando cualquier otro request.

## 9. Errores comunes

- **`401 Unauthorized`** en un endpoint protegido — el token venció, no se corrió Login
  (paso 6) en esta sesión, o el Environment activo (arriba a la derecha) no es
  `ApiKnowledge - Local`.
- **`Error: self signed certificate`** — falta el paso 4 (solo aplica cuando `baseUrl`
  usa `https://localhost:7225`).
- **`400 Bad Request` con el body aparentemente bien** — revisar que el Body esté en modo
  **raw** con el tipo **JSON** seleccionado (no "Text") — con "Text" Postman no manda el
  header `Content-Type: application/json` y la API no puede deserializar el cuerpo.
- **No conecta en absoluto (`ECONNREFUSED` / timeout)** — confirmar que `ApiKnowledge` sí
  está corriendo, y si `baseUrl` apunta a la IIS de otra máquina, revisar el Firewall de
  esa máquina (`instalaciones/IIS_SETUP.md`, sección 6).

## Compartir la Collection con el equipo

**Collections** → clic derecho sobre `ApiKnowledge` → **Export** → guardar el `.json`
resultante. Se puede dejar en el repositorio (junto a los workflows exportados de n8n en
`n8n/*.workflow.json`) para que cualquiera lo importe con **Import** → arrastrar el
archivo, sin tener que armar cada request de nuevo a mano.
