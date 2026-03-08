namespace ChessCrm.MigrationTool.Enums;

// Уровень группы
public enum GroupLevel
{
    Beginner = 1, // Новички
    Junior = 2,   // Младшая группа
    Middle = 3,   // Средняя группа
    Senior = 4    // Старшая группа
}

// Формат обучения
public enum TrainingFormat
{
    Offline = 1, // Очно
    Online = 2   // Онлайн
}

// Статус клиента
public enum ClientStatus
{
    Active = 1,   // Занимается
    Paused = 2,   // Временно не ходит
    Archived = 3, // Архив (ушел)
    Lead = 4      // Заявка (еще не купил)
}

// Тип оплаты
public enum PaymentMethod
{
    Cash = 1,     // Наличные
    Card = 2,     // Карта / Перевод
    QrCode = 3    // QR
}

// Статус посещения
public enum AttendanceStatus
{
    Scheduled = 0, // Запланировано
    Present = 1,   // Был
    Absent = 2,    // Прогул
    Sick = 3,      // Болел (справка)
    Excused = 4    // Уважительная причина
}