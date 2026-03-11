using System.Text.Json;
using System.Text.RegularExpressions;
using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Handlers;
using ChessCrm.Bot.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ChessCrm.Bot.Services;

public class TelegramBotService(
    AppConfig config,
    AiService aiService,
    DatabaseService dbService,
    IntentService intentService,
    PendingActionService pendingActions,
    SubscriptionHandler subscriptionHandler,
    NewStudentHandler newStudentHandler,
    AddToGroupHandler addToGroupHandler,
    EditStudentHandler editStudentHandler,
    RemoveFromGroupHandler removeFromGroupHandler,
    TransferGroupHandler transferGroupHandler,
    InviteHandler inviteHandler,
    ParentOnboardingHandler parentOnboardingHandler,
    ParentCommandsHandler parentCommandsHandler,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private readonly TelegramBotClient _bot = new(config.BotToken);
    private string _botUsername = "";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: new ReceiverOptions
            {
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
            },
            cancellationToken: ct
        );

        var me = await _bot.GetMe(ct);
        _botUsername = me.Username ?? "";
        logger.LogInformation("Бот @{Username} запущен.", me.Username);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        // ── Callback (кнопки ✅/❌) ─────────────────────────────────────────
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(bot, callback, ct);
            return;
        }

        // ── Обычное сообщение ──────────────────────────────────────────────
        if (update.Message is not { Text: { } userQuestion } message)
            return;

        long chatId = message.Chat.Id;
        long userId = message.From?.Id ?? 0;
        var tgUsername = message.From?.Username;

        // ── /start TOKEN → parent onboarding (no role required) ─────────
        if (userQuestion.StartsWith("/start ") && userQuestion.Length > 7)
        {
            var token = userQuestion[7..].Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    var (success, responseMsg) = await parentOnboardingHandler.HandleAsync(
                        token, userId, tgUsername, ct);
                    await bot.SendMessage(chatId, responseMsg, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при онбординге родителя");
                    await bot.SendMessage(chatId, "Произошла ошибка. Попробуйте позже.", cancellationToken: ct);
                }
                return;
            }
        }

        // ── Role check ──────────────────────────────────────────────────
        var userRole = await dbService.GetUserRoleAsync(userId, ct);

        if (userRole == null)
        {
            logger.LogWarning("Отклонён запрос от пользователя без роли {UserId}", userId);
            await bot.SendMessage(chatId,
                "Нет доступа. Используйте персональную ссылку от администратора.",
                cancellationToken: ct);
            return;
        }

        // ── Parent role → button menu ────────────────────────────────────
        if (userRole.Value.Role == "parent")
        {
            await SendParentMenu(bot, chatId, ct);
            return;
        }

        // ── From here: admin only ───────────────────────────────────────

        // ── Если чат ждёт дополнительных данных после кнопки ✂️ ──────────────────
        if (pendingActions.IsWaitingForEdit(chatId))
        {
            await HandleEditInputAsync(bot, chatId, userQuestion, ct);
            return;
        }

        if (userQuestion.StartsWith("/start"))
        {
            await bot.SendMessage(chatId,
                "👋 Привет! Я помощник менеджера шахматной школы.\n\n" +
                "Могу:\n" +
                "• <b>Отвечать на вопросы</b> — кто должен, посещаемость, расписание\n" +
                "• <b>Оформлять абонементы</b> — <i>Айгерим оплатила 15000 за апрель</i>\n" +
                "• <b>Добавлять учеников</b> — <i>Новый ученик Попов Богдан, 2015</i>\n" +
                "• <b>Записывать в группы</b> — <i>Записать Попова в среду 18:00</i>",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }

        logger.LogInformation(">>> ВОПРОС от {User} ({Id}): {Q}",
            message.From?.Username, userId, userQuestion);

        var statusMsg = await bot.SendMessage(chatId,
            "🧠 <b>Анализирую запрос...</b>",
            parseMode: ParseMode.Html,
            cancellationToken: ct);

        try
        {
            var intent = await intentService.ClassifyAsync(userQuestion, ct);
            logger.LogInformation("Намерение: {Intent}", intent.Intent);

            switch (intent.Intent)
            {
                // ── Команды записи: показываем превью ──────────────────────
                case "sell_subscription":
                    var subPreview = BuildSubscriptionPreview(intent.Params);
                    pendingActions.Set(chatId, new PendingAction(
                        subPreview,
                        intent.Intent,
                        intent.Params,
                        execCt => subscriptionHandler.HandleAsync(intent.Params, execCt)
                    ));
                    await EditWithConfirmButtons(bot, chatId, statusMsg.MessageId, subPreview, ct);
                    return;

                case "new_student":
                    var (studentPreview, studentGroupError) =
                        await BuildNewStudentPreviewAsync(intent.Params, ct);
                    if (studentGroupError != null)
                    {
                        await Edit(bot, chatId, statusMsg.MessageId, studentGroupError, ct);
                        return;
                    }
                    pendingActions.Set(chatId, new PendingAction(
                        studentPreview!,
                        intent.Intent,
                        intent.Params,
                        execCt => newStudentHandler.HandleAsync(intent.Params, execCt)
                    ));
                    await EditWithConfirmButtonsAndEdit(bot, chatId, statusMsg.MessageId, studentPreview!, ct);
                    return;

                case "add_to_group":
                    var (addToGroupPreview, addToGroupError) =
                        await BuildAddToGroupPreviewAsync(intent.Params, ct);
                    if (addToGroupError != null)
                    {
                        await Edit(bot, chatId, statusMsg.MessageId, addToGroupError, ct);
                        return;
                    }
                    pendingActions.Set(chatId, new PendingAction(
                        addToGroupPreview!,
                        intent.Intent,
                        intent.Params,
                        execCt => addToGroupHandler.HandleAsync(intent.Params, execCt)
                    ));
                    await EditWithConfirmButtons(bot, chatId, statusMsg.MessageId, addToGroupPreview!, ct);
                    return;

                case "edit_student":
                    var (editPreview, editError) =
                        await BuildEditStudentPreviewAsync(intent.Params, ct);
                    if (editError != null)
                    {
                        await Edit(bot, chatId, statusMsg.MessageId, editError, ct);
                        return;
                    }
                    pendingActions.Set(chatId, editPreview!);
                    await EditWithConfirmButtons(bot, chatId, statusMsg.MessageId, editPreview!.Preview, ct);
                    return;

                case "remove_from_group":
                    var (removePreview, removeError) =
                        await BuildRemoveFromGroupPreviewAsync(intent.Params, ct);
                    if (removeError != null)
                    {
                        await Edit(bot, chatId, statusMsg.MessageId, removeError, ct);
                        return;
                    }
                    pendingActions.Set(chatId, removePreview!);
                    await EditWithConfirmButtons(bot, chatId, statusMsg.MessageId, removePreview!.Preview, ct);
                    return;

                case "transfer_group":
                    var (transferPreview, transferError) =
                        await BuildTransferGroupPreviewAsync(intent.Params, ct);
                    if (transferError != null)
                    {
                        await Edit(bot, chatId, statusMsg.MessageId, transferError, ct);
                        return;
                    }
                    pendingActions.Set(chatId, transferPreview!);
                    await EditWithConfirmButtons(bot, chatId, statusMsg.MessageId, transferPreview!.Preview, ct);
                    return;

                case "generate_invite":
                    var (inviteAction, inviteError) =
                        await BuildInvitePreviewAsync(intent.Params, ct);
                    if (inviteError != null)
                    {
                        await Edit(bot, chatId, statusMsg.MessageId, inviteError, ct);
                        return;
                    }
                    pendingActions.Set(chatId, inviteAction!);
                    await EditWithConfirmButtons(bot, chatId, statusMsg.MessageId, inviteAction!.Preview, ct);
                    return;

                default:
                    break;
            }

            // ── Аналитика: SQL ──────────────────────────────────────────────
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
                await Edit(bot, chatId, statusMsg.MessageId, "🔍 <b>Повторный запрос к базе...</b>", ct);
                jsonResult = await dbService.ExecuteQueryAsync(sql, ct);
            }

            await Edit(bot, chatId, statusMsg.MessageId, "✍️ <b>Формирую отчёт...</b>", ct);
            string finalResponse = await aiService.AnalyzeDataAsync(userQuestion, jsonResult, ct);
            finalResponse = ConvertToTelegramHtml(finalResponse);

            logger.LogInformation("<<< ОТВЕТ:\n{R}", finalResponse);
            await SendWithFallback(bot, chatId, statusMsg.MessageId, finalResponse, ct);
        }
        catch (InvalidOperationException ioEx)
        {
            logger.LogWarning(ioEx, "Отказ в выполнении операции");
            await Edit(bot, chatId, statusMsg.MessageId, $"⚠️ <b>Отказано:</b>\n{ioEx.Message}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Непредвиденная ошибка");
            await Edit(bot, chatId, statusMsg.MessageId, $"❌ <b>Ошибка:</b> {ex.Message}", ct);
        }
    }

    // ── Callback handler ────────────────────────────────────────────────────

    private async Task HandleCallbackAsync(
        ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var chatId  = callback.Message!.Chat.Id;
        var msgId   = callback.Message.MessageId;
        var data    = callback.Data ?? "";

        // Всегда отвечаем на callback чтобы убрать "часики"
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        // ── Parent callbacks ─────────────────────────────────────────────
        if (data.StartsWith("parent_"))
        {
            await HandleParentCallbackAsync(bot, chatId, callback.From.Id, data, ct);
            return;
        }

        if (data == "edit")
        {
            var action = pendingActions.Peek(chatId);
            if (action == null)
            {
                await EditNoButtons(bot, chatId, msgId, "⚠️ Действие устарело. Повтори запрос.", ct);
                return;
            }
            pendingActions.SetWaitingForEdit(chatId, action.Intent, action.Params);
            await bot.SendMessage(chatId,
                "✏️ Что добавить или изменить? Напиши, например:\n<i>дата рождения 04.11.2019</i>\n<i>мама Иванова Светлана 7771234567</i>",
                parseMode: ParseMode.Html,
                cancellationToken: ct);
            return;
        }

        if (data == "confirm")
        {
            var action = pendingActions.Take(chatId);
            if (action == null)
            {
                await EditNoButtons(bot, chatId, msgId, "⚠️ Действие устарело. Повтори запрос.", ct);
                return;
            }

            await EditNoButtons(bot, chatId, msgId,
                action.Preview + "\n\n⏳ <i>Выполняю...</i>", ct);

            try
            {
                var result = await action.Execute(ct);
                await EditNoButtons(bot, chatId, msgId, result.Message, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при выполнении подтверждённого действия");
                await EditNoButtons(bot, chatId, msgId, $"❌ <b>Ошибка:</b> {ex.Message}", ct);
            }
        }
        else if (data == "cancel")
        {
            pendingActions.Take(chatId);
            await EditNoButtons(bot, chatId, msgId, "↩️ <i>Отменено.</i>", ct);
        }
    }

    // ── Preview builders ────────────────────────────────────────────────────

    private static string BuildSubscriptionPreview(Dictionary<string, string?> p)
    {
        var name      = p.GetValueOrDefault("student_name") ?? "?";
        var amount    = p.GetValueOrDefault("amount") ?? "?";
        var period    = p.GetValueOrDefault("period") ?? "";
        var recipient = p.GetValueOrDefault("recipient");

        var lines = new List<string>
        {
            "📋 <b>Оформить абонемент?</b>\n",
            $"👤 Ученик: <b>{name}</b>",
            $"💰 Сумма: <b>{amount} ₸</b>",
        };
        if (!string.IsNullOrWhiteSpace(period))
            lines.Add($"📅 Период: {period}");
        if (!string.IsNullOrWhiteSpace(recipient))
            lines.Add($"🧑 Получатель: {recipient}");

        return string.Join("\n", lines);
    }

    // Returns (preview, errorMessage) — errorMessage is non-null on blocking validation error
    private async Task<(string? Preview, string? Error)> BuildNewStudentPreviewAsync(
        Dictionary<string, string?> p, CancellationToken ct)
    {
        var lastName   = p.GetValueOrDefault("last_name")  ?? "?";
        var firstName  = p.GetValueOrDefault("first_name") ?? "";
        var birthDateRaw = p.GetValueOrDefault("birth_date");
        var parent     = p.GetValueOrDefault("parent_name");
        var phone      = p.GetValueOrDefault("parent_phone");
        var parent2    = p.GetValueOrDefault("parent_name_2");
        var phone2     = p.GetValueOrDefault("parent_phone_2");
        var level      = p.GetValueOrDefault("level");

        var fullName = string.IsNullOrWhiteSpace(firstName)
            ? lastName.Trim()
            : $"{lastName.Trim()} {firstName.Trim()}";

        var lines = new List<string>
        {
            "👤 <b>Создать нового ученика?</b>\n",
            $"📋 Имя: <b>{fullName}</b>",
        };

        // ── 1. Проверка дубля имени ──────────────────────────────────────
        var duplicates = await dbService.CheckDuplicateClientAsync(fullName, ct);
        var exactDupes = duplicates.Where(d => d.IsExact).ToList();
        var fuzzyDupes = duplicates.Where(d => !d.IsExact).ToList();

        if (exactDupes.Count > 0)
        {
            var list = string.Join(", ", exactDupes.Select(d => $"{d.FullName} (id: {d.Id})"));
            return (null, $"❌ Ученик <b>{fullName}</b> уже есть в базе: {list}.\n\nЕсли это другой человек — измени ФИО через ✏️.");
        }

        if (fuzzyDupes.Count > 0)
        {
            var list = string.Join("\n", fuzzyDupes.Select(d => $"  • {d.FullName} (id: {d.Id})"));
            lines.Add($"⚠️ Похожие ученики:\n{list}\nЕсли это новый ученик — нажми ✅.");
        }

        // ── 2. Дата рождения ─────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(birthDateRaw)
            && DateOnly.TryParseExact(birthDateRaw.Trim(), "dd.MM.yyyy",
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.None, out var birthDate))
        {
            var today = DateOnly.FromDateTime(DateTime.Now);

            if (birthDate > today)
                return (null, $"❌ Дата рождения <b>{birthDate:dd.MM.yyyy}</b> в будущем. Проверь дату.");

            var age = today.Year - birthDate.Year;
            if (birthDate.AddYears(age) > today) age--;

            if (age < 3)
                lines.Add($"🎂 Дата рождения: {birthDate:dd.MM.yyyy} ({age} лет)\n⚠️ Возраст меньше 3 лет — проверь дату.");
            else
                lines.Add($"🎂 Дата рождения: {birthDate:dd.MM.yyyy} ({age} лет)");
        }
        else if (!string.IsNullOrWhiteSpace(birthDateRaw))
        {
            lines.Add($"🎂 Дата рождения: {birthDateRaw}");
        }

        // ── 3. Уровень ──────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(level))
            lines.Add($"📊 Уровень: {level}");

        // ── 4. Родитель 1 ───────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(parent))
            lines.Add($"👩 Родитель: {parent}");

        // ── 5. Телефон + форматирование + дубль ──────────────────────────
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var formatted = FormatPhone(phone);
            p["parent_phone"] = formatted;
            lines.Add($"📞 Телефон: {formatted}");

            var phoneDigits = new string(phone.Where(char.IsDigit).ToArray());
            if (phoneDigits.Length >= 10)
            {
                var phoneDupes = await dbService.FindClientsByPhoneAsync(phoneDigits, ct);
                if (phoneDupes.Count > 0)
                {
                    var list = string.Join(", ", phoneDupes.Select(d => $"{d.FullName} (id: {d.Id})"));
                    lines.Add($"ℹ️ Такой телефон у: {list}");
                }
            }
        }

        // ── 6. Родитель 2 ───────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(parent2))
            lines.Add($"👨 Родитель 2: {parent2}");
        if (!string.IsNullOrWhiteSpace(phone2))
        {
            var formatted2 = FormatPhone(phone2);
            p["parent_phone_2"] = formatted2;
            lines.Add($"📞 Телефон 2: {formatted2}");
        }

        // ── 7. Группы ────────────────────────────────────────────────────
        for (int n = 1; n <= 5; n++)
        {
            var day       = p.GetValueOrDefault($"group_{n}_day");
            var time      = p.GetValueOrDefault($"group_{n}_time");
            var groupId   = p.GetValueOrDefault($"group_{n}_id");
            var grpName   = p.GetValueOrDefault($"group_{n}_name") ?? (n == 1 ? p.GetValueOrDefault("group_name") : null);
            if (string.IsNullOrWhiteSpace(day) && string.IsNullOrWhiteSpace(time)
                && string.IsNullOrWhiteSpace(groupId) && string.IsNullOrWhiteSpace(grpName))
                break;

            var (groupLine, err) = await ResolveGroupLineAsync(day, time, groupId, ct, groupName: grpName);
            if (err != null) return (null, err);
            lines.Add($"📅 Группа {n}: <b>{groupLine}</b>");
        }

        return (string.Join("\n", lines), null);
    }

    private async Task<(string? Preview, string? Error)> BuildAddToGroupPreviewAsync(
        Dictionary<string, string?> p, CancellationToken ct)
    {
        var name      = p.GetValueOrDefault("student_name") ?? "?";
        var gid       = p.GetValueOrDefault("group_id");
        var day       = p.GetValueOrDefault("group_1_day") ?? p.GetValueOrDefault("day_of_week");
        var time      = p.GetValueOrDefault("group_1_time") ?? p.GetValueOrDefault("time");
        var groupName = p.GetValueOrDefault("group_name");

        var (groupLine, err) = await ResolveGroupLineAsync(day, time, gid, ct, groupName: groupName);
        if (err != null) return (null, err);

        return ($"📌 <b>Записать в группу?</b>\n\n👤 Ученик: <b>{name}</b>\n📅 Группа: <b>{groupLine}</b>", null);
    }

    private async Task<(PendingAction? Action, string? Error)> BuildRemoveFromGroupPreviewAsync(
        Dictionary<string, string?> p, CancellationToken ct)
    {
        // ── 1. Find student ──────────────────────────────────────────────
        var studentName = p.GetValueOrDefault("student_name");
        if (string.IsNullOrWhiteSpace(studentName))
            return (null, "❓ Не понял имя ученика. Напиши: <i>Убери Иванова из пн 16:00</i>");

        var matches = await dbService.FindClientByNameAsync(studentName, ct);

        if (matches.Count == 0)
            return (null, $"❌ Ученик <b>{studentName}</b> не найден. Уточни имя.");

        if (matches.Count > 1)
        {
            var list = string.Join("\n", matches.Select(m => $"  • {m.FullName} (id: {m.Id})"));
            return (null, $"🔍 Найдено несколько учеников:\n{list}\n\nУточни полное ФИО.");
        }

        var client = matches[0];

        // ── 2. Find group ────────────────────────────────────────────────
        var day       = p.GetValueOrDefault("group_day");
        var time      = p.GetValueOrDefault("group_time");
        var grpName   = p.GetValueOrDefault("group_name");

        DayOfWeek? dow = ParseDayOfWeek(day);
        TimeSpan?  ts  = ParseTime(time);

        if (string.IsNullOrWhiteSpace(grpName) && (dow == null || ts == null))
            return (null, $"❓ Не понял группу: <b>{day} {time}</b>\nНапиши день и время, например: <i>убери Иванова из вт 18:00</i>");

        var group = await dbService.FindGroupAsync(dow, ts, null, ct, groupName: grpName);
        if (group == null)
        {
            var hint = !string.IsNullOrWhiteSpace(grpName) ? grpName : $"{day} {time}";
            return (null,
                $"❌ Группа <b>{hint}</b> не найдена в базе.\n" +
                $"Уточни день и время, например: <i>вт 18:00</i>");
        }

        // ── 3. Verify student is in group ────────────────────────────────
        if (!await dbService.IsClientInGroupAsync(client.Id, group.Id, ct))
            return (null, $"ℹ️ <b>{client.FullName}</b> не записан в группу <b>{group.Name}</b>.");

        // ── 4. Build preview ─────────────────────────────────────────────
        var preview = $"🚪 <b>Убрать из группы?</b>\n\n" +
                      $"👤 Ученик: <b>{client.FullName}</b>\n" +
                      $"📅 Группа: <b>{group.Name}</b>";

        var capturedClientId   = client.Id;
        var capturedClientName = client.FullName;
        var capturedGroupId    = group.Id;
        var capturedGroupName  = group.Name;

        var action = new PendingAction(
            preview,
            "remove_from_group",
            p,
            execCt => removeFromGroupHandler.HandleAsync(
                capturedClientId, capturedClientName,
                capturedGroupId, capturedGroupName, execCt)
        );

        return (action, null);
    }

    private async Task<(PendingAction? Action, string? Error)> BuildTransferGroupPreviewAsync(
        Dictionary<string, string?> p, CancellationToken ct)
    {
        // ── 1. Find student ──────────────────────────────────────────────
        var studentName = p.GetValueOrDefault("student_name");
        if (string.IsNullOrWhiteSpace(studentName))
            return (null, "❓ Не понял имя ученика. Напиши: <i>Перенеси Иванова из пн 16:00 в ср 18:00</i>");

        var matches = await dbService.FindClientByNameAsync(studentName, ct);

        if (matches.Count == 0)
            return (null, $"❌ Ученик <b>{studentName}</b> не найден. Уточни имя.");

        if (matches.Count > 1)
        {
            var list = string.Join("\n", matches.Select(m => $"  • {m.FullName} (id: {m.Id})"));
            return (null, $"🔍 Найдено несколько учеников:\n{list}\n\nУточни полное ФИО.");
        }

        var client = matches[0];

        // ── 2. Resolve source group ──────────────────────────────────────
        var fromDay     = p.GetValueOrDefault("from_group_day");
        var fromTime    = p.GetValueOrDefault("from_group_time");
        var fromGrpName = p.GetValueOrDefault("from_group_name");

        DatabaseService.GroupInfo fromGroup;

        if (string.IsNullOrWhiteSpace(fromDay) && string.IsNullOrWhiteSpace(fromTime) && string.IsNullOrWhiteSpace(fromGrpName))
        {
            // Source not specified — resolve from active groups
            var activeGroups = await dbService.GetClientActiveGroupsAsync(client.Id, ct);

            if (activeGroups.Count == 0)
                return (null, $"❌ <b>{client.FullName}</b> не записан ни в одну группу.");

            if (activeGroups.Count > 1)
            {
                var groupList = string.Join(", ", activeGroups.Select(g => g.Name));
                return (null,
                    $"🔍 <b>{client.FullName}</b> в нескольких группах: {groupList}\n\n" +
                    $"Уточни, из какой группы перенести.");
            }

            fromGroup = activeGroups[0];
        }
        else
        {
            DayOfWeek? fromDow = ParseDayOfWeek(fromDay);
            TimeSpan?  fromTs  = ParseTime(fromTime);

            if (string.IsNullOrWhiteSpace(fromGrpName) && (fromDow == null || fromTs == null))
                return (null,
                    $"❓ Не понял исходную группу: <b>{fromDay} {fromTime}</b>\n" +
                    $"Напиши, например: <i>перенеси Иванова из вт 18:00 в чт 16:00</i>");

            var found = await dbService.FindGroupAsync(fromDow, fromTs, null, ct, groupName: fromGrpName);
            if (found == null)
            {
                var hint = !string.IsNullOrWhiteSpace(fromGrpName) ? fromGrpName : $"{fromDay} {fromTime}";
                return (null,
                    $"❌ Группа <b>{hint}</b> не найдена в базе.\n" +
                    $"Уточни день и время, например: <i>вт 18:00</i>");
            }

            fromGroup = found;
        }

        // ── 3. Verify student is in source group ─────────────────────────
        if (!await dbService.IsClientInGroupAsync(client.Id, fromGroup.Id, ct))
            return (null, $"ℹ️ <b>{client.FullName}</b> не записан в группу <b>{fromGroup.Name}</b>.");

        // ── 4. Find target group ─────────────────────────────────────────
        var toDay     = p.GetValueOrDefault("to_group_day");
        var toTime    = p.GetValueOrDefault("to_group_time");
        var toGrpName = p.GetValueOrDefault("to_group_name");

        DayOfWeek? toDow = ParseDayOfWeek(toDay);
        TimeSpan?  toTs  = ParseTime(toTime);

        if (string.IsNullOrWhiteSpace(toGrpName) && (toDow == null || toTs == null))
            return (null,
                $"❓ Не понял целевую группу: <b>{toDay} {toTime}</b>\n" +
                $"Напиши, например: <i>перенеси Иванова в ср 18:00</i>");

        var toGroup = await dbService.FindGroupAsync(toDow, toTs, null, ct, groupName: toGrpName);
        if (toGroup == null)
        {
            var hint = !string.IsNullOrWhiteSpace(toGrpName) ? toGrpName : $"{toDay} {toTime}";
            return (null,
                $"❌ Группа <b>{hint}</b> не найдена в базе.\n" +
                $"Уточни день и время, например: <i>ср 18:00</i>");
        }

        // ── 5. Verify target group has free spots ────────────────────────
        if (toGroup.CurrentCount >= toGroup.MaxStudents)
            return (null, $"🚫 В группе <b>{toGroup.Name}</b> нет мест ({toGroup.CurrentCount}/{toGroup.MaxStudents}).");

        // ── 6. Verify student not already in target group ────────────────
        if (await dbService.IsClientInGroupAsync(client.Id, toGroup.Id, ct))
            return (null, $"ℹ️ <b>{client.FullName}</b> уже в группе <b>{toGroup.Name}</b>.");

        // ── 7. Build preview ─────────────────────────────────────────────
        var preview = $"🔄 <b>Перенос?</b>\n\n" +
                      $"👤 Ученик: <b>{client.FullName}</b>\n" +
                      $"📅 Из группы: <b>{fromGroup.Name}</b>\n" +
                      $"📅 В группу: <b>{toGroup.Name}</b> ({toGroup.CurrentCount}/{toGroup.MaxStudents} мест)";

        var capturedClientId    = client.Id;
        var capturedClientName  = client.FullName;
        var capturedFromGroupId = fromGroup.Id;
        var capturedFromName    = fromGroup.Name;
        var capturedToGroupId   = toGroup.Id;
        var capturedToName      = toGroup.Name;

        var action = new PendingAction(
            preview,
            "transfer_group",
            p,
            execCt => transferGroupHandler.HandleAsync(
                capturedClientId, capturedClientName,
                capturedFromGroupId, capturedFromName,
                capturedToGroupId, capturedToName, execCt)
        );

        return (action, null);
    }

    // Returns (PendingAction, null) on success, or (null, errorMessage) on blocking error
    private async Task<(PendingAction? Action, string? Error)> BuildEditStudentPreviewAsync(
        Dictionary<string, string?> p, CancellationToken ct)
    {
        var studentName = p.GetValueOrDefault("student_name");
        if (string.IsNullOrWhiteSpace(studentName))
            return (null, "❓ Не указано имя ученика для редактирования.");

        // ── Parse fields JSON ────────────────────────────────────────────
        var fieldsJson = p.GetValueOrDefault("fields");
        if (string.IsNullOrWhiteSpace(fieldsJson))
            return (null, "❓ Не указаны поля для изменения.");

        Dictionary<string, string> fields;
        try
        {
            fields = JsonSerializer.Deserialize<Dictionary<string, string>>(fieldsJson)
                     ?? new Dictionary<string, string>();
        }
        catch
        {
            return (null, "❌ Не удалось разобрать поля для изменения.");
        }

        if (fields.Count == 0)
            return (null, "❓ Не указаны поля для изменения.");

        // ── Find client ──────────────────────────────────────────────────
        var matches = await dbService.FindClientByNameAsync(studentName, ct);

        if (matches.Count == 0)
            return (null, $"❌ Ученик <b>{studentName}</b> не найден. Уточни имя.");

        if (matches.Count > 1)
        {
            var list = string.Join("\n", matches.Select(m => $"  • {m.FullName} (id: {m.Id})"));
            return (null, $"🔍 Найдено несколько учеников:\n{list}\n\nУточни полное ФИО.");
        }

        var client = matches[0];

        // ── Read current values ──────────────────────────────────────────
        var currentValues = await dbService.GetClientFieldsAsync(client.Id, fields.Keys, ct);

        // ── Validate + build preview ─────────────────────────────────────
        var lines = new List<string>
        {
            "✏️ <b>Изменить данные ученика?</b>\n",
            $"👤 Ученик: <b>{client.FullName}</b> (id: {client.Id})\n",
        };

        var warnings = new List<string>();

        foreach (var (key, newValue) in fields)
        {
            var oldRaw = currentValues.GetValueOrDefault(key);
            var oldDisplay = FormatFieldValue(key, oldRaw);
            var newDisplay = newValue;

            switch (key)
            {
                case "birth_date":
                    if (!DateOnly.TryParseExact(newValue.Trim(), "dd.MM.yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var birthDate))
                    {
                        return (null, $"❌ Не удалось распознать дату: <b>{newValue}</b>. Формат: ДД.ММ.ГГГГ");
                    }

                    var today = DateOnly.FromDateTime(DateTime.Now);
                    if (birthDate > today)
                        return (null, $"❌ Дата рождения <b>{birthDate:dd.MM.yyyy}</b> в будущем. Проверь дату.");

                    var age = today.Year - birthDate.Year;
                    if (birthDate.AddYears(age) > today) age--;
                    if (age < 3)
                        warnings.Add($"⚠️ Возраст {age} лет — проверь дату.");

                    newDisplay = $"{birthDate:dd.MM.yyyy} ({age} лет)";
                    break;

                case "parent_phone":
                case "parent_phone_2":
                    var formatted = FormatPhone(newValue);
                    fields[key] = formatted;
                    newDisplay = formatted;

                    var phoneDigits = new string(newValue.Where(char.IsDigit).ToArray());
                    if (phoneDigits.Length >= 10)
                    {
                        var phoneDupes = await dbService.FindClientsByPhoneAsync(phoneDigits, ct);
                        // Exclude the current client from dupes
                        phoneDupes = phoneDupes.Where(d => d.Id != client.Id).ToList();
                        if (phoneDupes.Count > 0)
                        {
                            var dupeList = string.Join(", ", phoneDupes.Select(d => $"{d.FullName} (id: {d.Id})"));
                            warnings.Add($"ℹ️ Такой телефон у: {dupeList}");
                        }
                    }
                    break;

                case "subscriptions_count":
                    if (!int.TryParse(newValue.Trim(), out var sc) || sc < 0)
                        return (null, $"❌ Неверное значение кол-ва абонементов: <b>{newValue}</b>. Ожидается число >= 0.");
                    break;

                case "is_active":
                    newDisplay = newValue.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                        ? "активен" : "неактивен";
                    if (oldRaw is bool oldBool)
                        oldDisplay = oldBool ? "активен" : "неактивен";
                    break;
            }

            lines.Add($"  {EditStudentHandler.FieldDisplayName(key)}: {oldDisplay} → <b>{newDisplay}</b>");
        }

        foreach (var w in warnings)
            lines.Add(w);

        var preview = string.Join("\n", lines);

        // ── Build PendingAction ──────────────────────────────────────────
        var capturedFields = new Dictionary<string, string>(fields);
        var capturedClientId = client.Id;
        var capturedClientName = client.FullName;

        var action = new PendingAction(
            preview,
            "edit_student",
            p,
            execCt => editStudentHandler.HandleAsync(capturedClientId, capturedClientName, capturedFields, execCt)
        );

        return (action, null);
    }

    private async Task<(PendingAction? Action, string? Error)> BuildInvitePreviewAsync(
        Dictionary<string, string?> p, CancellationToken ct)
    {
        var studentName = p.GetValueOrDefault("student_name");
        if (string.IsNullOrWhiteSpace(studentName))
            return (null, "❓ Не указано имя ученика. Напиши: <i>инвайт для Иванова</i>");

        var matches = await dbService.FindClientByNameAsync(studentName, ct);

        if (matches.Count == 0)
            return (null, $"❌ Ученик <b>{studentName}</b> не найден. Уточни имя.");

        if (matches.Count > 1)
        {
            var list = string.Join("\n", matches.Select(m => $"  • {m.FullName} (id: {m.Id})"));
            return (null, $"🔍 Найдено несколько учеников:\n{list}\n\nУточни полное ФИО.");
        }

        var client = matches[0];

        var preview = $"🔗 <b>Сгенерировать инвайт-ссылку для:</b>\n\n" +
                      $"👤 Ученик: <b>{client.FullName}</b>";

        var capturedClientId = client.Id;
        var capturedClientName = client.FullName;
        var capturedBotUsername = _botUsername;

        var action = new PendingAction(
            preview,
            "generate_invite",
            p,
            execCt => inviteHandler.HandleAsync(
                capturedClientId, capturedClientName, capturedBotUsername, execCt)
        );

        return (action, null);
    }

    /// <summary>Formats a DB value for display in preview.</summary>
    private static string FormatFieldValue(string fieldName, object? value)
    {
        if (value == null) return "<i>не указано</i>";

        return fieldName switch
        {
            "birth_date" when value is DateTime dt => DateOnly.FromDateTime(dt).ToString("dd.MM.yyyy"),
            "birth_date" when value is DateOnly d  => d.ToString("dd.MM.yyyy"),
            "is_active" when value is bool b       => b ? "активен" : "неактивен",
            _                                      => value.ToString() ?? ""
        };
    }

    /// <summary>
    /// Resolves a group from DB by groupName, day+time, or groupId.
    /// Returns ("GroupName (N/8 мест)", null) on success, or (null, errorMessage) if not found.
    /// </summary>
    private async Task<(string? Line, string? Error)> ResolveGroupLineAsync(
        string? day, string? time, string? groupId, CancellationToken ct,
        string? groupName = null)
    {
        int?       gid  = int.TryParse(groupId, out var g) ? g : null;
        DayOfWeek? dow  = ParseDayOfWeek(day);
        TimeSpan?  ts   = ParseTime(time);

        if (string.IsNullOrWhiteSpace(groupName) && gid == null && (dow == null || ts == null))
            return (null, $"❓ Не понял группу: <b>{day} {time}</b>\nНапиши день и время, например: <i>вторник 18:00</i>");

        var group = await dbService.FindGroupAsync(dow, ts, gid, ct, groupName: groupName);
        if (group == null)
        {
            var hint = !string.IsNullOrWhiteSpace(groupName) ? groupName
                     : gid.HasValue ? $"№{gid}"
                     : $"{day} {time}";
            return (null,
                $"❌ Группа <b>{hint}</b> не найдена в базе.\n" +
                $"Уточни день и время начала занятия, например: <i>вт 18:00</i> или <i>сб 11:00</i>");
        }

        return ($"{group.Name} ({group.CurrentCount}/{group.MaxStudents} мест)", null);
    }

    private static DayOfWeek? ParseDayOfWeek(string? raw) => DayOfWeekMapper.MapToDayOfWeek(raw);

    private static TimeSpan? ParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return TimeSpan.TryParse(raw.Trim(), out var ts) ? ts : null;
    }

    /// <summary>Форматирует телефон в 8 (XXX) XXX-XX-XX. Если не удалось — возвращает как есть.</summary>
    private static string FormatPhone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith('7'))
            digits = "8" + digits[1..];
        if (digits.Length == 10)
            digits = "8" + digits;
        if (digits.Length == 11 && digits.StartsWith('8'))
            return $"8 ({digits[1..4]}) {digits[4..7]}-{digits[7..9]}-{digits[9..11]}";
        return raw;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Task EditWithConfirmButtons(
        ITelegramBotClient bot, long chatId, int msgId, string text, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Да, записать", "confirm"),
                InlineKeyboardButton.WithCallbackData("❌ Отменить",    "cancel"),
            }
        });

        return bot.EditMessageText(
            chatId: chatId,
            messageId: msgId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    // ── Parent menu & callbacks ────────────────────────────────────────────

    private static Task SendParentMenu(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📅 Расписание", "parent_schedule") },
            new[] { InlineKeyboardButton.WithCallbackData("📋 Данные ребёнка", "parent_info") },
            new[] { InlineKeyboardButton.WithCallbackData("💳 Абонемент", "parent_subscription") },
        });

        return bot.SendMessage(chatId,
            "Выберите, что хотите узнать:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleParentCallbackAsync(
        ITelegramBotClient bot, long chatId, long tgId, string data, CancellationToken ct)
    {
        var userRole = await dbService.GetUserRoleAsync(tgId, ct);
        if (userRole is not { Role: "parent" } || userRole.Value.ClientId is not { } clientId)
        {
            await bot.SendMessage(chatId, "⚠️ Нет доступа.", cancellationToken: ct);
            return;
        }

        try
        {
            var result = data switch
            {
                "parent_schedule"     => await parentCommandsHandler.HandleScheduleAsync(clientId, ct),
                "parent_info"         => await parentCommandsHandler.HandleInfoAsync(clientId, ct),
                "parent_subscription" => await parentCommandsHandler.HandleSubscriptionAsync(clientId, ct),
                _ => new HandlerResult(false, "⚠️ Неизвестная команда.")
            };

            await bot.SendMessage(chatId, result.Message, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при обработке родительской команды {Data}", data);
            await bot.SendMessage(chatId, "❌ Произошла ошибка. Попробуйте ещё раз.", cancellationToken: ct);
        }

        // Re-send menu so parent can make another choice
        await SendParentMenu(bot, chatId, ct);
    }

    private static Task EditNoButtons(
        ITelegramBotClient bot, long chatId, int msgId, string text, CancellationToken ct)
    {
        return bot.EditMessageText(
            chatId: chatId,
            messageId: msgId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton>()),
            cancellationToken: ct);
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

    private static async Task SendWithFallback(
        ITelegramBotClient bot, long chatId, int msgId, string text, CancellationToken ct)
    {
        try
        {
            await Edit(bot, chatId, msgId, text, ct, ParseMode.Html);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse entities"))
        {
            var clean = Regex.Replace(text, "<.*?>", "");
            await Edit(bot, chatId, msgId, clean, ct);
        }
    }

    private static string ConvertToTelegramHtml(string text)
    {
        text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<b>$1</b>");
        text = Regex.Replace(text, @"__(.*?)__",     "<b>$1</b>");
        text = Regex.Replace(text, @"`(.*?)`",       "<code>$1</code>");
        return text;
    }

    // ── Edit input handler ────────────────────────────────────────────────

    private async Task HandleEditInputAsync(
        ITelegramBotClient bot, long chatId, string userInput, CancellationToken ct)
    {
        var editState = pendingActions.TakeEditState(chatId);
        if (editState == null) return;

        var statusMsg = await bot.SendMessage(chatId,
            "🧠 <b>Обновляю данные...</b>",
            parseMode: ParseMode.Html, cancellationToken: ct);

        // Извлекаем новые поля с контекстом черновика
        var newFields = await intentService.ClassifyAdditionalDataAsync(
            userInput, editState.Intent, editState.Params, ct);

        // Мёрджим парамы: новые перезаписывают старые
        var mergedParams = new Dictionary<string, string?>(editState.Params);
        foreach (var kv in newFields)
            if (!string.IsNullOrWhiteSpace(kv.Value))
                mergedParams[kv.Key] = kv.Value;

        // Перестраиваем превью
        string preview;
        switch (editState.Intent)
        {
            case "new_student":
                var (p, err) = await BuildNewStudentPreviewAsync(mergedParams, ct);
                if (err != null)
                {
                    await Edit(bot, chatId, statusMsg.MessageId, err, ct);
                    return;
                }
                preview = p!;
                pendingActions.Set(chatId, new PendingAction(
                    preview, editState.Intent, mergedParams,
                    execCt => newStudentHandler.HandleAsync(mergedParams, execCt)
                ));
                break;
            case "generate_invite":
                var (invAction, invErr) = await BuildInvitePreviewAsync(mergedParams, ct);
                if (invErr != null)
                {
                    await Edit(bot, chatId, statusMsg.MessageId, invErr, ct);
                    return;
                }
                preview = invAction!.Preview;
                pendingActions.Set(chatId, invAction);
                break;
            default:
                preview = pendingActions.Peek(chatId)?.Preview ?? "?";
                pendingActions.Set(chatId, new PendingAction(
                    preview, editState.Intent, mergedParams,
                    execCt => newStudentHandler.HandleAsync(mergedParams, execCt)
                ));
                break;
        }

        // Редактируем статус-сообщение превьюем и кнопками
        await EditWithConfirmButtonsAndEdit(bot, chatId, statusMsg.MessageId, preview, ct);
    }

    private static Task EditWithConfirmButtonsAndEdit(
        ITelegramBotClient bot, long chatId, int msgId, string text, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Да, записать", "confirm"),
                InlineKeyboardButton.WithCallbackData("❌ Отменить",    "cancel"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✏️ Добавить данные", "edit"),
            }
        });

        return bot.EditMessageText(
            chatId: chatId,
            messageId: msgId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        logger.LogError(ex, "Ошибка Telegram Polling");
        return Task.CompletedTask;
    }
}
