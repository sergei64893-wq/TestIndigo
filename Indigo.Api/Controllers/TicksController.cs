using Indigo.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Indigo.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TicksController : ControllerBase
{
    private readonly ITickRepository _tickRepository;

    public TicksController(
        ITickRepository tickRepository)
    {
        _tickRepository = tickRepository;
    }

    /// <summary>
    /// Получить последние N тиков
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentTicks([FromQuery] int count = 100, [FromQuery] string? source = null)
    {
        var ticks = await _tickRepository.GetRecentAsync(count, source);
        return Ok(new { count = ticks.Count, ticks });
    }

    /// <summary>
    /// Получить тики по источнику
    /// </summary>
    [HttpGet("by-source/{source}")]
    public async Task<IActionResult> GetBySource(string source, [FromQuery] int limit = 100)
    {
        var ticks = await _tickRepository.GetBySourceAsync(source, limit);
        return Ok(new { source, count = ticks.Count, ticks });
    }

    /// <summary>
    /// Получить количество всех тиков в БД
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount()
    {
        var count = await _tickRepository.CountAsync();
        return Ok(new { count });
    }
}
