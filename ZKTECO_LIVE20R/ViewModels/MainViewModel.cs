using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ZKTECO_LIVE20R.Infrastructure;
using ZKTECO_LIVE20R.Models;
using ZKTECO_LIVE20R.Services;

namespace ZKTECO_LIVE20R.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ZkLive20RService _zkService;
        private readonly IAttendanceRepository _repository;

        private string _statusMessage = "Сканер готов. Выберите действие и приложите палец.";
        private bool _isCheckInMode = true;
        private string _fullNameInput = string.Empty;
        private string _positionInput = string.Empty;
        private byte[]? _lastCapturedTemplate;

        public ObservableCollection<string> LiveLogs { get; set; } = new ObservableCollection<string>();

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsCheckInMode
        {
            get => _isCheckInMode;
            set { SetProperty(ref _isCheckInMode, value); OnPropertyChanged(nameof(IsCheckOutMode)); }
        }

        public bool IsCheckOutMode => !IsCheckInMode;

        public ICommand SetCheckInModeCommand { get; }
        public ICommand SetCheckOutModeCommand { get; }
        public ICommand OpenAdminPanelCommand { get; }

        public MainViewModel()
        {
            _repository = new AttendanceRepository();
            _zkService = new ZkLive20RService();

            SetCheckInModeCommand = new RelayCommand(() => IsCheckInMode = true);
            SetCheckOutModeCommand = new RelayCommand(() => IsCheckInMode = false);
            OpenAdminPanelCommand = new RelayCommand(OpenAdminPanel);

            _zkService.StatusChanged += (msg) => StatusMessage = msg;
            _zkService.FingerprintCaptured += OnFingerprintCaptured;

            _zkService.Initialize();
        }

        private async void OnFingerprintCaptured(byte[] capturedTemplate)
        {
            _lastCapturedTemplate = capturedTemplate;
            StatusMessage = "Проверка отпечатка...";

            var allFingerprints = await _repository.GetAllFingerprintsAsync();


            var matched = allFingerprints.Find(f => _zkService.MatchTemplates(f.TemplateData, capturedTemplate));


            if (matched != null)
            {
                using var context = new Data.AppDbContext();
                var log = new AttendanceLog
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = matched.EmployeeId,
                    Timestamp = DateTime.UtcNow,
                    IsCheckIn = IsCheckInMode
                };
                await context.AttendanceLogs.AddAsync(log);
                await context.SaveChangesAsync();

                string actionText = IsCheckInMode ? "ПРИХОД зафиксирован" : "УХОД зафиксирован";
                string logEntry = $"[{DateTime.Now:HH:mm}] {matched.Employee.FullName} — {actionText}";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    LiveLogs.Insert(0, logEntry);
                    StatusMessage = $"Успешно: {matched.Employee.FullName}";

                    ShowNotificationWindow(matched.Employee.Position,matched.Employee.FullName, actionText, isSuccess: true);
                });
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "Пользователь не найден!";
                    ShowNotificationWindow("" ,"Ошибка!", "Отпечаток не найден в базе данных.", isSuccess: false);
                });
            }
        }

        private void ShowNotificationWindow(string position, string name, string action, bool isSuccess)
        {
            var notifyWin = new Views.NotificationWindow(position, name, action, isSuccess);
            notifyWin.Show();
        }

        private void OpenAdminPanel()
        {
            var passwordWindow = new Views.PasswordWindow();
            if (passwordWindow.ShowDialog() == true)
            {
                var adminWin = new Views.AdminRegistrationWindow(_zkService, _repository);
                adminWin.ShowDialog();
            }
        }
    }
}