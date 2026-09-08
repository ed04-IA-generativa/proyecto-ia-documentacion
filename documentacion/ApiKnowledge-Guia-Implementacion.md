# ApiKnowledge — Guía de implementación (.NET 8 + ADO.NET + SQL Server + PostgreSQL/pgvector)

Esta guía documenta la arquitectura real del proyecto `ApiKnowledge`: cómo está armado el proyecto, cómo se conecta a sus bases de datos, cómo se crean los modelos y, sobre todo, cómo funciona el estándar `ApiResponseModel` que envuelve **todas** las respuestas de la API. No es una guía genérica: cada clase y cada fragmento de código de aquí en adelante es el código real que existe hoy en el repositorio.

## Requisitos previos

- .NET 8 SDK
- Visual Studio 2022 (o VS Code con el SDK de .NET 8)
- SQL Server 2022 (módulos "Shared": autenticación, empresas, estaciones de trabajo)
- PostgreSQL 16+ con la extensión `pgvector` (módulo "Conocimiento": indexado y búsqueda semántica)
- Docker Desktop + WSL2, para levantar Postgres/pgvector localmente (ver `docs/instalaciones/POSTGRES_SETUP.md`)

## 1. Creación del proyecto

El proyecto se creó como un **ASP.NET Core Web API** (plantilla `Microsoft.NET.Sdk.Web`) apuntando a `net8.0`, con `Nullable` e `ImplicitUsings` habilitados. No usa Entity Framework ni ningún ORM: todo el acceso a datos es **ADO.NET directo**, ya sea contra SQL Server (`Microsoft.Data.SqlClient` / `System.Data.SqlClient`) o contra PostgreSQL (`Npgsql`).

Paquetes NuGet instalados (`ApiKnowledge.csproj`):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.30" />
  <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
  <PackageReference Include="Microsoft.SqlServer.Server" Version="1.0.0" />
  <PackageReference Include="Npgsql" Version="10.0.3" />
  <PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
  <PackageReference Include="System.Data.SqlClient" Version="4.9.1" />
