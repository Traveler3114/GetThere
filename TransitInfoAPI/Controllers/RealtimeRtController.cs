using Microsoft.AspNetCore.Mvc;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("rt")]
public class RealtimeRtController : ControllerBase
{
    private readonly ILogger<RealtimeRtController> _logger;

    public RealtimeRtController(ILogger<RealtimeRtController> logger)
    {
        _logger = logger;
    }

    [HttpGet("hzpp")]
    public IActionResult GetHzppRealtime()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "feeds", "hzpp", "HZPP_GTFSRT.pb");

        if (!System.IO.File.Exists(path))
        {
            _logger.LogWarning("HZPP protobuf not found at {Path}", path);
            return NotFound("HZPP realtime data not available yet.");
        }

        return PhysicalFile(path, "application/x-protobuf");
    }
}
