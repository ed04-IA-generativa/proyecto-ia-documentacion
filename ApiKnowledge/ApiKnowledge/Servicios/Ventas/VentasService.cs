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

            // La vista ya trae su propio filtro base (Tipo_Documento = 3, Fecha_Documento
            // >= '20260102', Estado_Documento = 1, etc.). Aqui solo se agregan filtros
            // adicionales, todos opcionales via "@parametro IS NULL OR columna = @parametro".
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

            var parameters = new SqlParameter[]
            {
                new("@fechaInicio", SqlDbType.Date) { Value = (object?)criterio.FechaInicio ?? DBNull.Value },
                new("@fechaFin", SqlDbType.Date) { Value = (object?)criterio.FechaFin ?? DBNull.Value },
                new("@cliente", SqlDbType.VarChar, 100) { Value = (object?)criterio.Cliente ?? DBNull.Value },
                new("@producto", SqlDbType.VarChar, 100) { Value = (object?)criterio.Producto ?? DBNull.Value },
                new("@tipoCanal", SqlDbType.VarChar, 50) { Value = (object?)criterio.TipoCanal ?? DBNull.Value },
                new("@topN", SqlDbType.Int) { Value = criterio.TopN }
            };

            return await ExecuteQueryAsync(vista, sql, VentaM.MapToModel, parameters);
        }
    }
}
