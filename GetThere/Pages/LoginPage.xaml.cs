using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace GetThere.Pages;

public partial class LoginPage : ContentPage
{
	public LoginPage()
	{
		InitializeComponent();
	}

	private void ShowPasswordCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
	{
		PasswordEntry.IsPassword = !e.Value;
	}

	private async void LoginButton_Clicked(object sender, EventArgs e)
	{
		ErrorLabel.IsVisible = false;

		string email = EmailEntry.Text?.Trim();
		string password = PasswordEntry.Text ?? string.Empty;

		if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
		{
			ShowError("Please enter a valid email address.");
			return;
		}

		if (password.Length < 6)
		{
			ShowError("Password must be at least 6 characters.");
			return;
		}

		BusyIndicator.IsVisible = true;
		BusyIndicator.IsRunning = true;
		LoginButton.IsEnabled = false;

		try
		{
			await Task.Delay(900);
			// TODO: Authenticate with backend
			await DisplayAlert("Success", "Logged in successfully.", "OK");
			// Navigate to main page or shell
		}
		catch (Exception ex)
		{
			ShowError("Login failed. " + ex.Message);
		}
		finally
		{
			BusyIndicator.IsRunning = false;
			BusyIndicator.IsVisible = false;
			LoginButton.IsEnabled = true;
		}
	}

	private async void RegisterButton_Clicked(object sender, EventArgs e)
	{
		await Navigation.PushAsync(new RegistrationPage());
	}

	private void ShowError(string message)
	{
		ErrorLabel.Text = message;
		ErrorLabel.IsVisible = true;
	}

	private static bool IsValidEmail(string email)
	{
		try
		{
			var addr = new System.Net.Mail.MailAddress(email);
			return addr.Address == email;
		}
		catch
		{
			return false;
		}
	}
}
