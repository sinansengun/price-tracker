using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PriceTracker.Models;

namespace PriceTracker.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<AppUser> userManager,
    IConfiguration config) : ControllerBase
{
    // POST api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var email = request.Email?.Trim().ToLower();
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { error = "E-posta boş olamaz." });

        var user = new AppUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, request.Password ?? string.Empty);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return Ok(new { token = GenerateToken(user) });
    }

    // POST api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email?.Trim().ToLower();
        var user = await userManager.FindByEmailAsync(email ?? string.Empty);

        if (user == null || !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
            return Unauthorized(new { error = "E-posta veya şifre hatalı." });

        return Ok(new { token = GenerateToken(user) });
    }

    // PUT api/auth/device-token
    [HttpPut("device-token")]
    [Authorize]
    public async Task<IActionResult> UpdateDeviceToken([FromBody] DeviceTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Token boş olamaz." });

        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = await userManager.FindByIdAsync(userId ?? string.Empty);
        if (user == null) return Unauthorized();

        user.FcmToken = request.Token;
        await userManager.UpdateAsync(user);
        return Ok();
    }

    private string GenerateToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryDays = int.TryParse(config["Jwt:ExpiryDays"], out var d) ? d : 7;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expiryDays),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record RegisterRequest(string? Email, string? Password);
public record LoginRequest(string? Email, string? Password);
public record DeviceTokenRequest(string? Token);
