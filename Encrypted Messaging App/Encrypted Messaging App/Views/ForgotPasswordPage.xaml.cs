﻿using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using static Encrypted_Messaging_App.Views.Functions;

namespace Encrypted_Messaging_App.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ForgotPasswordPage : ContentPage
    {
        public ForgotPasswordPage() {  InitializeComponent(); }

        private void EmailFocused(object sender, FocusEventArgs e)
        {
            Label EmailIcon = (Label)Content.FindByName("EmailIcon");
            Color newColor = (Color)App.Current.Resources["Primary"];
            Color defaultColor = Color.FromHex("#000000"); // Black
            if (EmailIcon.TextColor == newColor) { EmailIcon.TextColor = defaultColor; }
            else { EmailIcon.TextColor = newColor; }
        }
        private void EditedEntry(object sender, EventArgs e)
        {
            Button LoginBtn = (Button)Content.FindByName("SubmitButton");
            Entry EmailEntry = (Entry)Content.FindByName("EmailEntry");
            bool emailValid = !String.IsNullOrEmpty(EmailEntry.Text) && EmailEntry.Text.Length > 0 && isValidEmail(EmailEntry.Text);

            if (emailValid) {
                LoginBtn.BackgroundColor = (Color)App.Current.Resources["Primary"];
                LoginBtn.IsEnabled = true;
            } else {   
                LoginBtn.BackgroundColor = Color.FromHex("#FF778899"); //Gray
                LoginBtn.IsEnabled = false;
            }
        }

        private async void SubmitButton_Clicked(object sender, EventArgs e)
        {
            // Send Email:
            Entry EmailEntry = (Entry)Content.FindByName("EmailEntry");
            string email = EmailEntry.Text;
            IAuthenticationService AuthenticationService = DependencyService.Resolve<IAuthenticationService>();
            bool result = await AuthenticationService.ForgotPassword(email);
            if (result)
            {
                await Xamarin.Forms.Shell.Current.DisplayAlert("Password Reset", "Password recovery sent, check your email", "OK");
                await Shell.Current.GoToAsync($"//{nameof(LoginPage)}");
            }
            else
            {
                Button LoginBtn = (Button)Content.FindByName("SubmitButton");
                LoginBtn.BackgroundColor = (Color)App.Current.Resources["Invalid"];
            }
        }
    }
}