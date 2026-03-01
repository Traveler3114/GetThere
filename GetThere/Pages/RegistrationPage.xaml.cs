using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace GetThere.Pages;

public partial class RegistrationPage : ContentPage
{
	public RegistrationPage()
	{
		InitializeComponent();
	}

	private void ShowPasswordCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
	{
		bool show = e.Value;
		PasswordEntry.IsPassword = !show;
		ConfirmPasswordEntry.IsPassword = !show;
	}

	private async void RegisterButton_Clicked(object sender, EventArgs e)
	{
		ErrorLabel.IsVisible = false;

		string fullName = FullNameEntry.Text?.Trim();
		string email = EmailEntry.Text?.Trim();
		string phone = PhoneEntry.Text?.Trim();
		string password = PasswordEntry.Text ?? string.Empty;
		string confirm = ConfirmPasswordEntry.Text ?? string.Empty;

		if (string.IsNullOrWhiteSpace(fullName))
		{
			ShowError("Please enter your full name.");
			return;
		}

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

		if (password != confirm)
		{
			ShowError("Passwords do not match.");
			return;
		}

		// Simulate registration
		BusyIndicator.IsVisible = true;
		BusyIndicator.IsRunning = true;
		RegisterButton.IsEnabled = false;

		try
		{
			await Task.Delay(1200); // Simulate network

			// TODO: call backend API to register user. For now, navigate to login page.
			await DisplayAlert("Success", "Account created successfully.", "OK");
			// Navigate to LoginPage if present
			if (Application.Current?.MainPage != null)
			{
				await Navigation.PushAsync(new LoginPage());
			}
		}
		catch (Exception ex)
		{
			ShowError("Registration failed. " + ex.Message);
		}
		finally
		{
			BusyIndicator.IsRunning = false;
			BusyIndicator.IsVisible = false;
			RegisterButton.IsEnabled = true;
		}
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

	private async void LoginButton_Clicked(object sender, EventArgs e)
	{
		await Navigation.PushAsync(new LoginPage());
	}
}