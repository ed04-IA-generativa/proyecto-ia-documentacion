using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Conocimiento;
using ApiBusiness.Models.Response.Conocimiento;
using ApiBusiness.Servicios.Conocimiento;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiBusiness.Controllers.Conocimiento
{
    [Authorize]
    [Route("api/v1/[controller]")]
    [ApiController]
    public class ConocimientoController(IVectorStore vectorStore) : ControllerBase
    {
        /* =============================================================================
           1. ENDPOINT PARA INDEXAR (UPSERT) UN DOCUMENTO YA VECTORIZADO
           ============================================================================= */
        [HttpPost("documentos")]
        public async Task<IActionResult> Upsertar([FromBody] DocumentoVectorialM documento)
        {
            ApiResponseModel<string> response = await vectorStore.UpsertarAsync(documento);

            if (response.Status)
                return Ok(response);

            return BadRequest(response);
        }

        /* =============================================================================
           2. ENDPOINT PARA BUSQUEDA SEMANTICA (permisos pre-filtrados, luego ranking)
           ============================================================================= */
        [HttpPost("buscar")]
        public async Task<IActionResult> Buscar([FromBody] CriterioBusquedaVectorialM criterio)
        {
            ApiResponseModel<List<ResultadoBusquedaVectorialM>> response = await vectorStore.BuscarAsync(criterio);

            if (response.Status)
                return Ok(response);

            return BadRequest(response);
        }
    }
}
