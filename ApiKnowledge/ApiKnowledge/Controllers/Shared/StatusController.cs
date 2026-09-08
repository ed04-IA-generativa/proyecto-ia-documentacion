using ApiBusiness.Model.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ApiBusiness.Controllers.Shared
{
    [Route("api/[controller]")]
    [ApiController]
    public class StatusController(IConfiguration configuration) : ControllerBase
    {
        [HttpGet()]
        public IActionResult StatusApp()
        {
            ApiResponseModel<List<object>> status = new(new List<object>(), configuration)
            {
                Status = true,
                Message = "Ok"
            };
            return Ok(status);
        }
    }
}
