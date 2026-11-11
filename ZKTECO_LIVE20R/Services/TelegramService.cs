using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ZKTECO_LIVE20R.Data;

namespace ZKTECO_LIVE20R.Services
{
    public static class TelegramService
    {
        private const string BotToken = "8039879484:AAGTmzmWynYbHVcI0DLzi-pp4qPIZqkTweA";

        // Список чатов всех администраторов, которым будут дублироваться уведомления и отчеты
        private static readonly string[] AllowedChatIds = { "6782484329", "613230873" };

        private static readonly TelegramBotClient? _botClient;
        private static CancellationTokenSource? _cts;

        private static readonly ConcurrentDictionary<long, UserState> _userStates = new();

        public static event Action<string, string>? OnOpenRegistrationRequested;

        private class UserState
        {
            public string Step { get; set; } = "NONE"; // NONE, WAITING_FOR_OTP, WAITING_FOR_NAME, WAITING_FOR_POSITION
            public string ExpectedOtp { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public DateTime OtpExpireTime { get; set; }
        }

        static TelegramService()
        {
            if (!string.IsNullOrWhiteSpace(BotToken))
            {
                _botClient = new TelegramBotClient(BotToken);
            }
        }

        public static void StartListening()
        {
            if (_botClient == null) return;

            _cts = new CancellationTokenSource();

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: (client, ex, ct) =>
                {
                    Console.WriteLine($"[TG ERROR]: {ex.Message}");
                    return Task.CompletedTask;
                },
                cancellationToken: _cts.Token
            );

            Console.WriteLine("[TG БОТ] Фоновое прослушивание успешно запущено.");
        }

