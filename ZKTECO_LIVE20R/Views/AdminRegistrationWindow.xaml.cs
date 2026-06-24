using System;
using System.Windows;
using ZKTECO_LIVE20R.Services;

namespace ZKTECO_LIVE20R.Views
{
    public partial class AdminRegistrationWindow : Window
    {
        private readonly ZkLive20RService _zkService;
        private readonly IAttendanceRepository _repo;
        private byte[]? _registeredTemplate;

        public AdminRegistrationWindow(ZkLive20RService zkService, IAttendanceRepository repo)
        {
            InitializeComponent();
            _zkService = zkService;
            _repo = repo;
            _zkService.FingerprintCaptured += OnFingerprintCaptured;
        }

        private void OnFingerprintCaptured(byte[] template)
        {
            _registeredTemplate = template;
            Dispatcher.Invoke(() => TxtStatus.Text = "✓ Отпечаток успешно захвачен!");
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtName.Text) || _registeredTemplate == null)
            {
                MessageBox.Show("Заполните имя и сканируйте палец!"); return;
            }
            bool ok = await _repo.RegisterEmployeeAsync(TxtName.Text, TxtPosition.Text, _registeredTemplate);
            if (ok)
            {
                MessageBox.Show("Успешно сохранено!");
                this.Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _zkService.FingerprintCaptured -= OnFingerprintCaptured; 
            base.OnClosed(e);
        }
    }
}