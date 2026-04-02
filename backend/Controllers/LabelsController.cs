using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;

namespace PriceTracker.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LabelsController(AppDbContext db) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET api/labels
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var labels = await db.Labels
            .Where(l => l.UserId == UserId)
            .Select(l => new { l.Id, l.Name, l.Color })
            .ToListAsync();
        return Ok(labels);
    }

    // POST api/labels
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLabelRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { error = "Label adı boş olamaz." });

        var label = new Label
        {
            Name = name,
            Color = request.Color ?? "#6366f1",
            UserId = UserId
        };

        db.Labels.Add(label);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = label.Id }, new { label.Id, label.Name, label.Color });
    }

    // DELETE api/labels/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == id && l.UserId == UserId);
        if (label == null) return NotFound();

        db.Labels.Remove(label);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CreateLabelRequest(string? Name, string? Color);
