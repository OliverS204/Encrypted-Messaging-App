﻿using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using static Encrypted_Messaging_App.Views.Functions;


namespace Encrypted_Messaging_App.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RegisterPage : ContentPage
    {
        IManageFirestoreService FirestoreService = DependencyService.Resolve<IManageFirestoreService>();
        IToastMessage toastMessage = DependencyService.Resolve<IToastMessage>();
        IAuthenticationService AuthenticationService = DependencyService.Resolve<IAuthenticationService>();

        Label invalidEmailIcon = new Label();
        Label invalidPasswordIcon = new Label();
        Label invalidPassword2Icon = new Label();

        // Setup:
        public RegisterPage() {  InitializeComponent();  }
        protected override void OnAppearing()
        {
            Console.WriteLine("~~ Register Page ~~");
            base.OnAppearing();
        }


        private void ChangePasswordVisibility(object sender, EventArgs e) // Make password + confirm password (in)visible
        {
            Label lblClicked = (Label)sender;
            Entry passwordEntry = (Entry)Content.FindByName("PasswordEntry");
            Entry password2Entry = (Entry)Content.FindByName("PasswordConfirmEntry");
            if (lblClicked.Text == "&#xf070;" || lblClicked.Text == "\uf070")
            {
                lblClicked.Text = "\uf06e ";
                passwordEntry.IsPassword = false;
                password2Entry.IsPassword = false;
            }
            else
            {
                lblClicked.Text = "\uf070";
                passwordEntry.IsPassword = true;
                password2Entry.IsPassword = true;
            }
        }
        private async void Login_Tap(object sender, EventArgs e)  // Goto login page
        {
            await Shell.Current.GoToAsync($"//{nameof(LoginPage)}");
        }

        private void EditedEntry(object sender, EventArgs e)  
        {   // Checks all entries are valid and changes icon of the invalid ones to red
            Button RegisterBtn = (Button)Content.FindByName("RegisterButton");
            Entry UsernameEntry = (Entry)Content.FindByName("UsernameEntry");
            Entry EmailEntry = (Entry)Content.FindByName("EmailEntry");
            Entry PasswordEntry = (Entry)Content.FindByName("PasswordEntry");
            Entry Password2Entry = (Entry)Content.FindByName("PasswordConfirmEntry");
            string[] texts = new string[] { UsernameEntry.Text, EmailEntry.Text, PasswordEntry.Text, Password2Entry.Text };
            bool filled = true;
            foreach(string text in texts)
            {
                if (String.IsNullOrEmpty(text)) { filled = false; }
                else if (text.Length == 0) { filled = false; }
            }
            bool emailValid = isValidEmail(EmailEntry.Text);
            bool passwordValid = PasswordEntry.Text == null || PasswordEntry.Text.Length >= 6;
            bool password2Valid = PasswordEntry.Text == null || Password2Entry.Text == null || Password2Entry.Text == PasswordEntry.Text;
            

            if (filled && emailValid && passwordValid && password2Valid) {
                RegisterBtn.BackgroundColor = (Color)App.Current.Resources["Primary"];
                RegisterBtn.IsEnabled = true;
            } else {
                RegisterBtn.IsEnabled = false;
                RegisterBtn.BackgroundColor = Color.FromHex("#FF778899"); //Gray
            }

            // Changing icons colours to red of invalid entries:
            if (!emailValid && !string.IsNullOrEmpty(EmailEntry.Text))
            {
                invalidEmailIcon = EntryInvalid(EmailEntry, invalidEmailIcon, 2);
            } else if (invalidEmailIcon != null)
            {
                invalidEmailIcon = EntryInvalidReset(EmailEntry, invalidEmailIcon);
            }

            if (!passwordValid) { invalidPasswordIcon = EntryInvalid(PasswordEntry, invalidPasswordIcon, 3); }
            else if(invalidPasswordIcon != null) { invalidPasswordIcon = EntryInvalidReset(EmailEntry, invalidPasswordIcon); }

            if (!password2Valid) { invalidPassword2Icon = EntryInvalid(Password2Entry, invalidPassword2Icon, 4); }
            else if (invalidPassword2Icon != null) { invalidPassword2Icon = EntryInvalidReset(EmailEntry, invalidPassword2Icon); }
        }


        // OnEntryFocused:
        private void EntryFocused(string name)
        {
            // Remove invalid icon colour:
            Label Icon = (Label)Content.FindByName($"{name}Icon");
            IconInvalidReset(Icon);

            // Change icon colour to represent selection
            Color selectedColor = (Color)App.Current.Resources["Primary"]; // Primary
            Color defaultColor = Color.FromHex("#000000"); // Black
            if (Icon.TextColor == selectedColor) { Icon.TextColor = defaultColor; }
            else { Icon.TextColor = selectedColor; }
        }
        private void PasswordFocused(object sender, FocusEventArgs e)
        {
            EntryFocused("Password");
        }
        private void UsernameFocused(object sender, FocusEventArgs e)
        {
            EntryFocused("Username");
        }
        private void EmailFocused(object sender, FocusEventArgs e)
        {
            EntryFocused("Email");
        }


        // Register:
        private async void RegisterButton_Clicked(object sender, EventArgs e)
        {
            Entry UsernameEntry = (Entry)Content.FindByName("UsernameEntry");
            Entry EmailEntry = (Entry)Content.FindByName("EmailEntry");
            Entry PasswordEntry = (Entry)Content.FindByName("PasswordEntry");
            Button RegisterBtn = (Button)Content.FindByName("RegisterButton");
            string username = UsernameEntry.Text;
            string email = EmailEntry.Text;
            string password = PasswordEntry.Text;

            // Check if username is already being used
            User userCheck = await FirestoreService.UserFromUsername(username);
            if(userCheck != null)
            {
                toastMessage.ShortAlert($"\'{username}\' isn\'t avaliable");
                Label InvalidIcon = (Label)Content.FindByName($"UsernameIcon");
                IconInvalid(InvalidIcon);
                return;
            }

            // Try to register user:
            (bool success, string message) result = await AuthenticationService.Register(username, email, password);

            if (!result.success)
            {
                string invalidName = result.message;
                if (invalidName != "") {
                    Label InvalidIcon = (Label)Content.FindByName($"{invalidName}Icon");
                    IconInvalid(InvalidIcon);
                }

                RegisterBtn.IsEnabled = false;
                RegisterBtn.BackgroundColor = (Color)App.Current.Resources["Invalid"];
            }
            else
            {   // Add necessary objects to firestore
                (bool success, string message) registerResult = await FirestoreService.InitiliseUser(username);
                if (registerResult.success) {
                    await Shell.Current.GoToAsync($"//{nameof(LoadingPage)}");
                } else {
                    toastMessage.LongAlert($"Unable to register user");
                    Console.WriteLine($"Unable to register: {registerResult.message}");
                }
                
            }
        }
    }
}