using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

        private string _statusMessage = "Сканер готов. Приложите палец.";
        private string _fullNameInput = string.Empty;
        private string _positionInput = string.Empty;
        private byte[]? _lastCapturedTemplate;

        // Максимальная длина смены. Если с прихода прошло больше, считаем, что уход забыли.
        // 12-часовые ночные смены -> 16; суточные -> 30.
        private static readonly TimeSpan MaxShiftDuration = TimeSpan.FromHours(16);

        // Защита от двойного касания сканера
        private static readonly TimeSpan DoubleTapGuard = TimeSpan.FromSeconds(60);

        // Через сколько секунд сбрасывать цветную панель результата
        private const int ResultPanelSeconds = 8;

        public ObservableCollection<string> LiveLogs { get; set; } = new ObservableCollection<string>();

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ===================== Панель результата последней отметки =====================

        private static Brush MakeBrush(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private static readonly Brush IdleBrush = MakeBrush(0x22, 0x34, 0x4A); // серо-синий
        private static readonly Brush InBrush = MakeBrush(0x27, 0xAE, 0x60); // зелёный
        private static readonly Brush OutBrush = MakeBrush(0xE7, 0x4C, 0x3C); // красный
        private static readonly Brush WarnBrush = MakeBrush(0xF3, 0x9C, 0x12); // оранжевый

        private string _lastActionTitle = "—";
        public string LastActionTitle
        {
            get => _lastActionTitle;
            set => SetProperty(ref _lastActionTitle, value);
        }

        private string _lastActionName = "Ожидание отпечатка";
        public string LastActionName
        {
            get => _lastActionName;
            set => SetProperty(ref _lastActionName, value);
        }

        private Brush _lastActionBrush = IdleBrush;
        public Brush LastActionBrush
        {
            get => _lastActionBrush;
            set => SetProperty(ref _lastActionBrush, value);
        }

        private CancellationTokenSource? _resetCts;

        // Вызывать только из UI-потока (внутри Dispatcher.Invoke)
        private async void ShowLastAction(string title, string name, Brush brush)
        {
            LastActionTitle = title;
            LastActionName = name;
            LastActionBrush = brush;

            _resetCts?.Cancel();
            _resetCts = new CancellationTokenSource();
            var token = _resetCts.Token;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(ResultPanelSeconds), token);
                LastActionTitle = "—";
                LastActionName = "Ожидание отпечатка";
                LastActionBrush = IdleBrush;
            }
            catch (TaskCanceledException) { }
        }

        // ===================== Команды =====================

        public ICommand OpenAdminPanelCommand { get; }

        public MainViewModel()
        {
            _repository = new AttendanceRepository();
            _zkService = new ZkLive20RService();

            TelegramService.StartListening();

            OpenAdminPanelCommand = new RelayCommand(OpenAdminPanel);

            _zkService.StatusChanged += (msg) => StatusMessage = msg;
            _zkService.FingerprintCaptured += OnFingerprintCaptured;

            TelegramService.OnOpenRegistrationRequested += (fullName, position) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var adminWin = new Views.AdminRegistrationWindow(_zkService, _repository, fullName, position);
                    adminWin.ShowDialog();
                }));
            };

            _zkService.Initialize();
        }

        // ===================== Обработка отпечатка =====================

        private async void OnFingerprintCaptured(byte[] capturedTemplate)
        {
            try
            {
                if (capturedTemplate == null || capturedTemplate.Length == 0) return;

                // 1. Проверяем, открыто ли окно регистрации прямо сейчас
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

                // Если открыто окно регистрации — MainViewModel НЕ должен обрабатывать приход/уход,
                // чтобы отпечаток полностью ушел в AdminRegistrationWindow.
                if (isAdminWindowOpen)
                {
                    return;
                }

                _lastCapturedTemplate = capturedTemplate;
                StatusMessage = "Проверка отпечатка...";

                var allFingerprints = await _repository.GetAllFingerprintsAsync();
                var matched = allFingerprints.Find(f => _zkService.MatchTemplates(f.TemplateData, capturedTemplate));

                if (matched == null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Пользователь не найден!";
                        ShowLastAction("✖ НЕ НАЙДЕН", "Отпечаток отсутствует в базе", WarnBrush);
                        ShowNotificationWindow("", "Ошибка!", "Отпечаток не найден в базе данных.", isSuccess: false);
                    });
                    return;
                }

                using var context = new Data.AppDbContext();

                var now = DateTime.UtcNow;

                // 2. Последняя отметка сотрудника за всё время
                var lastLog = await context.AttendanceLogs
                    .Where(l => l.EmployeeId == matched.EmployeeId)
                    .OrderByDescending(l => l.Timestamp)
                    .FirstOrDefaultAsync();

                // 3. Двойное касание: игнорируем
                if (lastLog != null && now - lastLog.Timestamp < DoubleTapGuard)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"{matched.Employee.FullName}: отметка уже принята.";
                    });
                    return;
                }

                // 4. Есть ли у сотрудника незакрытая смена
                bool hasOpenShift = lastLog != null
                                    && lastLog.IsCheckIn
                                    && (now - lastLog.Timestamp) <= MaxShiftDuration;

                // Есть открытая смена -> УХОД, иначе -> ПРИХОД
                bool isCheckIn = !hasOpenShift;

                // 5. Прошлая смена осталась без ухода (приход старше MaxShiftDuration)
                if (isCheckIn && lastLog != null && lastLog.IsCheckIn)
                {
                    var lastLocal = DateTime.SpecifyKind(lastLog.Timestamp, DateTimeKind.Utc).ToLocalTime();
                    _ = TelegramService.SendNotificationAsync(
                        $"❗ <b>[{matched.Employee.Position}] {matched.Employee.FullName}</b>: " +
                        $"предыдущая смена с приходом {lastLocal:dd.MM HH:mm} осталась без ухода");
                }

                // 6. Сохраняем отметку
                var log = new AttendanceLog
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = matched.EmployeeId,
                    Timestamp = now,
                    IsCheckIn = isCheckIn
                };
                await context.AttendanceLogs.AddAsync(log);
                await context.SaveChangesAsync();

                string actionText = isCheckIn ? "ПРИХОД зафиксирован" : "УХОД зафиксирован";
                string formattedLabel = $"[{matched.Employee.Position}] {matched.Employee.FullName}";
                string logEntry = $"[{DateTime.Now:dd.MM HH:mm}] {formattedLabel} — {actionText}";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    LiveLogs.Insert(0, logEntry);
                    StatusMessage = $"Успешно: {matched.Employee.FullName}";

                    ShowLastAction(isCheckIn ? "➔ ПРИХОД" : "← УХОД",
                                   matched.Employee.FullName,
                                   isCheckIn ? InBrush : OutBrush);

                    ShowNotificationWindow(matched.Employee.Position, matched.Employee.FullName, actionText, isSuccess: true);
                });

                string tgMessage = $"🔔 <b>{formattedLabel}</b> — {actionText} ({DateTime.Now:dd.MM HH:mm})";
                _ = TelegramService.SendNotificationAsync(tgMessage);
            }
            catch (Exception ex)
            {
                // async void: без try/catch любая ошибка закроет приложение
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = "Ошибка: " + ex.Message;
                    ShowLastAction("⚠ ОШИБКА", ex.Message, WarnBrush);
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