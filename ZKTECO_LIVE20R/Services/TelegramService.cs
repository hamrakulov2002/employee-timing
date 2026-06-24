using System;
using System.Threading.Tasks;
using Telegram.Bot; // Появится после установки NuGet

namespace ZKTECO_LIVE20R.Services
{
    public static class TelegramService
    {
        private const string BotToken = "8826911309:AAEiPiBCzApzEL-zMgB9oPQ0oZ2NULETOFo";
        private const string ChatId = "613230873";

        private static readonly TelegramBotClient? _botClient;

        static TelegramService()
        {
            if (!string.IsNullOrWhiteSpace(BotToken))
            {
                // В версии 22.x инициализация выглядит именно так
                _botClient = new TelegramBotClient(BotToken);
            }
        }

        /// <summary>
        /// Отправка уведомлений из любого места программы.
        /// Вызывается просто: await TelegramService.SendNotificationAsync("текст");
        /// </summary>
        public static async Task SendNotificationAsync(string message)
        {
            if (_botClient == null || string.IsNullOrWhiteSpace(ChatId))
            {
                System.Diagnostics.Debug.WriteLine("Telegram Бот: Клиент или ChatId не настроены.");
                return;
            }

            try
            {
                // В версии 22.x ChatId передается через структуру ChatId, которая умеет неявно парсить long или string
                long parsedChatId = long.Parse(ChatId);

                // Новый синтаксис отправки в Telegram.Bot v22
                await _botClient.SendMessage(
                    chatId: parsedChatId,
                    text: message,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
            }
            catch (Exception ex)
            {
                // Если пропал интернет — приложение продолжит работать, ошибка уйдет в дебаг
                System.Diagnostics.Debug.WriteLine($"Ошибка отправки сообщения в Telegram: {ex.Message}");
            }
        }
    }
}