using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Conocimiento;
using ApiBusiness.Models.Response.Conocimiento;

namespace ApiBusiness.Servicios.Conocimiento
{
    /* =============================================================================
       Contrato de almacenamiento/busqueda vectorial, independiente del motor.
       Hoy lo implementa SqlServerVectorStore; el dia que se agregue Qdrant u otro
       motor, esa implementacion nueva se registra en Program.cs y nada mas del
       proyecto necesita cambiar.
       ============================================================================= */
    public interface IVectorStore
    {
        Task<ApiResponseModel<string>> UpsertarAsync(DocumentoVectorialM documento);

        Task<ApiResponseModel<List<ResultadoBusquedaVectorialM>>> BuscarAsync(CriterioBusquedaVectorialM criterio);
    }
}