</ItemGroup>
```

Por qué dos motores de base de datos a la vez:

- **SQL Server** sigue siendo la base transaccional existente. El módulo `Shared` (login) consume esta base a través de stored procedures ya existentes; a futuro también se consultará a través de vistas (ver sección 3).
- **PostgreSQL + pgvector** es la base nueva, dedicada exclusivamente al módulo `Conocimiento`: guarda los documentos vectorizados (embeddings) que alimentan la búsqueda semántica que usa el agente conversacional orquestado desde n8n. Se eligió Postgres/pgvector en vez de esperar a `VECTOR` nativo de SQL Server 2025 porque la instancia real del proyecto es SQL Server 2022 (sin tipo `VECTOR`) y Postgres+pgvector ya resuelve hoy tanto el almacenamiento como el cálculo de similitud de coseno de forma nativa.

## 2. Estructura del proyecto

```
ApiKnowledge/
├── Connection/
│   └── Conexion.cs                          (legado, sin uso activo)
├── Controllers/
│   ├── Conocimiento/
│   │   └── ConocimientoController.cs        (indexar + buscar, sobre PostgreSQL)
│   ├── Shared/
│   │   ├── AuthController.cs                (login, JWT)
│   │   └── StatusController.cs              (healthcheck)
│   └── Ventas/
│       └── VentasController.cs              (consulta sobre la vista proyIA.viwConsultaVentas)
├── Models/
│   ├── Request/
│   │   ├── Conocimiento/
│   │   │   ├── DocumentoVectorialM.cs
│   │   │   └── CriterioBusquedaVectorialM.cs
│   │   ├── Shared/
│   │   │   └── LoginM.cs
│   │   └── Ventas/
│   │       └── CriterioVentasM.cs
│   ├── Response/
│   │   ├── Conocimiento/
│   │   │   └── ResultadoBusquedaVectorialM.cs
│   │   ├── Shared/
│   │   │   └── PaBscUser2M.cs
│   │   └── Ventas/
│   │       └── VentaM.cs
│   └── Shared/
│       └── ApiResponseM.cs                  (el estándar ApiResponseModel<T>)
├── Servicios/
│   ├── Conocimiento/
│   │   ├── IVectorStore.cs                  (contrato del motor vectorial)
│   │   └── PostgresVectorStore.cs           (implementación sobre pgvector)
│   ├── Shared/
│   │   └── AuthService.cs
│   └── Ventas/
│       └── VentasService.cs
├── Utilities/
│   ├── SqlDataReaderExtensions.cs
│   ├── StoredProcedureExecutor.cs           (consumo por stored procedures, ej. login)
│   └── ViewQueryExecutor.cs                 (consumo por vistas, ej. Ventas)
├── sql/
│   └── postgres_conocimiento_vector_store.sql
├── appsettings.json
└── Program.cs
```

Convención de carpetas: `Models/Request` y `Models/Response` separan lo que entra de lo que sale de la API; `Models/Shared` guarda lo transversal (como `ApiResponseModel<T>`). Dentro de cada carpeta, la subcarpeta indica el módulo (`Shared` para autenticación, `Conocimiento` para la capa vectorial). Los nombres de clase terminan siempre en `M` (`LoginM`, `PaBscUser2M`, `DocumentoVectorialM`).


## 3. Configuración de las cadenas de conexión

Ambas cadenas conviven en `appsettings.json`, bajo `ConnectionStrings`. Las credenciales reales **no** se documentan aquí — se reemplazan por `<usuario>`/`<contraseña>` a propósito, para que quede claro en qué parte de la cadena van sin exponer un valor real:

```json
{
  "ConnectionStrings": {
    "ConnectionString": "Server=S2019-DEVELOPER\\DEVELOPER;Database=test_cuenta_corriente_20250828;User Id=<usuario>;Password=<contraseña>;",
    "PostgresConocimiento": "Host=localhost;Port=5432;Database=apiknowledge;Username=<usuario>;Password=<contraseña>"
  }
}
```

- `ConnectionString` → SQL Server, la lee `StoredProcedureExecutor` (ver sección 6) y es la que usan `AuthService`, `OrganizationService`, etc. Cada parte:
  - `Server`: instancia de SQL Server a la que se conecta (`host\instancia`). Para este proyecto es `S2019-DEVELOPER\DEVELOPER`.
  - `Database`: base de datos contra la que corren las consultas. Para este proyecto es `test_cuenta_corriente_20250828` — es la base donde vive el esquema `proyIA` con las vistas que consume la API (por ejemplo `proyIA.viwConsultaVentas`, la vista de ventas).
  - `User Id` / `Password`: usuario y contraseña de SQL Server con permiso de lectura sobre esas vistas. Aquí van en blanco (`<usuario>`/`<contraseña>`); el valor real solo vive en el `appsettings.json` de cada ambiente (dev/QA/prod) o en su gestor de secretos, nunca en un documento versionado.
- `PostgresConocimiento` → PostgreSQL/pgvector, la lee directamente `PostgresVectorStore` con `configuration.GetConnectionString("PostgresConocimiento")`. Cada parte:
  - `Host` / `Port`: dirección y puerto del servidor Postgres. En desarrollo es el contenedor Docker local, expuesto en `localhost:5432`.
  - `Database`: base dedicada exclusivamente al módulo Conocimiento (`apiknowledge`), separada de la base transaccional de SQL Server de arriba.
  - `Username` / `Password`: credenciales del rol de Postgres con permisos sobre la tabla `conocimiento_documento`. Igual que en SQL Server, se dejan en blanco aquí por la misma razón.

Cada clase de acceso a datos lee **su propia** cadena por nombre; no hay una cadena "por defecto" implícita, así que agregar un tercer motor en el futuro es tan simple como agregar una entrada más aquí y leerla en la clase correspondiente.

## 4. Clase `SqlDataReaderExtensions.cs`

Namespace: `ApiBusiness.Utilidades`. Son métodos de extensión sobre `SqlDataReader` para leer columnas de forma segura ante nulos, sin repetir `reader.IsDBNull(...)` en cada mapeo:

```csharp
public static class SqlDataReaderExtensions
{
    // Lectura optimizada por posición cacheada, para conjuntos grandes (reportes/bulk).
    public static T? GetBulkValue<T>(this SqlDataReader reader, string columnName) { /* ... */ }

    // Lectura por nombre de columna, con manejo de nulos.
    public static T? GetValueOrDefault<T>(this SqlDataReader reader, string columnName)
    {
        if (reader.HasColumn(columnName))
        {
            object value = reader[columnName];
            return value != DBNull.Value ? (T)value : default;
        }
        return default;
    }

    // Lectura por índice de columna, con manejo de nulos.
    public static T? GetValueOrDefaulInt<T>(this SqlDataReader reader, int indexColumn) { /* ... */ }

