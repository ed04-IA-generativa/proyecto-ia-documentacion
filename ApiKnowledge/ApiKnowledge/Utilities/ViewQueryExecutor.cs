using ApiBusiness.Model.Shared;
using System.Data;
using System.Data.SqlClient;

namespace ApiBusiness.Utilidades
{
    // Analogo a StoredProcedureExecutor, pero para consultas de texto SQL parametrizadas
    // contra vistas. Una vista se consulta con SELECT (CommandType.Text), no con EXEC, asi
    // que no puede reusar CommandType.StoredProcedure de StoredProcedureExecutor.
    public class ViewQueryExecutor(IConfiguration configuration)
    {
        private readonly string _connectionString = configuration.GetConnectionString("ConnectionString") ?? "";
        private readonly IConfiguration _configuration = configuration;

        // objectName es solo el nombre de la vista/tabla que se esta consultando, para
        // depuracion (se refleja en ApiResponseModel.StoredProcedure, igual que el nombre
        // del SP en StoredProcedureExecutor).
        protected async Task<ApiResponseModel<List<T>>> ExecuteQueryAsync<T>(
            string objectName,
            string consultaSql,
            Func<SqlDataReader, T> mapFunction,
            params SqlParameter[] parameters
            )
        {
            var formattedParameters = parameters.ToDictionary(
                p => p.ParameterName,
                p => p.Value ?? (object)DBNull.Value
            );

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
                    Parameters = formattedParameters,
                    Status = true,
                    StoredProcedure = objectName,
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
                    StoredProcedure = objectName,
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
                    StoredProcedure = objectName,
                    Message = "Error en la lógica de la API.",
                    ErrorCode = "2"
                };
            }
        }
    }
}
