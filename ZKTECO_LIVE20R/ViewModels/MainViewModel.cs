using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.ObjectModel;
using System.Linq; 
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore; 
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

            TelegramService.StartListening();

            SetCheckInModeCommand = new RelayCommand(() => IsCheckInMode = true);
            SetCheckOutModeCommand = new RelayCommand(() => IsCheckInMode = false);
            OpenAdminPanelCommand = new RelayCommand(OpenAdminPanel);

            _zkService.StatusChanged += (msg) => StatusMessage = msg;
            _zkService.FingerprintCaptured += OnFingerprintCaptured;

            TelegramService.OnOpenRegistrationRequested += (fullName, position) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Создаем окно с таймером автозакрытия
                    var adminWin = new Views.AdminRegistrationWindow(_zkService, _repository, fullName, position);
                    adminWin.ShowDialog();
                });
            };


            _zkService.Initialize();
        }

        private async void OnFingerprintCaptured(byte[] capturedTemplate)
        {
            _lastCapturedTemplate = capturedTemplate;
            StatusMessage = "Проверка отпечатка...";
            bool isAdminWindowOpen = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is Views.AdminRegistrationWindow)
                    {
                        isAdminWindowOpen = true;
                        break;
                    }
                }
            });

            if (isAdminWindowOpen)
            {
                return;
            }
            else
            {
                var allFingerprints = await _repository.GetAllFingerprintsAsync();
                var matched = allFingerprints.Find(f => _zkService.MatchTemplates(f.TemplateData, capturedTemplate));

                if (matched != null)
                {
                    using var context = new Data.AppDbContext();

                    var today = DateTime.UtcNow.Date;

                    var lastLogToday = await context.AttendanceLogs
                        .Where(l => l.EmployeeId == matched.EmployeeId && l.Timestamp >= today)
                        .OrderByDescending(l => l.Timestamp)
                        .FirstOrDefaultAsync();

                    if (lastLogToday != null)
                    {
                        if (IsCheckInMode && lastLogToday.IsCheckIn)
                        {
                            string warningText = "Уже на работе!";
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Внимание: {matched.Employee.FullName} уже на работе.";
                                ShowNotificationWindow(matched.Employee.Position, matched.Employee.FullName, warningText, isSuccess: false);
                            });

                            _ = TelegramService.SendNotificationAsync($"⚠️ <b>[{matched.Employee.Position}] {matched.Employee.FullName}</b> повторно нажал ПРИХОД ({DateTime.Now:HH:mm})");
                            return; 
                        }

                        if (!IsCheckInMode && !lastLogToday.IsCheckIn)
                        {
                            string warningText = "Уже ушел!";
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Внимание: {matched.Employee.FullName} уже отметил уход.";
                                ShowNotificationWindow(matched.Employee.Position, matched.Employee.FullName, warningText, isSuccess: false);
                            });

                            _ = TelegramService.SendNotificationAsync($"⚠️ <b>[{matched.Employee.Position}] {matched.Employee.FullName}</b> повторно нажал УХОД ({DateTime.Now:HH:mm})");
                            return;
                        }
                    }
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
                    string formattedLabel = $"[{matched.Employee.Position}] {matched.Employee.FullName}";
                    string logEntry = $"[{DateTime.Now:HH:mm}] {formattedLabel} — {actionText}";

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LiveLogs.Insert(0, logEntry);
                        StatusMessage = $"Успешно: {matched.Employee.FullName}";

                        ShowNotificationWindow(matched.Employee.Position, matched.Employee.FullName, actionText, isSuccess: true);
                    });

                    string tgMessage = $"🔔 <b>{formattedLabel}</b> — {actionText} ({DateTime.Now:HH:mm})";
                    _ = TelegramService.SendNotificationAsync(tgMessage);
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Пользователь не найден!";
                        ShowNotificationWindow("", "Ошибка!", "Отпечаток не найден в базе данных.", isSuccess: false);
                    });
                }
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