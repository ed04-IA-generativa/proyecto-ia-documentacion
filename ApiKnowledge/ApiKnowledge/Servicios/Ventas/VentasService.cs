using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Ventas;
using ApiBusiness.Models.Response.Ventas;
using ApiBusiness.Utilidades;
using System.Data;
using System.Data.SqlClient;

namespace ApiBusiness.Servicios.Ventas
{
    public class VentasService(IConfiguration configuration) : ViewQueryExecutor(configuration)
    {
        /* =============================================================================
           1. CONSULTA DE VENTAS SOBRE [proyIA].[viwConsultaVentas]
           ============================================================================= */
        public async Task<ApiResponseModel<List<VentaM>>> ConsultarVentas(CriterioVentasM criterio)
        {
            const string vista = "proyIA.viwConsultaVentas";

            // Si no viene rango de fechas, se usa un default de los ultimos 30 dias.
            // Medido con SET STATISTICS IO: sin ningun limite de fecha, tbl_Documento
            // se escanea casi por completo (~87,000 lecturas logicas + ~88,000
            // read-ahead) para poder ordenar/limitar por Fecha_Documento. Con el rango
            // siempre presente, el filtro deja de ser opcional para esa columna y el
            // indice INX_Fecha_Documento se puede aprovechar de forma directa.
            DateTime fechaInicio = criterio.FechaInicio ?? DateTime.Today.AddDays(-30);
            DateTime fechaFin = criterio.FechaFin ?? DateTime.Now;

            // La vista ya trae su propio filtro base (Tipo_Documento = 3, Fecha_Documento
            // >= '20260102', Estado_Documento = 1, etc.). Cliente/producto/tipoCanal siguen
            // siendo opcionales via "@parametro IS NULL OR columna = @parametro" -- ese
            // patron hace que SQL Server compile un solo plan valido para cualquier
            // combinacion de nulos, lo que suele descartar indices utiles. OPTION (RECOMPILE)
            // fuerza un plan nuevo segun los valores reales de cada llamada.
            const string sql = """
                SELECT Fecha_Documento, Producto, UM, Clase, Vendedor, Cliente, Documento, Consecutivo,
                       TipoTransaccion, SerieDocto, VtaSinIva, Cantidad, TipoCliente, Localizacion,
                       TipoCanalDescripcion, Cuenta_Correntista, Cuenta_Cta, Bodega, Documento_Nombre,
                       GPS_Latitud, GPS_Longitud
                FROM [proyIA].[viwConsultaVentas]
                WHERE Fecha_Documento >= @fechaInicio
                  AND Fecha_Documento <= @fechaFin
                  AND (@cliente IS NULL OR Cliente LIKE '%' + @cliente + '%')
                  AND (@producto IS NULL OR Producto LIKE '%' + @producto + '%')
                  AND (@tipoCanal IS NULL OR TipoCanalDescripcion = @tipoCanal)
                ORDER BY Fecha_Documento DESC
                OFFSET 0 ROWS FETCH NEXT @topN ROWS ONLY
                OPTION (RECOMPILE);
                """;

            var parameters = new SqlParameter[]
            {
                new("@fechaInicio", SqlDbType.DateTime) { Value = fechaInicio },
                new("@fechaFin", SqlDbType.DateTime) { Value = fechaFin },
                new("@cliente", SqlDbType.VarChar, 100) { Value = (object?)criterio.Cliente ?? DBNull.Value },
                new("@producto", SqlDbType.VarChar, 100) { Value = (object?)criterio.Producto ?? DBNull.Value },
                new("@tipoCanal", SqlDbType.VarChar, 50) { Value = (object?)criterio.TipoCanal ?? DBNull.Value },
                new("@topN", SqlDbType.Int) { Value = criterio.TopN }
            };

            return await ExecuteQueryAsync(vista, sql, VentaM.MapToModel, parameters);
        }
    }
}
