using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChessCrm.MigrationTool.Entities;

public abstract class BaseEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // --- Поля для синхронизации ---

    // Хэш сырых данных строки (чтобы понимать, были ли изменения)
    [Column("sync_hash")]
    public string? SyncHash { get; set; }

    // Когда последний раз данные обновлялись ИЗ Гугла
    [Column("last_synced_at")]
    public DateTime? LastSyncedAt { get; set; }

    // ID таблицы (SpreadsheetId), откуда пришла запись
    [Column("google_spreadsheet_id")]
    public string? GoogleSpreadsheetId { get; set; }

    // Номер строки в гугл таблице
    [Column("google_sheet_row_index")]
    public int? GoogleSheetRowIndex { get; set; }
}