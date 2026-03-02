namespace GetThere.Pages;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

        var htmlSource = new UrlWebViewSource
        {
            Url = "map.html"
        };

        MapWebView.Source = htmlSource;
    }


    private async void LoginButton_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//login");
    }
}