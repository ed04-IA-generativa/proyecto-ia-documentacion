using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Conocimiento;
using ApiBusiness.Models.Response.Conocimiento;
using Npgsql;
using System.Text.Json;

namespace ApiBusiness.Servicios.Conocimiento
{
    /* =============================================================================
       Implementacion de IVectorStore sobre PostgreSQL + pgvector.

       A diferencia de la version interina que se hizo sobre SQL Server 2022 (que no
       tiene tipo vector y calculaba similitud en C#), aqui el filtro de permisos y el
       ranking por similitud van en la misma sentencia SQL: pgvector calcula distancia
       de coseno de forma nativa (operador <=>), asi que nunca compara un documento que
       el WHERE ya haya descartado por permisos.
       ============================================================================= */
    public class PostgresVectorStore(IConfiguration configuration) : IVectorStore
    {
        private readonly string _connectionString = configuration.GetConnectionString("PostgresConocimiento") ?? "";

        // Guarda o actualiza un documento vectorizado, identificado por su origen
        // (Modulo, TipoDocumento, ReferenciaId).
        public async Task<ApiResponseModel<string>> UpsertarAsync(DocumentoVectorialM documento)
        {
            if (documento.Embedding.Length == 0)
            {
                return new ApiResponseModel<string>(string.Empty, configuration)
                {
                    Status = false,
                    Message = "Error en la lógica de la API.",
                    Error = "El documento no trae un vector de embedding.",
                    ErrorCode = "2"
                };
            }

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

            try
            {
                await using var conexion = new NpgsqlConnection(_connectionString);
                await conexion.OpenAsync();

                await using var comando = new NpgsqlCommand(sql, conexion);
                comando.Parameters.AddWithValue("modulo", documento.Modulo);
                comando.Parameters.AddWithValue("tipoDocumento", documento.TipoDocumento);
                comando.Parameters.AddWithValue("referenciaId", documento.ReferenciaId);
                comando.Parameters.AddWithValue("contenido", documento.Contenido);
                comando.Parameters.AddWithValue("embedding", JsonSerializer.Serialize(documento.Embedding));
                comando.Parameters.AddWithValue("empresa", (object?)documento.Empresa ?? DBNull.Value);
                comando.Parameters.AddWithValue("estacionTrabajo", (object?)documento.EstacionTrabajo ?? DBNull.Value);
                comando.Parameters.AddWithValue("userName", (object?)documento.UserName ?? DBNull.Value);

                await comando.ExecuteNonQueryAsync();

                return new ApiResponseModel<string>("Operación exitosa", configuration)
                {
                    Status = true,
                    Message = "Operacion exitosa"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponseModel<string>(string.Empty, configuration)
                {
                    Status = false,
                    Message = "Error en la base de datos.",
                    Error = ex.Message,
                    ErrorCode = "1"
                };
            }
        }

        // Busca los documentos mas parecidos al vector recibido, ya filtrados por
        // permisos. NULL en Empresa/EstacionTrabajo/UserName del criterio => esa
        // dimension no se usa como filtro (pero un documento restringido sigue oculto
        // si el criterio no aporta el dato, igual que en la version de SQL Server).
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

            try
            {
                await using var conexion = new NpgsqlConnection(_connectionString);
                await conexion.OpenAsync();

                await using var comando = new NpgsqlCommand(sql, conexion);
                comando.Parameters.AddWithValue("embedding", JsonSerializer.Serialize(criterio.Embedding));
                comando.Parameters.AddWithValue("modulo", (object?)criterio.Modulo ?? DBNull.Value);
                comando.Parameters.AddWithValue("empresa", (object?)criterio.Empresa ?? DBNull.Value);
                comando.Parameters.AddWithValue("estacionTrabajo", (object?)criterio.EstacionTrabajo ?? DBNull.Value);
                comando.Parameters.AddWithValue("userName", (object?)criterio.UserName ?? DBNull.Value);
                comando.Parameters.AddWithValue("topN", criterio.TopN);

                List<ResultadoBusquedaVectorialM> resultados = [];

                await using var reader = await comando.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    resultados.Add(new ResultadoBusquedaVectorialM
                    {
                        DocumentoId = reader.GetInt64(0),
                        Modulo = reader.GetString(1),
                        TipoDocumento = reader.GetString(2),
                        ReferenciaId = reader.GetString(3),
                        Contenido = reader.GetString(4),
                        Similitud = reader.GetDouble(5)
                    });
                }

                return new ApiResponseModel<List<ResultadoBusquedaVectorialM>>(resultados, configuration)
                {
                    Status = true,
                    Message = "Operacion exitosa"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponseModel<List<ResultadoBusquedaVectorialM>>(new List<ResultadoBusquedaVectorialM>(), configuration)
                {
                    Status = false,
                    Message = "Error en la base de datos.",
                    Error = ex.Message,
                    ErrorCode = "1"
                };
            }
        }
    }
}