        private static ReplyKeyboardMarkup GetMainMenuKeyboard()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "📋 Логи за сегодня", "➕ Добавить сотрудника" },
                new KeyboardButton[] { "👤 По сотрудникам", "📊 Общий отчет за месяц" }
            })
            {
                ResizeKeyboard = true,
                IsPersistent = true
            };
        }

        /// <summary>
        /// Вспомогательный метод для проверки, разрешен ли чат
        /// </summary>
        private static bool IsAllowedChat(long chatId)
        {
            return AllowedChatIds.Contains(chatId.ToString());
        }

        /// <summary>
        /// Вызывается из WPF для отправки OTP кода всем администраторам
        /// </summary>
        public static async Task<string> RequestOtpAsync()
        {
            if (_botClient == null || AllowedChatIds.Length == 0) return string.Empty;

            // Генерируем 4-значный OTP код
            string generatedOtp = new Random().Next(1000, 9999).ToString();
            string message = $"🔑 <b>Одноразовый код доступа (OTP):</b> <code>{generatedOtp}</code>\n\n<i>Код действителен 2 минуты. Введите его в окне на терминале или отправьте боту.</i>";

            // Рассылаем код каждому админу из списка и обновляем его состояние
            foreach (var chatIdStr in AllowedChatIds)
            {
                if (long.TryParse(chatIdStr, out long targetChatId))
                {
                    var state = _userStates.GetOrAdd(targetChatId, _ => new UserState());
                    state.ExpectedOtp = generatedOtp;
                    state.OtpExpireTime = DateTime.Now.AddMinutes(2);
                    state.Step = "WAITING_FOR_OTP";

                    try
                    {
                        await _botClient.SendMessage(
                            chatId: targetChatId,
                            text: message,
                            parseMode: ParseMode.Html,
                            replyMarkup: GetMainMenuKeyboard()
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TG ERROR - RequestOtpAsync to {targetChatId}]: {ex.Message}");
                    }
                }
            }

            return generatedOtp;
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                using var context = new AppDbContext();

                // 1. ОБРАБОТКА ИНЛАЙН-КНОПОК
                if (update.CallbackQuery is { } callbackQuery && !string.IsNullOrEmpty(callbackQuery.Data))
                {
                    long senderChatId = callbackQuery.Message?.Chat.Id ?? 0;
                    if (!IsAllowedChat(senderChatId)) return;

                    if (callbackQuery.Data.StartsWith("emp_"))
                    {
                        string employeeIdStr = callbackQuery.Data.Replace("emp_", "");
                        if (Guid.TryParse(employeeIdStr, out Guid employeeId))
                        {
                            await botClient.AnswerCallbackQuery(callbackQuery.Id, "Загрузка...", cancellationToken: cancellationToken);

                            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                            var endDate = startDate.AddMonths(1);

                            var employee = await context.Employees.FindAsync(new object[] { employeeId }, cancellationToken);
                            var empLogs = await context.AttendanceLogs
                                .Where(l => l.EmployeeId == employeeId && l.Timestamp >= startDate && l.Timestamp < endDate)
                                .OrderBy(l => l.Timestamp)
                                .ToListAsync(cancellationToken);

                            if (employee == null) return;

                            TimeSpan totalDuration = TimeSpan.Zero;
                            for (int i = 0; i < empLogs.Count; i++)
                            {
                                if (empLogs[i].IsCheckIn && i + 1 < empLogs.Count && !empLogs[i + 1].IsCheckIn)
                                {
                                    totalDuration += (empLogs[i + 1].Timestamp - empLogs[i].Timestamp);
                                    i++;
                                }
                            }

                            var resBuilder = new StringBuilder();
                            resBuilder.AppendLine($"👤 <b>ПЕРСОНАЛЬНЫЙ ОТЧЕТ</b>");
                            resBuilder.AppendLine($"───────────────────");
                            resBuilder.AppendLine($"📋 ФИО: <b>{employee.FullName}</b>");
                            resBuilder.AppendLine($"💼 Должность: <i>{employee.Position}</i>");
                            resBuilder.AppendLine($"⏱ Часов за месяц: <b>{(int)totalDuration.TotalHours} ч. {totalDuration.Minutes} мин.</b>");

                            if (callbackQuery.Message != null)
                            {
                                await botClient.SendMessage(
                                    chatId: senderChatId,
                                    text: resBuilder.ToString(),
                                    parseMode: ParseMode.Html,
                                    cancellationToken: cancellationToken
                                );
                            }
                        }
                    }
                    return;
                }

                // 2. ОБРАБОТКА ТЕКСТОВЫХ СООБЩЕНИЙ
                if (update.Message is not { } message || message.Text == null) return;

                string text = message.Text.ToString().Trim();
                if (string.IsNullOrEmpty(text)) return;

                long userId = message.Chat.Id;
                if (!IsAllowedChat(userId)) return;

                var state = _userStates.GetOrAdd(userId, _ => new UserState());

                // === ПРИОРИТЕТ 1: ПРОВЕРКА КНОПОК МЕНЮ И СБРОС СОСТОЯНИЯ ===
                if (text.Equals("/start", StringComparison.OrdinalIgnoreCase) || text.Equals("меню", StringComparison.OrdinalIgnoreCase))
                {
                    state.Step = "NONE";
                    await botClient.SendMessage(userId, "👋 <b>Главное меню:</b>", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                    return;
                }

                if(text.Equals("/test", StringComparison.OrdinalIgnoreCase))
                {
                    state.Step = "NONE";
                    ZkLive20RService _zkService = new ZkLive20RService();
                    _zkService.SimulateCapture(); // Эмуляция захвата отпечатка
                    await botClient.SendMessage(userId, "✅ <b>Тестовое сообщение успешно отправлено.</b>", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                    return;
                }

                if (text.Equals("📋 Логи за сегодня", StringComparison.OrdinalIgnoreCase) || text.Equals("/logs", StringComparison.OrdinalIgnoreCase))
                {
                    state.Step = "NONE";
                    var todayUtcStart = DateTime.Now.Date.ToUniversalTime();
                    var todayLogs = await context.AttendanceLogs
                        .Include(l => l.Employee)
                        .Where(l => l.Timestamp >= todayUtcStart)
                        .OrderBy(l => l.Timestamp)
                        .ToListAsync(cancellationToken);

                    if (!todayLogs.Any())
                    {
                        await botClient.SendMessage(userId, "📋 <i>Сегодня записей нет.</i>", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                        return;
                    }

                    var logsBuilder = new StringBuilder();
                    logsBuilder.AppendLine($"📋 <b>ЛОГИ ЗА СЕГОДНЯ ({DateTime.Now:dd.MM.yyyy})</b>\n───────────────────");

                    foreach (var log in todayLogs)
                    {
                        string actionText = log.IsCheckIn ? "🟢 ПРИХОД" : "🔴 УХОД";
                        logsBuilder.AppendLine($"{actionText} <code>[{log.Timestamp.ToLocalTime():HH:mm}]</code> <b>{log.Employee.FullName}</b>");
                    }

                    await botClient.SendMessage(userId, logsBuilder.ToString(), parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                    return;
                }

                if (text.Equals("➕ Добавить сотрудника", StringComparison.OrdinalIgnoreCase) || text.Equals("/add", StringComparison.OrdinalIgnoreCase))
                {
                    state.Step = "WAITING_FOR_NAME";
                    state.FullName = string.Empty;

                    await botClient.SendMessage(
                        chatId: userId,
                        text: "👤 <b>Введите ФИО нового сотрудника:</b>",
                        parseMode: ParseMode.Html,
                        replyMarkup: new ReplyKeyboardRemove(),
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                if (text.Equals("👤 По сотрудникам", StringComparison.OrdinalIgnoreCase) || text.Equals("/employee", StringComparison.OrdinalIgnoreCase))
                {
                    state.Step = "NONE";
                    var employees = await context.Employees.OrderBy(e => e.FullName).ToListAsync(cancellationToken);

                    if (!employees.Any())
                    {
                        await botClient.SendMessage(userId, "📭 <i>Сотрудники не найдены.</i>", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                        return;
                    }

                    var inlineButtons = employees.Select(emp => new[]
                    {
                        InlineKeyboardButton.WithCallbackData(text: emp.FullName, callbackData: $"emp_{emp.Id}")
                    }).ToArray();

                    await botClient.SendMessage(userId, "🔍 <b>Выберите сотрудника:</b>", parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(inlineButtons), cancellationToken: cancellationToken);
                    return;
                }

                if (text.Equals("📊 Общий отчет за месяц", StringComparison.OrdinalIgnoreCase) || text.Equals("/report", StringComparison.OrdinalIgnoreCase))
                {
                    state.Step = "NONE";
                    var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var endDate = startDate.AddMonths(1);

                    var logs = await context.AttendanceLogs
                        .Include(l => l.Employee)
                        .Where(l => l.Timestamp >= startDate && l.Timestamp < endDate)
                        .ToListAsync(cancellationToken);

                    if (!logs.Any())
                    {
                        await botClient.SendMessage(userId, "📭 <i>Отчетов за месяц нет.</i>", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                        return;
                    }

                    var reportBuilder = new StringBuilder();
                    reportBuilder.AppendLine($"📊 <b>ОТЧЕТ ЗА МЕСЯЦ</b>\n───────────────────");

                    foreach (var group in logs.GroupBy(l => l.EmployeeId))
                    {
                        var emp = group.First().Employee;
                        reportBuilder.AppendLine($"👤 <b>{emp.FullName}</b> ({emp.Position})");
                    }

                    await botClient.SendMessage(userId, reportBuilder.ToString(), parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                    return;
                }

                // === ПРИОРИТЕТ 2: ОБРАБОТКА ВВОДА OTP ИЛИ ПОШАГОВОЙ РЕГИСТРАЦИИ ===

                if (state.Step == "WAITING_FOR_OTP")
                {
                    if (DateTime.Now > state.OtpExpireTime)
                    {
                        state.Step = "NONE";
                        await botClient.SendMessage(userId, "❌ <b>Время действия OTP кода истекло.</b> Запросите новый код.", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                        return;
                    }

                    if (text == state.ExpectedOtp)
                    {
                        state.Step = "NONE";
                        await botClient.SendMessage(userId, "✅ <b>OTP код подтвержден!</b> Доступ разрешен.", parseMode: ParseMode.Html, replyMarkup: GetMainMenuKeyboard(), cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(userId, "⚠️ <b>Неверный OTP код.</b> Попробуйте еще раз или нажмите кнопку в меню.", parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                    }
                    return;
                }

                if (state.Step == "WAITING_FOR_NAME")
                {
                    state.FullName = text;
                    state.Step = "WAITING_FOR_POSITION";

                    await botClient.SendMessage(
                        chatId: userId,
                        text: $"✅ ФИО: <b>{state.FullName}</b>\n\n💼 Теперь введите <b>должность</b>:",
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                if (state.Step == "WAITING_FOR_POSITION")
                {
                    string position = text;
                    string fullName = state.FullName;

                    state.Step = "NONE";

                    OnOpenRegistrationRequested?.Invoke(fullName, position);

                    await botClient.SendMessage(
                        chatId: userId,
                        text: $"⏳ <b>Запрос отправлен на терминал!</b>\n👤 <b>ФИО:</b> {fullName}\n💼 <b>Должность:</b> {position}",
                        parseMode: ParseMode.Html,
                        replyMarkup: GetMainMenuKeyboard(),
                        cancellationToken: cancellationToken
                    );
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TG ERROR]: {ex.Message}");
            }
        }

        /// <summary>
        /// Рассылка обычных уведомлений (например, об успешном приходе/уходе или регистрации) ВСЕМ администраторам из списка
        /// </summary>
        public static async Task SendNotificationAsync(string message)
        {
            if (_botClient == null || AllowedChatIds.Length == 0) return;

            foreach (var chatIdStr in AllowedChatIds)
            {
                if (long.TryParse(chatIdStr, out long targetChatId))
                {
                    try
                    {
                        await _botClient.SendMessage(
                            chatId: targetChatId,
                            text: message,
                            parseMode: ParseMode.Html,
                            replyMarkup: GetMainMenuKeyboard()
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TG ERROR - SendNotificationAsync to {targetChatId}]: {ex.Message}");
                    }
                }
            }
        }

        public static void StopListening()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}