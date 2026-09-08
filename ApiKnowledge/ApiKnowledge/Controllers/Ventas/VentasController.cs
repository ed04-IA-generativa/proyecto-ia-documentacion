using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Ventas;
using ApiBusiness.Models.Response.Ventas;
using ApiBusiness.Servicios.Ventas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiBusiness.Controllers.Ventas
{
    [Authorize]
    [Route("api/v1/[controller]")]
    [ApiController]
    public class VentasController(IConfiguration configuration) : ControllerBase
    {
        private readonly VentasService _ventasService = new(configuration);

        /* =============================================================================
           1. ENDPOINT PARA CONSULTAR VENTAS (sobre la vista proyIA.viwConsultaVentas)
           ============================================================================= */
        [HttpPost("consultar")]
        public async Task<IActionResult> Consultar([FromBody] CriterioVentasM criterio)
        {
            ApiResponseModel<List<VentaM>> response = await _ventasService.ConsultarVentas(criterio);

            if (response.Status)
                return Ok(response);

            return BadRequest(response);
        }
    }
}
