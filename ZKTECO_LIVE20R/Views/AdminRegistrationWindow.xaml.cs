using System;
using System.Threading.Tasks;
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

        private DispatcherTimer? _timeoutTimer;
        private int _secondsRemaining = 30; // Время ожидания (для вызова из Telegram)

        // 1. Основной конструктор (вызывается всегда первично для инициализации компонентов и подписки)
        public AdminRegistrationWindow(ZkLive20RService zkService, IAttendanceRepository repo)
        {
            InitializeComponent();
            _zkService = zkService;
            _repo = repo;

            // Подписываемся на событие сканирования реального отпечатка (работает и для ПК, и для Telegram)
            _zkService.FingerprintCaptured += OnFingerprintCaptured;
        }

        // 2. Второй конструктор для вызова из Telegram / веб-панели (передает данные и включает таймер)
        public AdminRegistrationWindow(ZkLive20RService zkService, IAttendanceRepository repo, string fullName, string position)
            : this(zkService, repo) // Вызывает первый конструктор, чтобы InitializeComponent и подписка точно сработали!
        {
            TxtName.Text = fullName;
            TxtPosition.Text = position;

            // Включаем таймер обратного отсчета только для удаленного вызова
            _timeoutTimer = new DispatcherTimer();
            _timeoutTimer.Interval = TimeSpan.FromSeconds(1);
            _timeoutTimer.Tick += TimeoutTimer_Tick;
            _timeoutTimer.Start();

            UpdateStatusWithTimer();
        }

        private void TimeoutTimer_Tick(object? sender, EventArgs e)
        {
            _secondsRemaining--;

            if (_secondsRemaining <= 0)
            {
                _timeoutTimer?.Stop();
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

        private bool _isSaving = false; // защита от повторных срабатываний (палец лежит на сканере)

        private void OnFingerprintCaptured(byte[] template)
        {
            // Уходим в UI-поток, дальше все обращения к элементам безопасны
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (_isSaving) return;

                    string name = TxtName.Text;
                    string position = TxtPosition.Text;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show("Заполните имя сотрудника!");
                        return;
                    }

                    _timeoutTimer?.Stop();
                    _registeredTemplate = template;

                    if (_timeoutTimer != null)
                    {
                        _isSaving = true;
                        TxtStatus.Text = "✓ Отпечаток захвачен! Сохранение...";

                        bool ok = await _repo.RegisterEmployeeAsync(name, position, template);
                        if (ok)
                        {
                            _ = TelegramService.SendNotificationAsync(
                                $"✅ <b>Сотрудник успешно зарегистрирован!</b>\nИмя: <b>{name}</b>\nДолжность: <b>{position}</b>");
                            Close();
                        }
                        else
                        {
                            _isSaving = false;
                        }
                    }
                    else
                    {
                        TxtStatus.Text = "✓ Отпечаток захвачен! Нажмите 'Сохранить'.";
                    }
                }
                catch (Exception ex)
                {
                    _isSaving = false;
                    _ = TelegramService.SendNotificationAsync(
                        $"❌ <b>Ошибка регистрации!</b>\nОшибка: {ex.Message}");
                    MessageBox.Show("Ошибка: " + ex.Message);
                }
            }));
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