    private static bool HasColumn(this SqlDataReader reader, string columnName) { /* ... */ }
}
```

`GetValueOrDefault<T>` es el que se usa en el 100% de los `MapToModel` del proyecto (ver `PaBscUser2M.MapToModel` en la sección 8). `GetBulkValue<T>` cachea el ordinal de la columna por instancia de reader, pensado para procedimientos que devuelven muchas filas y donde repetir `GetOrdinal` por cada fila sí importa.

## 5. Clase `ApiResponseModel<T>` — el estándar

Namespace: `ApiBusiness.Model.Shared`, archivo `Models/Shared/ApiResponseM.cs`. **Todo** endpoint de la API devuelve este envoltorio, nunca el dato "pelado":

```csharp
public class ApiResponseModel<T>
{
    public bool Status { get; set; } = false;
    public string Message { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string StoredProcedure { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public T Data { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Version { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; } = null;
    public string ErrorCode { get; set; } = string.Empty; // "1-547" (SQL), "2" (API), "3" (no controlado)

    public ApiResponseModel(T data, IConfiguration configuration)
    {
        Data = data;
        Version = configuration["Version"] ?? "Desconocida";
        ReleaseDate = GetReleaseDate(configuration);
    }

    private static DateTimeOffset? GetReleaseDate(IConfiguration configuration)
    {
        // Arma un DateTimeOffset UTC a partir de ReleaseDate:Year/Month/Date/Hour/Minute
        // en appsettings.json. Si falta cualquier campo, devuelve null sin tronar.
    }
}
```

Puntos clave del estándar:

- El constructor **obliga** a pasar `data` y `configuration`: así `Version` y `ReleaseDate` quedan siempre resueltos desde `appsettings.json`, sin que cada controlador tenga que acordarse de setearlos.
- `ErrorCode` sigue una convención de tres prefijos, consistente en todo el proyecto (ver sección 12):
  - `1-<número>` → error de SQL Server, `<número>` es `SqlException.Number`.
  - `2` → error de lógica dentro de la API (mapeo, validación de negocio, etc.).
  - `3` → error no controlado, típicamente de validación de modelo (`ModelState`).
- `Parameters` y `StoredProcedure` son opcionales y solo los llenan los métodos de `StoredProcedureExecutor` — sirven para depurar qué SP y con qué parámetros se ejecutó una petición, sin que eso se filtre a los consumidores de la API salvo que abran el JSON completo.
- `PostgresVectorStore` (que no pasa por `StoredProcedureExecutor`, porque no ejecuta stored procedures sino SQL parametrizado directo contra Postgres) también devuelve `ApiResponseModel<T>`: el estándar aplica a la forma de la respuesta, no a cómo se llega a los datos.

## 6. Clase `StoredProcedureExecutor.cs`

Namespace: `ApiBusiness.Utilidades`. Es la clase base de la que heredan todos los servicios que hablan con **SQL Server**. Centraliza apertura de conexión, ejecución del stored procedure, mapeo del `SqlDataReader` a modelos y — lo más importante — el manejo de errores homologado con `ApiResponseModel`.

Métodos que expone (todos `protected`, para usarse solo desde clases que heredan de esta):

- `ExecuteStoredProcedureAsync<T>(procedureName, mapFunction, parameters)` — ejecuta un SP que devuelve un único conjunto de filas y las mapea con `mapFunction`.
- `ExecuteStoredProcedureAsync<T>(procedureName, mapFunction, commandTimeoutSegundos, parameters)` — igual, con timeout configurable por llamada (default 180s si se omite).
- `ExecuteStoredProcedureNonQueryAsync(procedureName, parameters)` — para SPs que no devuelven filas (inserts/updates de una sola operación).
- `ExecuteStoredProcedureMultiAsync<TResult>(procedureName, mapFunction, parameters)` — para SPs que devuelven **varios** conjuntos de resultados; se apoya en `LeerConjuntoAsync<T>` para leerlos uno por uno, avanzando con `reader.NextResultAsync()`.

Patrón de manejo de errores, idéntico en los cuatro métodos:

```csharp
try
{
    using SqlConnection sql = new(_connectionString);
    using SqlCommand cmd = new(procedureName, sql) { CommandType = CommandType.StoredProcedure };
    cmd.Parameters.AddRange(parameters);

    await sql.OpenAsync();
    using var reader = await cmd.ExecuteReaderAsync();

    List<T> response = new();
    while (await reader.ReadAsync())
        response.Add(mapFunction(reader));

    return new ApiResponseModel<List<T>>(response, _configuration)
    {
        Parameters = formattedParameters,
        Status = true,
        StoredProcedure = procedureName,
        Message = "Operacion exitosa"
    };
}
catch (SqlException ex)
{
    return new ApiResponseModel<List<T>>(new List<T>(), _configuration)
    {
        Parameters = formattedParameters,
        Error = ex.Message,
        ErrorCode = $"1-{ex.Number}",
        Status = false,
        StoredProcedure = procedureName,
        Message = "Error en la base de datos."
    };
}
catch (Exception e)
{
    return new ApiResponseModel<List<T>>(new List<T>(), _configuration)
    {
        Parameters = formattedParameters,
        Error = e.Message,
        Status = false,
        StoredProcedure = procedureName,
        Message = "Error en la lógica de la API.",
        ErrorCode = "2"
    };
}
```

Un detalle propio de esta clase (no aparece en la mayoría de guías genéricas): los parámetros se formatean antes de ejecutarse (`FormatearParametros`) para dejarlos legibles en `ApiResponseModel.Parameters`. Si un parámetro es un TVP (`SqlDbType.Structured`), no se copia la tabla completa — solo su tipo y número de filas —, y si un `varchar(max)`/`nvarchar(max)` supera 500 caracteres se trunca con un resumen de tamaño. Esto evita que el eco de depuración infle la respuesta cuando el parámetro es un JSON grande o una tabla masiva.

## 7. Capa de servicios — ejemplo real: `AuthService`

Namespace: `ApiBusiness.Servicios.Shared`. Hereda de `StoredProcedureExecutor` y expone un método por cada operación de negocio, siempre devolviendo `ApiResponseModel<T>`:

```csharp
public class AuthService(IConfiguration configuration) : StoredProcedureExecutor(configuration)
{
    public async Task<ApiResponseModel<List<PaBscUser2M>>> PA_bsc_User_2(LoginM request)
    {
        string storeProcedure = "PA_bsc_User_2";

        var parameters = new SqlParameter[]
        {
            new("@pOpcion", SqlDbType.Int) { Value = request.pOpcion },
            new("@pUserName", SqlDbType.VarChar, 30) { Value = request.pUserName },
            new("@pPass", SqlDbType.VarChar, 100) { Value = request.pPass }
        };

        return await ExecuteStoredProcedureAsync(storeProcedure, PaBscUser2M.MapToModel, parameters);
    }
}
```

El modelo de respuesta trae su propio mapeo estático, para no repetir la lógica de lectura del reader en el servicio:

```csharp
// Models/Response/Shared/PaBscUser2M.cs
public class PaBscUser2M
{
    public bool Continuar { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Token { get; set; }

    public static PaBscUser2M MapToModel(SqlDataReader reader) => new()
    {
        Continuar = reader.GetValueOrDefault<bool>("Continuar"),
        Mensaje = reader.GetValueOrDefault<string>("Mensaje") ?? string.Empty,
        UserName = reader.GetValueOrDefault<string>("UserName") ?? string.Empty,
        Email = reader.GetValueOrDefault<string?>("Email")
    };
}
```

El modelo de request es un DTO simple (`Models/Request/Shared/LoginM.cs`):

```csharp
public class LoginM
{
    public int pOpcion { get; set; } = 1;
    public string pUserName { get; set; } = string.Empty;
    public string pPass { get; set; } = string.Empty;
}
```

## 8. Capa de controladores y endpoints — ejemplo real: `AuthController`

Namespace: `ApiBusiness.Controllers.Shared`. El patrón del proyecto (fuera del módulo Conocimiento) es instanciar el servicio directamente con `new(configuration)`, no por inyección de dependencias:

```csharp
[Route("api/[controller]")]
[ApiController]
public class AuthController(IConfiguration configuration) : ControllerBase
{
    private readonly AuthService _authService = new(configuration);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginM request)
    {
        ApiResponseModel<List<PaBscUser2M>> response = await _authService.PA_bsc_User_2(request);

        if (!response.Status)
            return BadRequest(response);

        PaBscUser2M? usuario = response.Data.FirstOrDefault();

        if (usuario is null || !usuario.Continuar)
            return Unauthorized(response);

        usuario.Token = GenerarToken(request.pUserName);

        return Ok(response);
    }

    private string GenerarToken(string userName)
    {
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, configuration["Jwt:Subject"] ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("UserName", userName)
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var signing = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: signing
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

`POST /api/auth/login` responde con `ApiResponseModel<List<PaBscUser2M>>`, con `Data[0].Token` conteniendo el JWT recién generado si el login fue exitoso. Nótese que `AuthController` (junto con `OrganizationController` y `StatusController`) **no** está versionado bajo `/api/v1/`, a diferencia de `ConocimientoController`. Es una inconsistencia real y conocida del proyecto, no un estándar a replicar — la convención hacia la que se está migrando es que toda ruta nueva vaya bajo `/api/v1/`.

## 9. Configuración de versión en `appsettings.json`

```json
{
  "Version": "1.0.0",
  "ReleaseDate": {
    "Year": "2026",
    "Month": "08",
    "Date": "26",
    "Hour": "14",
    "Minute": "15"
  }
}
```

`ApiResponseModel<T>` lee estos valores en su constructor (sección 5) y los agrega a **cada** respuesta, sin que ningún controlador o servicio tenga que hacer nada extra. Subir la versión del API es cambiar estos dos bloques en `appsettings.json`, nunca tocar código.

## 10. `StatusController`

```csharp
namespace ApiBusiness.Controllers.Shared
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatusController(IConfiguration configuration) : ControllerBase
    {
        [HttpGet()]
        public IActionResult StatusApp()
        {
            ApiResponseModel<List<object>> status = new(new List<object>(), configuration)
            {
                Status = true,
                Message = "Ok"
            };
            return Ok(status);
        }
    }
}
```

`GET /api/status` sirve como healthcheck: no toca ninguna base de datos, solo confirma que la API está viva y devuelve el envoltorio estándar con `Version`/`ReleaseDate` ya resueltos — útil para verificar rápido qué versión quedó desplegada en un ambiente.

## 11. Control de errores en la API

El proyecto usa una convención de tres prefijos para `ErrorCode`, aplicada de forma consistente en `StoredProcedureExecutor` y en `PostgresVectorStore`:

| Prefijo | Origen | Ejemplo | Dónde se genera |
|---|---|---|---|
| `1-<n>` | Error de base de datos | `1-547` (violación de constraint en SQL Server) | `catch (SqlException ex)` → `ErrorCode = $"1-{ex.Number}"` en `StoredProcedureExecutor`; `catch (Exception ex)` con `ErrorCode = "1"` en `PostgresVectorStore` (Npgsql no expone un código numérico tan directo como `SqlException.Number`) |
| `2` | Error de lógica de la API | Mapeo fallido, regla de negocio violada | `catch (Exception e)` genérico en `StoredProcedureExecutor` |
| `3` | Error no controlado / validación de modelo | `[Required]` incumplido, tipo de dato inválido en el `[FromBody]` | `ApiBehaviorOptions.InvalidModelStateResponseFactory` en `Program.cs` (sección 12) |

En todos los casos la respuesta sigue siendo un `ApiResponseModel<T>` con `Status = false`, `Message` legible para el consumidor y `Error` con el detalle técnico — nunca se devuelve una excepción cruda ni un `500` sin envolver.

## 12. Manejo de errores no controlados (validación de modelos)

Configurado en `Program.cs`, sobreescribiendo la respuesta por defecto de ASP.NET Core cuando el `ModelState` no es válido (por ejemplo, un campo `[Required]` faltante en el body de una petición):

```csharp
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value.Errors.Select(err => err.ErrorMessage).ToArray()
            );

        ApiResponseModel<List<object>> response = new(new List<object>(), configuration)
        {
            Error = JsonSerializer.Serialize(errors),
            Message = "Error no controlado",
            ErrorCode = "3"
        };

        return new BadRequestObjectResult(response);
    };
});
```

Diferencia importante frente a implementaciones genéricas de este mismo patrón: aquí la serialización usa `System.Text.Json` (`JsonSerializer.Serialize`), no `Newtonsoft.Json` — el proyecto retiró `Newtonsoft.Json` para no arrastrar una dependencia que `System.Text.Json` (incluido en el SDK) ya cubre. Con esto, cualquier error de validación de modelo llega al cliente como un `400 Bad Request` con la forma estándar de `ApiResponseModel`, `ErrorCode = "3"` y el detalle de qué campo(s) fallaron dentro de `Error` (como JSON serializado).

## 13. Caso especial: la capa de Conocimiento (RAG) sobre PostgreSQL + pgvector

El módulo `Conocimiento` es la parte más nueva y la que más se aparta del patrón "todo por stored procedure" de las secciones anteriores — deliberadamente, porque su fuente de datos no es SQL Server. Sigue envuelto en `ApiResponseModel<T>` como todo lo demás, pero:

- No hereda de `StoredProcedureExecutor` (esa clase es específica de `SqlClient`/SQL Server).
- Ejecuta SQL parametrizado directo contra PostgreSQL con `Npgsql`, en vez de llamar stored procedures.
- Está detrás de una interfaz, `IVectorStore`, para poder cambiar de motor vectorial sin tocar el resto del proyecto.

### 13.1. Contrato `IVectorStore`

```csharp
// Servicios/Conocimiento/IVectorStore.cs
namespace ApiBusiness.Servicios.Conocimiento
{
    public interface IVectorStore
    {
        Task<ApiResponseModel<string>> UpsertarAsync(DocumentoVectorialM documento);
        Task<ApiResponseModel<List<ResultadoBusquedaVectorialM>>> BuscarAsync(CriterioBusquedaVectorialM criterio);
    }
}
```

### 13.2. Modelos

```csharp
// Models/Request/Conocimiento/DocumentoVectorialM.cs
public class DocumentoVectorialM
{
    public string Modulo { get; set; } = string.Empty;
    public string TipoDocumento { get; set; } = string.Empty;
    public string ReferenciaId { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = [];
    public byte? Empresa { get; set; }
    public short? EstacionTrabajo { get; set; }
    public string? UserName { get; set; }
}

// Models/Request/Conocimiento/CriterioBusquedaVectorialM.cs
public class CriterioBusquedaVectorialM
{
    public float[] Embedding { get; set; } = [];
    public string? Modulo { get; set; }
    public byte? Empresa { get; set; }
    public short? EstacionTrabajo { get; set; }
    public string? UserName { get; set; }
    public int TopN { get; set; } = 5;
}

// Models/Response/Conocimiento/ResultadoBusquedaVectorialM.cs
public class ResultadoBusquedaVectorialM
{
    public long DocumentoId { get; set; }
    public string Modulo { get; set; } = string.Empty;
    public string TipoDocumento { get; set; } = string.Empty;
    public string ReferenciaId { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public double Similitud { get; set; }
}
```

`Empresa`, `EstacionTrabajo` y `UserName` son nulables a propósito: `null` en el criterio de búsqueda significa "esa dimensión no filtra"; `null` en el documento indexado significa "documento sin restricción en esa dimensión" (por ejemplo, catálogos globales).

### 13.3. `PostgresVectorStore` — implementación de `IVectorStore`

```csharp
public class PostgresVectorStore(IConfiguration configuration) : IVectorStore
{
    private readonly string _connectionString = configuration.GetConnectionString("PostgresConocimiento") ?? "";

    public async Task<ApiResponseModel<string>> UpsertarAsync(DocumentoVectorialM documento)
    {
        // ... valida que Embedding no venga vacío
        const string sql = """
            INSERT INTO conocimiento_documento
                (modulo, tipo_documento, referencia_id, contenido, embedding, empresa, estacion_trabajo, user_name)
            VALUES
                (@modulo, @tipoDocumento, @referenciaId, @contenido, @embedding::vector, @empresa, @estacionTrabajo, @userName)
            ON CONFLICT (modulo, tipo_documento, referencia_id) DO UPDATE SET
                contenido = EXCLUDED.contenido,
                embedding = EXCLUDED.embedding,
                empresa = EXCLUDED.empresa,
                estacion_trabajo = EXCLUDED.estacion_trabajo,
                user_name = EXCLUDED.user_name,
                estado = 1,
                m_fecha_hora = now();
            """;
        // ... abre NpgsqlConnection, ejecuta con parámetros, envuelve en ApiResponseModel<string>
    }

    public async Task<ApiResponseModel<List<ResultadoBusquedaVectorialM>>> BuscarAsync(CriterioBusquedaVectorialM criterio)
    {
        const string sql = """
            SELECT documento_id, modulo, tipo_documento, referencia_id, contenido,
                   1 - (embedding <=> @embedding::vector) AS similitud
            FROM conocimiento_documento
            WHERE estado = 1
              AND (@modulo IS NULL OR modulo = @modulo)
              AND (empresa IS NULL OR empresa = @empresa)
              AND (estacion_trabajo IS NULL OR estacion_trabajo = @estacionTrabajo)
              AND (user_name IS NULL OR user_name = @userName)
            ORDER BY embedding <=> @embedding::vector
            LIMIT @topN;
            """;
        // ... ejecuta y mapea filas a ResultadoBusquedaVectorialM
    }
}
```

El punto no negociable del proyecto está resuelto en esta única sentencia SQL de `BuscarAsync`: el filtro de permisos (`empresa`, `estacion_trabajo`, `user_name`) y el ranking por similitud (`<=>`, distancia de coseno de pgvector) ocurren en el **mismo** `SELECT`. No hay ningún escenario en el que un documento fuera de los permisos del usuario llegue a compararse contra el vector de búsqueda — el `WHERE` descarta las filas antes de que `ORDER BY embedding <=> @embedding` las toque.

El vector se manda como JSON serializado (`System.Text.Json`) y se castea a `vector` en la propia sentencia SQL (`@embedding::vector`) — así el modelo C# solo trabaja con `float[]`, sin depender de ningún tipo específico de Npgsql para pgvector.

Esquema de la tabla (`sql/postgres_conocimiento_vector_store.sql`): extensión `vector` habilitada, tabla `conocimiento_documento` con columna `embedding VECTOR(1536)` (dimensión de `gemini-embedding-001`), restricción `UNIQUE (modulo, tipo_documento, referencia_id)` que habilita el `ON CONFLICT` de arriba, e índice sobre las columnas de permisos.

### 13.4. `ConocimientoController` — el único que usa inyección de dependencias

```csharp
[Authorize]
[Route("api/v1/[controller]")]
[ApiController]
public class ConocimientoController(IVectorStore vectorStore) : ControllerBase
{
    [HttpPost("documentos")]
    public async Task<IActionResult> Upsertar([FromBody] DocumentoVectorialM documento)
    {
        ApiResponseModel<string> response = await vectorStore.UpsertarAsync(documento);
        return response.Status ? Ok(response) : BadRequest(response);
    }

    [HttpPost("buscar")]
    public async Task<IActionResult> Buscar([FromBody] CriterioBusquedaVectorialM criterio)
    {
        ApiResponseModel<List<ResultadoBusquedaVectorialM>> response = await vectorStore.BuscarAsync(criterio);
        return response.Status ? Ok(response) : BadRequest(response);
    }
}
```

A diferencia de `AuthController` (que hace `new AuthService(configuration)`), este controlador recibe `IVectorStore` por inyección de dependencias. Es intencional: ese es justo el punto de tener la interfaz — poder registrar `PostgresVectorStore` hoy y, si algún día se cambia de motor vectorial, registrar otra implementación en `Program.cs` sin tocar el controlador ni ningún otro punto del proyecto:

```csharp
// Program.cs
builder.Services.AddScoped<IVectorStore, PostgresVectorStore>();
```

`ConocimientoController` también es el único controlador ya versionado bajo `/api/v1/` y el único protegido con `[Authorize]` desde su creación — el JWT que emite `AuthController.GenerarToken` (sección 8) es el mismo que valida el middleware de autenticación configurado en `Program.cs` para poder llamar estos endpoints.

## 14. Consumo de vistas de SQL Server — módulo Ventas

Como se acordó, el acceso a datos transaccionales de SQL Server para reportes/consultas (a diferencia del login) se hace **por vistas**, no por stored procedures. La primera vista consumida es `[proyIA].[viwConsultaVentas]`, ya creada en la base de datos, y que trae su propio filtro base (tipo de documento, estado, fecha desde `20260102`, etc.).

`StoredProcedureExecutor` (sección 6) no sirve para esto: está fijo a `CommandType.StoredProcedure` (ejecuta `EXEC <procedimiento>`), y una vista se consulta con `SELECT ... FROM` (`CommandType.Text`). Por eso se creó una clase paralela, `ViewQueryExecutor`, con el mismo contrato de manejo de errores y el mismo envoltorio `ApiResponseModel<T>`, pero para SQL de texto parametrizado.

### 14.1. `ViewQueryExecutor.cs`

Namespace: `ApiBusiness.Utilidades`. Análogo a `StoredProcedureExecutor`, pero recibe la sentencia SQL como texto en vez del nombre de un procedimiento:

```csharp
public class ViewQueryExecutor(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("ConnectionString") ?? "";

    protected async Task<ApiResponseModel<List<T>>> ExecuteQueryAsync<T>(
        string objectName,       // nombre de la vista/tabla, solo para depuración
        string consultaSql,
        Func<SqlDataReader, T> mapFunction,
        params SqlParameter[] parameters
        )
    {
        try
        {
            using SqlConnection sql = new(_connectionString);
            using SqlCommand cmd = new(consultaSql, sql) { CommandType = CommandType.Text };
            cmd.Parameters.AddRange(parameters);

            await sql.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            List<T> response = new();
            while (await reader.ReadAsync())
                response.Add(mapFunction(reader));

            return new ApiResponseModel<List<T>>(response, _configuration)
            {
                Status = true,
                StoredProcedure = objectName,
                Message = "Operacion exitosa"
            };
        }
        catch (SqlException ex)
        {
            // Igual que StoredProcedureExecutor: ErrorCode = "1-{numero}"
        }
        catch (Exception e)
        {
            // Igual que StoredProcedureExecutor: ErrorCode = "2"
        }
    }
}
```

Usa la misma cadena `ConnectionString` (SQL Server) que `StoredProcedureExecutor` — la vista vive en la misma base de datos (`test_cuenta_corriente_20250828`) que ya usa el login, así que no hace falta una cadena de conexión aparte.

### 14.2. Modelos

```csharp
// Models/Request/Ventas/CriterioVentasM.cs
public class CriterioVentasM
{
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string? Cliente { get; set; }
    public string? Producto { get; set; }
    public string? TipoCanal { get; set; }
    public int TopN { get; set; } = 500;
}
```

`VentaM` (`Models/Response/Ventas/VentaM.cs`) mapea 1:1 las columnas del `SELECT` de la vista, con los tipos reales confirmados contra `sys.columns` de SQL Server — puntos a tener en cuenta porque no son lo primero que uno asumiría por el nombre de la columna:

- `Bodega` es `smallint`, no `int` → se mapea como `short`.
- `GPS_Latitud` y `GPS_Longitud` son `varchar(200)`, **no** numéricos → se mapean como `string?`, no como `decimal?`.
- `Cliente`, `Consecutivo`, `VtaSinIva`, `Cantidad`, `Cuenta_Correntista` y `Documento_Nombre` son nullable en la vista (por los `LEFT JOIN` y conversiones) → todas sus propiedades correspondientes en `VentaM` son nullable.

```csharp
public static VentaM MapToModel(SqlDataReader reader) => new()
{
    FechaDocumento = reader.GetValueOrDefault<DateTime>("Fecha_Documento"),
    Producto = reader.GetValueOrDefault<string>("Producto") ?? string.Empty,
    Bodega = reader.GetValueOrDefault<short>("Bodega"),
    GpsLatitud = reader.GetValueOrDefault<string?>("GPS_Latitud"),
    // ... resto de columnas, mismo patrón que PaBscUser2M.MapToModel (sección 7)
};
```

### 14.3. `VentasService` y `VentasController`

Mismo patrón que `AuthService`/`AuthController` (secciones 7 y 8): el servicio hereda de `ViewQueryExecutor` en vez de `StoredProcedureExecutor`, y el controlador instancia el servicio con `new(configuration)`.

```csharp
// Servicios/Ventas/VentasService.cs
public class VentasService(IConfiguration configuration) : ViewQueryExecutor(configuration)
{
    public async Task<ApiResponseModel<List<VentaM>>> ConsultarVentas(CriterioVentasM criterio)
    {
        const string sql = """
            SELECT Fecha_Documento, Producto, UM, Clase, Vendedor, Cliente, Documento, Consecutivo,
                   TipoTransaccion, SerieDocto, VtaSinIva, Cantidad, TipoCliente, Localizacion,
                   TipoCanalDescripcion, Cuenta_Correntista, Cuenta_Cta, Bodega, Documento_Nombre,
                   GPS_Latitud, GPS_Longitud
            FROM [proyIA].[viwConsultaVentas]
            WHERE (@fechaInicio IS NULL OR Fecha_Documento >= @fechaInicio)
              AND (@fechaFin IS NULL OR Fecha_Documento <= @fechaFin)
              AND (@cliente IS NULL OR Cliente LIKE '%' + @cliente + '%')
              AND (@producto IS NULL OR Producto LIKE '%' + @producto + '%')
              AND (@tipoCanal IS NULL OR TipoCanalDescripcion = @tipoCanal)
            ORDER BY Fecha_Documento DESC
            OFFSET 0 ROWS FETCH NEXT @topN ROWS ONLY;
            """;
        // ... arma los SqlParameter y llama ExecuteQueryAsync("proyIA.viwConsultaVentas", sql, VentaM.MapToModel, parameters)
    }
}
```

Todos los filtros de `CriterioVentasM` son opcionales (`@parametro IS NULL OR columna = @parametro`); la vista ya trae su propio filtro base, así que estos solo lo acotan más. `TopN` (default 500) evita traer resultados sin límite — se implementa con `OFFSET ... FETCH NEXT`, el estándar de paginación de SQL Server moderno.

```csharp
// Controllers/Ventas/VentasController.cs
[Authorize]
[Route("api/v1/[controller]")]
[ApiController]
public class VentasController(IConfiguration configuration) : ControllerBase
{
    private readonly VentasService _ventasService = new(configuration);

    [HttpPost("consultar")]
    public async Task<IActionResult> Consultar([FromBody] CriterioVentasM criterio)
    {
        ApiResponseModel<List<VentaM>> response = await _ventasService.ConsultarVentas(criterio);
        return response.Status ? Ok(response) : BadRequest(response);
    }
}
```

`POST /api/v1/ventas/consultar` sigue la misma convención de `ConocimientoController`: ruta versionada y protegida con `[Authorize]`, porque es un endpoint nuevo y son datos transaccionales reales.

> Verificado contra la base real (`sqlcmd` contra `S2019-DEVELOPER\DEVELOPER`, `test_cuenta_corriente_20250828`): la consulta ejecuta sin errores y los tipos de columna arriba fueron confirmados contra `sys.columns`. La vista devolvió 0 filas en esta base de prueba (no hay ventas registradas después del corte de fecha que trae la vista) — es una característica de los datos de esta base, no un problema de la consulta.

## 15. Notas

- **Envoltorio universal**: no existe ningún endpoint en el proyecto que devuelva un objeto "pelado" — siempre `ApiResponseModel<T>`, con `Status`, `Message`, `Error`, `ErrorCode`, `Version` y `ReleaseDate` resueltos de forma consistente.
- **Dos motores de datos, un mismo estándar de respuesta**: SQL Server (vía `StoredProcedureExecutor`) y PostgreSQL/pgvector (vía `PostgresVectorStore`) son intencionalmente distintos en *cómo* acceden a los datos, pero idénticos en *qué* devuelven al consumidor de la API.
- **Convención de errores**: `1-<n>` (SQL), `2` (lógica de API), `3` (validación de modelo/no controlado) — se respeta en toda la capa de datos, incluida la de Postgres.
- **Versionado de rutas pendiente de unificar**: `ConocimientoController` y `VentasController` ya viven bajo `/api/v1/`; `AuthController` y `StatusController` siguen sin versionar (`/api/[controller]`). Es deuda técnica conocida, no un patrón a replicar en código nuevo.
- **Permisos antes que similitud**: la regla no negociable de la capa de Conocimiento es que el filtro de permisos (empresa/estación de trabajo/usuario) ocurra siempre antes — o en la misma sentencia que — el ranking por similitud vectorial, nunca después. `PostgresVectorStore.BuscarAsync` la cumple combinando `WHERE` y `ORDER BY ... <=>` en un único `SELECT`.
- **Generación de embeddings fuera de ApiKnowledge**: la API nunca llama a un proveedor de IA para generar vectores — solo almacena y busca `float[]` que ya vienen calculados. Ese cálculo (hoy con `gemini-embedding-001`, 1536 dimensiones) ocurre en el workflow de n8n que orquesta indexado y consultas.
- **Dos formas de consumir SQL Server**: `StoredProcedureExecutor` (stored procedures, ej. login) y `ViewQueryExecutor` (vistas, ej. Ventas) coexisten a propósito — no hay una única forma "correcta" de hablar con SQL Server en este proyecto, sino una por cada tipo de objeto que se consulta. Ambas devuelven el mismo `ApiResponseModel<T>` y siguen la misma convención de `ErrorCode`.
- **Endpoints independientes del orquestador**: todos los endpoints de ApiKnowledge son APIs REST estándar (HTTP + JSON, JWT Bearer, `ApiResponseModel<T>`) — no tienen ninguna dependencia de n8n en particular. n8n es simplemente el orquestador que hoy los consume (genera embeddings y llama `POST /api/v1/conocimiento/*`), pero cualquier otro orquestador capaz de hacer peticiones HTTP (por ejemplo, uno basado en GPT) puede consumir exactamente los mismos endpoints sin ningún cambio en la API.
- **Referencias relacionadas**: `docs/instalaciones/POSTGRES_SETUP.md` (cómo levantar Postgres/pgvector en Docker localmente) y `docs/SINCRONIZACION_SQLSERVER_POSTGRES.md` (contexto de por qué conviven ambos motores).
