using ApiBusiness.Model.Shared;
using ApiBusiness.Models.Request.Shared;
using ApiBusiness.Models.Response.Shared;
using ApiBusiness.Servicios.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ApiBusiness.Controllers.Shared
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IConfiguration configuration) : ControllerBase
    {
        private readonly AuthService _authService = new(configuration);

        /* =============================================================================
           1. ENDPOINT PARA LOGIN DE USUARIO
           ============================================================================= */
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginM request)
        {
            ApiResponseModel<List<PaBscUser2M>> response = await _authService.PA_bsc_User_2(request);

            if (!response.Status)
                return BadRequest(response);

            PaBscUser2M? usuario = response.Data.FirstOrDefault();

            if (usuario is null || !usuario.Continuar)
                return Unauthorized(response);

            usuario.Token = GenerarToken(request.pUserName);

            return Ok(response);
        }

        private string GenerarToken(string userName)
        {
            Claim[] claims =
            [
                new Claim(JwtRegisteredClaimNames.Sub, configuration["Jwt:Subject"] ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("UserName", userName)
            ];

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
            var signing = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: configuration["Jwt:Issuer"],
                audience: configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(1),
                signingCredentials: signing
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
