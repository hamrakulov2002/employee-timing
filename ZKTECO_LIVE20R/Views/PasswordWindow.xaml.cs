using System.Windows;
using ZKTECO_LIVE20R.Services;
namespace ZKTECO_LIVE20R.Views
{
    public partial class PasswordWindow : Window
    {
        public PasswordWindow() { InitializeComponent(); TxtPassword.Focus(); OtpSend(); }


        private string OtpCode { get; set; } = string.Empty;
        private string OtpSend()
        {
            string random = new Random().Next(1, 10000).ToString("D4");

            OtpCode = random;

            TelegramService.SendNotificationAsync($"OTP: {random} \n Разовый код для входа в админку");

            return random;
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string enteredPassword = TxtPassword.Password;

            if (enteredPassword == OtpCode)
            { 
                this.DialogResult = true;
            }
            else
            {
                MessageBox.Show("Неверный пароль!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}