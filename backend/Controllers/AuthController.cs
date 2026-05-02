using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
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
    IConfiguration config,
    IWebHostEnvironment env) : ControllerBase
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

    // POST api/auth/google
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirebaseIdToken))
            return BadRequest(new { error = "Google kimlik doğrulama token'ı boş olamaz." });

        if (FirebaseApp.DefaultInstance == null)
            return StatusCode(503, new { error = "Google login şu anda kullanılamıyor." });

        FirebaseToken decodedToken;
        try
        {
            decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(request.FirebaseIdToken);
        }
        catch
        {
            return Unauthorized(new { error = "Google kimliği doğrulanamadı." });
        }

        var email = decodedToken.Claims.TryGetValue("email", out var emailObj)
            ? emailObj?.ToString()?.Trim().ToLower()
            : null;

        if (string.IsNullOrWhiteSpace(email))
            return Unauthorized(new { error = "Google hesabında geçerli e-posta bulunamadı." });

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        return Ok(new { token = GenerateToken(user) });
    }

    // POST api/auth/dev-reset-user
    // Sadece local development'ta, test hesabını şifreyle birlikte sıfırlamak için.
    [HttpPost("dev-reset-user")]
    public async Task<IActionResult> DevResetUser([FromBody] RegisterRequest request)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var email = request.Email?.Trim().ToLower();
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { error = "E-posta boş olamaz." });

        var existing = await userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            var deleteResult = await userManager.DeleteAsync(existing);
            if (!deleteResult.Succeeded)
                return BadRequest(new { errors = deleteResult.Errors.Select(e => e.Description) });
        }

        var user = new AppUser { UserName = email, Email = email };
        var createResult = await userManager.CreateAsync(user, request.Password ?? string.Empty);
        if (!createResult.Succeeded)
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });

        return Ok(new { token = GenerateToken(user), message = "Test hesabı sıfırlandı." });
    }

    // PUT api/auth/device-token
    [HttpPut("device-token")]
    [Authorize]
    public async Task<IActionResult> UpdateDeviceToken([FromBody] DeviceTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Token boş olamaz." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = await userManager.FindByIdAsync(userId ?? string.Empty);
        if (user == null) return Unauthorized();

        user.FcmToken = request.Token;
        await userManager.UpdateAsync(user);
        return Ok();
    }

    // GET api/auth/me
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var hasPassword = await userManager.HasPasswordAsync(user);
        return Ok(new
        {
            email = user.Email,
            createdAt = user.CreatedAt,
            hasPassword
        });
    }

    // PUT api/auth/change-password
    [HttpPut("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword)
            || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Mevcut şifre ve yeni şifre zorunludur." });

        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        if (!await userManager.HasPasswordAsync(user))
            return BadRequest(new { error = "Bu hesap için şifre tanımlı değil." });

        var result = await userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return Ok(new { message = "Şifre güncellendi." });
    }

    private async Task<AppUser?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userId)) return null;
        return await userManager.FindByIdAsync(userId);
    }

    private string GenerateToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = int.TryParse(config["Jwt:ExpiryMinutes"], out var m) ? m : 0;
        var expiryHours = int.TryParse(config["Jwt:ExpiryHours"], out var h) ? h : 0;
        var expiryDays = int.TryParse(config["Jwt:ExpiryDays"], out var d) ? d : 7;

        var tokenLifetime = expiryMinutes > 0
            ? TimeSpan.FromMinutes(expiryMinutes)
            : expiryHours > 0
                ? TimeSpan.FromHours(expiryHours)
                : TimeSpan.FromDays(expiryDays > 0 ? expiryDays : 7);

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
            expires: DateTime.UtcNow.Add(tokenLifetime),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record RegisterRequest(string? Email, string? Password);
public record LoginRequest(string? Email, string? Password);
public record GoogleLoginRequest(string? FirebaseIdToken);
public record DeviceTokenRequest(string? Token);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
