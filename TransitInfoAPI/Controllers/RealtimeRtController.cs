using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace TransitInfoAPI.Controllers;

[ApiController]
[Route("rt")]
public class RealtimeRtController : ControllerBase
{
    private readonly ILogger<RealtimeRtController> _logger;
    private readonly IWebHostEnvironment _env;

    public RealtimeRtController(ILogger<RealtimeRtController> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    [HttpGet("hzpp")]
    public IActionResult GetHzppRealtime()
    {
        var path = Path.Combine(_env.ContentRootPath, "feeds", "hzpp", "HZPP_GTFSRT.pb");

        if (!System.IO.File.Exists(path))
        {
            _logger.LogWarning("HZPP protobuf not found at {Path}", path);
            return NotFound("HZPP realtime data not available yet.");
        }

        return PhysicalFile(path, "application/x-protobuf");
    }
}
