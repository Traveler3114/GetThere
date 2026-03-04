namespace GetThere.Pages;

public partial class MapPage : ContentPage
{
	public MapPage()
	{
		InitializeComponent();

        var htmlSource = new UrlWebViewSource
        {
            Url = "map.html"
        };

        MapWebView.Source = htmlSource;
    }
}