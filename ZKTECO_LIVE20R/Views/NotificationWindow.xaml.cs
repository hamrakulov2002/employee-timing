using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZKTECO_LIVE20R.Views
{
    public partial class NotificationWindow : Window
    {
        public NotificationWindow(string position, string name, string action, bool isSuccess)
        {
            InitializeComponent();

            TxtPosition.Text = position;
            TxtName.Text = name;
            TxtAction.Text = action.ToUpper();

            if (!isSuccess)
            {
                MainBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
                TxtIcon.Text = "✕";
            }

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2.5);
            timer.Tick += (s, e) => {
                timer.Stop();
                this.Close();
            };
            timer.Start();
        }
    }
}