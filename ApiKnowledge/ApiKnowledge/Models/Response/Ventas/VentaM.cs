using ApiBusiness.Utilidades;
using System.Data.SqlClient;

namespace ApiBusiness.Models.Response.Ventas
{
    public class VentaM
    {
        public DateTime FechaDocumento { get; set; }
        public string Producto { get; set; } = string.Empty;
        public string UM { get; set; } = string.Empty;
        public string Clase { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
        public string? Cliente { get; set; }
        public string Documento { get; set; } = string.Empty;
        public string? Consecutivo { get; set; }
        public string TipoTransaccion { get; set; } = string.Empty;
        public string SerieDocto { get; set; } = string.Empty;
        public decimal? VtaSinIva { get; set; }
        public decimal? Cantidad { get; set; }
        public string? TipoCliente { get; set; }
        public string Localizacion { get; set; } = string.Empty;
        public string TipoCanalDescripcion { get; set; } = string.Empty;
        public int? CuentaCorrentista { get; set; }
        public string? CuentaCta { get; set; }
        public short Bodega { get; set; }
        public string? DocumentoNombre { get; set; }
        public string? GpsLatitud { get; set; }
        public string? GpsLongitud { get; set; }

        public static VentaM MapToModel(SqlDataReader reader) => new()
        {
            FechaDocumento = reader.GetValueOrDefault<DateTime>("Fecha_Documento"),
            Producto = reader.GetValueOrDefault<string>("Producto") ?? string.Empty,
            UM = reader.GetValueOrDefault<string>("UM") ?? string.Empty,
            Clase = reader.GetValueOrDefault<string>("Clase") ?? string.Empty,
            Vendedor = reader.GetValueOrDefault<string>("Vendedor") ?? string.Empty,
            Cliente = reader.GetValueOrDefault<string?>("Cliente"),
            Documento = reader.GetValueOrDefault<string>("Documento") ?? string.Empty,
            Consecutivo = reader.GetValueOrDefault<string?>("Consecutivo"),
            TipoTransaccion = reader.GetValueOrDefault<string>("TipoTransaccion") ?? string.Empty,
            SerieDocto = reader.GetValueOrDefault<string>("SerieDocto") ?? string.Empty,
            VtaSinIva = reader.GetValueOrDefault<decimal?>("VtaSinIva"),
            Cantidad = reader.GetValueOrDefault<decimal?>("Cantidad"),
            TipoCliente = reader.GetValueOrDefault<string?>("TipoCliente"),
            Localizacion = reader.GetValueOrDefault<string>("Localizacion") ?? string.Empty,
            TipoCanalDescripcion = reader.GetValueOrDefault<string>("TipoCanalDescripcion") ?? string.Empty,
            CuentaCorrentista = reader.GetValueOrDefault<int?>("Cuenta_Correntista"),
            CuentaCta = reader.GetValueOrDefault<string?>("Cuenta_Cta"),
            Bodega = reader.GetValueOrDefault<short>("Bodega"),
            DocumentoNombre = reader.GetValueOrDefault<string?>("Documento_Nombre"),
            GpsLatitud = reader.GetValueOrDefault<string?>("GPS_Latitud"),
            GpsLongitud = reader.GetValueOrDefault<string?>("GPS_Longitud")
        };
    }
}
