using ApiBusiness.Model.Shared;
using System.Data;
using System.Data.SqlClient;

namespace ApiBusiness.Utilidades
{
    public class StoredProcedureExecutor(IConfiguration configuration)
     {
        private readonly string _connectionString = configuration.GetConnectionString("ConnectionString") ?? "";
        private readonly IConfiguration _configuration = configuration;

        //Tiempo de espera para ExecuteStoredProcedureMultiAsync. La base es la de ADO.NET (30s);
        //los servicios con procedimientos pesados la sobreescriben, por ejemplo la migracion de productos.
        //Los dos metodos de abajo conservan el tiempo que ya tenian para no alterar a los modulos existentes.
        protected virtual int CommandTimeoutSegundos => 30;

        //Ancho maximo de un valor de texto en el eco de parametros. Por encima de esto se trunca:
        //este diccionario es solo para depuracion (ver CLAUDE.md), nadie en la API lo vuelve a leer.
        private const int AnchoMaximoEcoTexto = 500;

        //Convierte los SqlParameter en un formato legible para el eco de la respuesta: Nombre parametro, valor.
        //Los parametros de tipo tabla (TVP) no se copian tal cual: su valor es un DataTable que puede traer
        //miles de filas y que ademas revienta al serializar por el ciclo DataTable -> DataRow -> Table.
        //De esos solo se deja el tipo y la cantidad de filas.
        private static Dictionary<string, object> FormatearParametros(SqlParameter[] parameters)
        {
            Dictionary<string, object> formateados = new();

            foreach (SqlParameter parametro in parameters)
            {
                if (parametro.SqlDbType == SqlDbType.Structured)
                {
                    int filas = parametro.Value is DataTable tabla ? tabla.Rows.Count : 0;
                    formateados[parametro.ParameterName] = $"TVP {parametro.TypeName} ({filas} filas)";
                    continue;
                }

                formateados[parametro.ParameterName] = FormatearValorEco(parametro.Value);
            }

            return formateados;
        }

        //Un nvarchar(max)/varchar(max) con un JSON grande (catalogos, actualizaciones masivas, etc.)
        //no aporta nada legible en el eco y solo infla la respuesta. Se deja una muestra y el tamano real.
        private static object FormatearValorEco(object? valor)
        {
            if (valor is string texto && texto.Length > AnchoMaximoEcoTexto)
            {
                double kiloBytes = System.Text.Encoding.UTF8.GetByteCount(texto) / 1024.0;
                return $"{texto[..AnchoMaximoEcoTexto]}... ({texto.Length} caracteres, {kiloBytes:0.0} KB total)";
            }

            return valor!;
        }

        //Consumo generico de procedimientos con tabla de respuesta
        //
        //commandTimeoutSegundos es opcional y no cambia nada para quien no lo manda: el default
        //sigue siendo 180, exactamente el mismo valor que tenia este metodo antes. Se agrego
        //porque paMigracionPublicarLote necesitaba mas tiempo (migraciones grandes pueden tardar
        //varios minutos) y este metodo -a diferencia de ExecuteStoredProcedureMultiAsync- no leia
        //CommandTimeoutSegundos: estaba fijo en 180 sin importar lo que el servicio sobreescribiera.
        //Se prefirio un parametro opcional a leer la propiedad virtual aqui, para que el cambio de
        //timeout sea explicito por llamada y no afecte por accidente a los demas modulos que usan
        //este mismo metodo compartido.
        protected async Task<ApiResponseModel<List<T>>> ExecuteStoredProcedureAsync<T>(
            string procedureName, //Procedsimiento que se sua
            Func<SqlDataReader, T> mapFunction, //Respuesta que debe retornar
            params SqlParameter[] parameters //parametros para el procedimeito
            )
        {
            return await ExecuteStoredProcedureAsync(procedureName, mapFunction, null, parameters);
        }

