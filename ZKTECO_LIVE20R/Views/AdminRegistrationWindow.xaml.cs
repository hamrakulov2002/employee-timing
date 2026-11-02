using System;
using System.Windows;
using System.Windows.Threading;
using ZKTECO_LIVE20R.Services;

namespace ZKTECO_LIVE20R.Views
{
    public partial class AdminRegistrationWindow : Window
    {
        private readonly ZkLive20RService _zkService;
        private readonly IAttendanceRepository _repo;
        private byte[]? _registeredTemplate;

        private readonly DispatcherTimer? _timeoutTimer;
        private int _secondsRemaining = 30; // Время ожидания в секундах

        // Конструктор при вызове из Telegram
        public AdminRegistrationWindow(ZkLive20RService zkService, IAttendanceRepository repo, string fullName, string position)
            : this(zkService, repo)
        {
            TxtName.Text = fullName;
            TxtPosition.Text = position;

            // Настраиваем авто-закрытие только когда окно открыто удаленно из Telegram
            _timeoutTimer = new DispatcherTimer();
            _timeoutTimer.Interval = TimeSpan.FromSeconds(1);
            _timeoutTimer.Tick += TimeoutTimer_Tick;
            _timeoutTimer.Start();

            UpdateStatusWithTimer();
        }

        public AdminRegistrationWindow(ZkLive20RService zkService, IAttendanceRepository repo)
        {
            InitializeComponent();
            _zkService = zkService;
            _repo = repo;
            _zkService.FingerprintCaptured += OnFingerprintCaptured;
        }

        private void TimeoutTimer_Tick(object? sender, EventArgs e)
        {
            _secondsRemaining--;

            if (_secondsRemaining <= 0)
            {
                _timeoutTimer?.Stop();

                // Уведомляем директора, что время истекло
                string fullName = TxtName.Text;
                _ = TelegramService.SendNotificationAsync(
                    $"⏱ <b>Время ожидания истекло!</b>\nРегистрация сотрудника <b>{fullName}</b> отменена (30 сек)."
                );

                this.Close();
            }
            else
            {
                UpdateStatusWithTimer();
            }
        }

        private void UpdateStatusWithTimer()
        {
            TxtStatus.Text = $"Приложите палец к сканеру... ({_secondsRemaining} сек.)";
        }

        private async void OnFingerprintCaptured(byte[] template)
        {
            // Остановливаем таймер, так как палец приложили
            _timeoutTimer?.Stop();

            _registeredTemplate = template;
            Dispatcher.Invoke(() => TxtStatus.Text = "✓ Отпечаток захвачен!");

            if (!string.IsNullOrWhiteSpace(TxtName.Text) && !string.IsNullOrWhiteSpace(TxtPosition.Text))
            {
                bool ok = await _repo.RegisterEmployeeAsync(TxtName.Text, TxtPosition.Text, _registeredTemplate);
                if (ok)
                {
                    string fullName = TxtName.Text;
                    string position = TxtPosition.Text;

                    Dispatcher.Invoke(() =>
                    {
                        _ = TelegramService.SendNotificationAsync(
                            $"✅ <b>Сотрудник успешно зарегистрирован!</b>\n👤 {fullName}\n💼 {position}"
                        );
                        this.Close();
                    });
                }
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _timeoutTimer?.Stop();

            if (string.IsNullOrWhiteSpace(TxtName.Text) || _registeredTemplate == null)
            {
                MessageBox.Show("Заполните имя и сканируйте палец!");
                return;
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
            _timeoutTimer?.Stop();
            _zkService.FingerprintCaptured -= OnFingerprintCaptured;
            base.OnClosed(e);
        }
    }
}