using ApiBusiness.Utilidades;
using System.Data.SqlClient;

namespace ApiBusiness.Models.Response.Shared
{
    public class PaBscUser2M
    {
        public bool Continuar { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Token { get; set; }

        public static PaBscUser2M MapToModel(SqlDataReader reader)
        {
            return new PaBscUser2M()
            {
                Continuar = reader.GetValueOrDefault<bool>("Continuar"),
                Mensaje = reader.GetValueOrDefault<string>("Mensaje") ?? string.Empty,
                UserName = reader.GetValueOrDefault<string>("UserName") ?? string.Empty,
                Email = reader.GetValueOrDefault<string?>("Email")
            };
        }
    }
}
