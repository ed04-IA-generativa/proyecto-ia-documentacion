using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Shared;
using ApiBusiness.Models.Response.Shared;
using ApiBusiness.Utilidades;
using System.Data;
using System.Data.SqlClient;

namespace ApiBusiness.Servicios.Shared
{
    public class AuthService(IConfiguration configuration) : StoredProcedureExecutor(configuration)
    {
        /* =============================================================================
           1. SERVICIO PARA LOGIN DE USUARIO
           ============================================================================= */
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
}
