using System.Text.RegularExpressions;
using ChessCrm.Bot.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ChessCrm.Bot.Services;

public class TelegramBotService(
    AppConfig config,
    AiService aiService,
    DatabaseService dbService,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private readonly TelegramBotClient _bot = new(config.BotToken);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: new ReceiverOptions { AllowedUpdates = [] },
            cancellationToken: ct
        );

        var me = await _bot.GetMe(ct);
        logger.LogInformation("Бот @{Username} запущен.", me.Username);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } userQuestion } message)
            return;

        long chatId = message.Chat.Id;
        long userId = message.From?.Id ?? 0;

        // Проверка белого списка (если задан)
        if (config.AllowedUserIds.Count > 0 && !config.AllowedUserIds.Contains(userId))
        {
            logger.LogWarning("Отклонён запрос от неавторизованного пользователя {UserId}", userId);
            await bot.SendMessage(chatId, "⛔ У вас нет доступа к этому боту.", cancellationToken: ct);
            return;
        }

        // Команда /start
        if (userQuestion.StartsWith("/start"))
        {
            await bot.SendMessage(chatId,
                "👋 Привет! Я аналитический помощник шахматной школы.\n\n" +
                "Чем я могу помочь тебе сегодня?",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }

        logger.LogInformation(">>> ВОПРОС от {User} ({Id}): {Q}", message.From?.Username, userId, userQuestion);

        var statusMsg = await bot.SendMessage(chatId,
            "🧠 <b>Анализирую запрос...</b>",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        try
        {
            // --- ЭТАП 1: Генерация SQL с авто-retry ---
            string sql = await aiService.GenerateSqlAsync(userQuestion, ct);
            logger.LogInformation("--- SQL (попытка 1) ---\n{Sql}", sql);

            string jsonResult;
            try
            {
                await Edit(bot, chatId, statusMsg.MessageId, "🔍 <b>Запрашиваю данные из базы...</b>", ct);
                jsonResult = await dbService.ExecuteQueryAsync(sql, ct);
            }
            catch (PostgresException pgEx)
            {
                logger.LogWarning("SQL ошибка, пробуем исправить: {Err}", pgEx.MessageText);

                await Edit(bot, chatId, statusMsg.MessageId, "🔧 <b>Исправляю запрос...</b>", ct);

                sql = await aiService.FixSqlAsync(userQuestion, sql, pgEx.MessageText, ct);
                logger.LogInformation("--- SQL (попытка 2) ---\n{Sql}", sql);

                // Вторая попытка — если снова упадёт, поймает внешний catch
                await Edit(bot, chatId, statusMsg.MessageId, "🔍 <b>Повторный запрос к базе...</b>", ct);
                jsonResult = await dbService.ExecuteQueryAsync(sql, ct);
            }

            // --- ЭТАП 3: Формирование красивого ответа ---
            await Edit(bot, chatId, statusMsg.MessageId,
                "✍️ <b>Формирую отчёт...</b>", ct);

            string finalResponse = await aiService.AnalyzeDataAsync(userQuestion, jsonResult, ct);

            // Конвертируем Markdown от модели в HTML-теги Telegram
            finalResponse = ConvertToTelegramHtml(finalResponse);

            logger.LogInformation("<<< ОТВЕТ:\n{R}", finalResponse);

            await SendWithFallback(bot, chatId, statusMsg.MessageId, finalResponse, ct);
        }
        catch (InvalidOperationException ioEx) // GuardReadOnly или ошибка конфига
        {
            logger.LogWarning(ioEx, "Отказ в выполнении операции");
            await Edit(bot, chatId, statusMsg.MessageId,
                $"⚠️ <b>Отказано:</b>\n{ioEx.Message}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Непредвиденная ошибка");
            await Edit(bot, chatId, statusMsg.MessageId,
                $"❌ <b>Ошибка:</b> {ex.Message}", ct);
        }
    }

    // Отправка с фолбэком на plain text если HTML не прошёл
    private static async Task SendWithFallback(
        ITelegramBotClient bot, long chatId, int msgId, string text, CancellationToken ct)
    {
        try
        {
            await Edit(bot, chatId, msgId, text, ct, ParseMode.Html);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse entities"))
        {
            // Убираем все HTML теги и шлём как plain text
            var clean = Regex.Replace(text, "<.*?>", "");
            await Edit(bot, chatId, msgId, clean, ct);
        }
    }

    private static Task Edit(
        ITelegramBotClient bot, long chatId, int msgId, string text,
        CancellationToken ct, ParseMode parseMode = ParseMode.Html)
    {
        return bot.EditMessageText(
            chatId: chatId,
            messageId: msgId,
            text: text,
            parseMode: parseMode,
            cancellationToken: ct);
    }

    // Конвертация Markdown от LLM → HTML для Telegram
    private static string ConvertToTelegramHtml(string text)
    {
        text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");
        text = Regex.Replace(text, @"__(.*?)__", "<b>$1</b>");
        text = Regex.Replace(text, @"`(.*?)`", "<code>$1</code>");
        // Экранируем угловые скобки, которые не являются HTML-тегами
        // (простой подход: модели редко пишут сырой HTML)
        return text;
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Ошибка Telegram Polling");
        return Task.CompletedTask;
    }
}