        protected async Task<ApiResponseModel<List<T>>> ExecuteStoredProcedureAsync<T>(
            string procedureName,
            Func<SqlDataReader, T> mapFunction,
            int? commandTimeoutSegundos, //null = 180, el mismo comportamiento de siempre
            params SqlParameter[] parameters
            )
        {
            //convertir Sql Parameters en un formato legible: Nombre parametro, valor
            var formattedParameters = FormatearParametros(parameters);

            try
            {
                //Instancia para la conexion
                using SqlConnection sql = new(_connectionString);

                //Instancia para el comando sql
                using SqlCommand cmd = new(procedureName, sql);

                //Tipo de commando
                cmd.CommandType = CommandType.StoredProcedure;

                //Aumentamos el Tiempo de respuesta - Default era de 30
                cmd.CommandTimeout = commandTimeoutSegundos ?? 180;

                //Agregar parametros
                cmd.Parameters.AddRange(parameters);

                //abrir conexion
                await sql.OpenAsync();

                //ejecutar procedimiento
                using var reader = await cmd.ExecuteReaderAsync();

                //lista para almacenar la tabla
                List<T> response = new();

                //Recorrer cada registro obtenido
                while (await reader.ReadAsync())
                {
                    //agregar objeto mapeado
                    response.Add(mapFunction(reader));
                }

                //respuesta
                return new ApiResponseModel<List<T>>(response, _configuration)
                {
                    Parameters = formattedParameters, //parametros
                    Status = true, //estado 
                    StoredProcedure = procedureName, //nompre pa si aplica
                    Message = "Operacion exitosa" //mensaje si aplica
                };

            }
            //control de errores de sql
            catch (SqlException ex)
            {
                return new ApiResponseModel<List<T>>(new List<T>(), _configuration)
                {
                    Parameters = formattedParameters,
                    Error = ex.Message,
                    ErrorCode = $"1-{ex.Number}", // Prefijo 1 indica error SQL
                    Status = false,
                    StoredProcedure = procedureName,
                    Message = "Error en la base de datos."
                };
            }
            //control de errores por ejecucion del codigo de la API, como errores de mapeo o de logica
            catch (Exception e)
            {
                //respuesta
                return new ApiResponseModel<List<T>>(new List<T>(), _configuration)
                {
                    Parameters = formattedParameters, //parametros si aplica
                    Error = e.Message, //descripcion del error
                    Status = false, //estado 
                    StoredProcedure = procedureName, //nombre del pa si aplica
                    Message = "Error en la lógica de la API.", //mensaje si aplica
                    ErrorCode = "2" // Prefijo 2 indica error en la API
                };


            }

        }

        protected async Task<ApiResponseModel<string>> ExecuteStoredProcedureNonQueryAsync(
            string procedureName,
            params SqlParameter[] parameters
        )
        {
            var formattedParameters = FormatearParametros(parameters);

            try
            {
                using SqlConnection sql = new(_connectionString);
                using SqlCommand cmd = new(procedureName, sql);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddRange(parameters);

                await sql.OpenAsync();

                int rowsAffected = await cmd.ExecuteNonQueryAsync();

                return new ApiResponseModel<string>(
                    "Operación exitosa",
                    _configuration)
                {
                    Parameters = formattedParameters,
                    Status = true,
                    StoredProcedure = procedureName,
                    Message = "Operacion exitosa" //mensaje si aplica
                };
            }
            //control de errores de sql
            catch (SqlException ex)
            {
                return new ApiResponseModel<string>(string.Empty, _configuration)
                {
                    Parameters = formattedParameters,
                    Error = ex.Message,
                    ErrorCode = $"1-{ex.Number}", // Prefijo 1 indica error SQL
                    Status = false,
                    StoredProcedure = procedureName,
                    Message = "Error en la base de datos."
                };
            }
            //control de errores por ejecucion del codigo de la API, como errores de mapeo o de logica
            catch (Exception e)
            {
                //respuesta
                return new ApiResponseModel<string>(string.Empty, _configuration)
                {
                    Parameters = formattedParameters, //parametros si aplica
                    Error = e.Message, //descripcion del error
                    Status = false, //estado 
                    StoredProcedure = procedureName, //nombre del pa si aplica
                    Message = "Error en la lógica de la API.", //mensaje si aplica
                    ErrorCode = "2" // Prefijo 2 indica error en la API
                };


            }
        }

