using Microsoft.AspNetCore.Hosting;

namespace GetThereAPI.Infrastructure;

public class WebRootIconFileStore : IIconFileStore
{
    private readonly string _imagesPath;

    public WebRootIconFileStore(IWebHostEnvironment env)
    {
        _imagesPath = Path.Combine(env.WebRootPath, "images");
    }

    public bool Exists(string filename)
        => File.Exists(Path.Combine(_imagesPath, filename));
}
