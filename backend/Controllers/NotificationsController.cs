using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;

namespace PriceTracker.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            < 1 => 30,
            > 100 => 100,
            _ => pageSize
        };

        var query = db.NotificationHistories
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.SentAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                body = n.Body,
                sentAt = n.SentAt,
                isSuccess = n.IsSuccess,
                error = n.Error,
                oldPrice = n.OldPrice,
                newPrice = n.NewPrice,
                productId = n.ProductId,
                userProductId = n.UserProductId
            })
            .ToListAsync();

        return Ok(new
        {
            page,
            pageSize,
            total,
            items
        });
    }
}
