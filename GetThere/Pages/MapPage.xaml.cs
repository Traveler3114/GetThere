using System.Diagnostics;

namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
	public MapPage()
	{
		InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Trace.WriteLine("Function abatu be executed");
        await GetLocationAndUpdateMap();
        Trace.WriteLine("Function executed");
    }





    private async Task GetLocationAndUpdateMap()
    {
        try
        {
            LoadingOverlay.IsVisible = true;

            await Task.Delay(500); // Making sure the MapTiler map is fully loaded before trying to update the location
                                   // If GetLocationAndUpdateMap() function is called before the map is fully loaded, it wont work

            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (status == PermissionStatus.Granted)
            {
                var location = await Geolocation.GetLocationAsync(new GeolocationRequest { DesiredAccuracy = GeolocationAccuracy.Best, Timeout = TimeSpan.FromSeconds(10) });

                if (location == null)
                {
                    Trace.WriteLine("Location is null");
                    LoadingOverlay.IsVisible = false;
                    return;
                }

                var script = $"updateMapLocation({location.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {location.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});";
                await MapWebView.EvaluateJavaScriptAsync(script);
                Trace.WriteLine($"Map updated to: {location.Longitude}, {location.Latitude}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error getting location: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.IsVisible = false;
        }
    }

}