        //Consumo generico de procedimientos que devuelven varios conjuntos de resultados.
        //El mapFunction recibe el reader parado en el primer conjunto y va armando el modelo
        //compuesto conjunto por conjunto con LeerConjuntoAsync.
        protected async Task<ApiResponseModel<TResult>> ExecuteStoredProcedureMultiAsync<TResult>(
            string procedureName, //Procedimiento que se usa
            Func<SqlDataReader, Task<TResult>> mapFunction, //Armado del modelo compuesto
            params SqlParameter[] parameters //parametros para el procedimiento
            ) where TResult : new() //Para poder devolver un modelo vacio, y no nulo, cuando hay error
        {
            //convertir Sql Parameters en un formato legible: Nombre parametro, valor
            var formattedParameters = FormatearParametros(parameters);

            try
            {
                //Instancia para la conexion
                using SqlConnection sql = new(_connectionString);

                //Instancia para el comando sql
                using SqlCommand cmd = new(procedureName, sql);

                //Tipo de commando
                cmd.CommandType = CommandType.StoredProcedure;

                //Tiempo de respuesta, ajustable por el servicio que hereda
                cmd.CommandTimeout = CommandTimeoutSegundos;

                //Agregar parametros
                cmd.Parameters.AddRange(parameters);

                //abrir conexion
                await sql.OpenAsync();

                //ejecutar procedimiento
                using var reader = await cmd.ExecuteReaderAsync();

                //El mapeo va dentro del try a proposito: si el procedimiento lanza RAISERROR despues
                //de haber devuelto filas, la SqlException salta en la lectura y no en ExecuteReaderAsync.
                TResult response = await mapFunction(reader);

                //respuesta
                return new ApiResponseModel<TResult>(response, _configuration)
                {
                    Parameters = formattedParameters, //parametros
                    Status = true, //estado
                    StoredProcedure = procedureName, //nombre del pa si aplica
                    Message = "Operacion exitosa" //mensaje si aplica
                };
            }
            //control de errores de sql
            catch (SqlException ex)
            {
                return new ApiResponseModel<TResult>(new TResult(), _configuration)
                {
                    Parameters = formattedParameters,
                    Error = ex.Message,
                    ErrorCode = $"1-{ex.Number}", // Prefijo 1 indica error SQL
                    Status = false,
                    StoredProcedure = procedureName,
                    Message = "Error en la base de datos."
                };
            }
            //control de errores por ejecucion del codigo de la API, como errores de mapeo o de logica
            catch (Exception e)
            {
                //respuesta
                return new ApiResponseModel<TResult>(new TResult(), _configuration)
                {
                    Parameters = formattedParameters, //parametros si aplica
                    Error = e.Message, //descripcion del error
                    Status = false, //estado
                    StoredProcedure = procedureName, //nombre del pa si aplica
                    Message = "Error en la lógica de la API.", //mensaje si aplica
                    ErrorCode = "2" // Prefijo 2 indica error en la API
                };
            }
        }

        //Lee por completo el conjunto de resultados en el que esta parado el reader y avanza al siguiente.
        //Se llama una vez por cada conjunto que devuelve el procedimiento, en el mismo orden del SELECT.
        //Ojo: en estos mapeos hay que usar GetValueOrDefault y no GetBulkValue. GetBulkValue cachea el
        //ordinal con el hash del reader, que no cambia entre conjuntos, asi que una columna con el mismo
        //nombre en dos conjuntos distintos leeria la posicion equivocada.
        protected static async Task<List<T>> LeerConjuntoAsync<T>(
            SqlDataReader reader, //Lector en curso
            Func<SqlDataReader, T> mapFunction //Respuesta que debe retornar
            )
        {
            //lista para almacenar la tabla
            List<T> conjunto = new();

            //Recorrer cada registro del conjunto actual
            while (await reader.ReadAsync())
            {
                //agregar objeto mapeado
                conjunto.Add(mapFunction(reader));
            }

            //Pasar al siguiente conjunto. En el ultimo devuelve false y no hace nada.
            await reader.NextResultAsync();

            return conjunto;
        }
    }
}
