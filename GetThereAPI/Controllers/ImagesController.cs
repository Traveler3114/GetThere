using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace GetThereAPI.Controllers;

/// <summary>
/// Serves map icon PNG/SVG files that are embedded directly in the assembly.
///
/// GET /operator/images/tram.png
/// GET /operator/images/bus.png
///
/// To add a new icon (e.g. rail.png):
///   1. Add the file anywhere in the GetThereAPI project (e.g. Assets/Images/)
///   2. In its Properties, set Build Action = "Embedded Resource"
///   3. Add one line to STOP_ICON_MAP in index.html
///   No controller changes needed — ever.
///
/// Embedded resource names follow the pattern:
///   {DefaultNamespace}.{FolderPath}.{Filename}
///   e.g. GetThereAPI.Assets.Images.tram.png
/// </summary>
[ApiController]
[Route("operator/images")]
public class ImagesController : ControllerBase
{
    // Namespace prefix for embedded resources.
    // If you put images in Assets/Images/, this becomes "GetThereAPI.Assets.Images."
    // If you put them in the project root, it's just "GetThereAPI."
    private const string ResourcePrefix = "GetThereAPI.Assets.Images.";

    [HttpGet("{filename}")]
    [ResponseCache(Duration = 86400)] // cache 24h — icons never change at runtime
    public IActionResult GetImage(string filename)
    {
        // Sanitise — no path traversal, only images
        if (string.IsNullOrEmpty(filename)
            || filename.Contains('/')
            || filename.Contains('\\')
            || filename.Contains(".."))
            return BadRequest("Invalid filename");

        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext != ".png" && ext != ".svg")
            return BadRequest("Only .png and .svg files are served");

        var resourceName = ResourcePrefix + filename;
        var stream = Assembly.GetExecutingAssembly()
                             .GetManifestResourceStream(resourceName);

        if (stream is null)
            return NotFound($"Icon '{filename}' not found. " +
                $"Expected embedded resource: '{resourceName}'");

        var contentType = ext == ".svg" ? "image/svg+xml" : "image/png";
        return File(stream, contentType);
    }
}