using ApiBusiness.Utilidades;
using System.Data.SqlClient;

namespace ApiBusiness.Models.Response
{
    public class OperationResultM
    {
        public bool resultado { get; set; }
        public string mensaje { get; set; }

        public static OperationResultM MapToModel(SqlDataReader reader)
        {
            return new OperationResultM()
            {
                resultado = reader.GetValueOrDefault<bool>("Resultado"), //Acceso por nombre
                mensaje = reader.GetValueOrDefault<string>("Mensaje"), //Acceso por nombre
            };
        }


    }
}
