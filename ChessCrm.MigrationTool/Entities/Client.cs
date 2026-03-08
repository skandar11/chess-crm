using System.ComponentModel.DataAnnotations.Schema;
using ChessCrm.MigrationTool.Enums;

namespace ChessCrm.MigrationTool.Entities;

[Table("clients")]
public class Client : BaseEntity
{
    // --- Личные данные ---
    [Column("last_name")]
    public string LastName { get; set; } = string.Empty;

    [Column("first_name")]
    public string? FirstName { get; set; }

    [Column("middle_name")]
    public string? MiddleName { get; set; }

    [Column("birth_date")]
    public DateOnly? BirthDate { get; set; }

    // --- Контакты ---
    [Column("phone")]
    public string? Phone { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    // --- Родители ---
    [Column("parent1_name")]
    public string? Parent1Name { get; set; }

    [Column("parent1_phone")]
    public string? Parent1Phone { get; set; }

    [Column("parent2_name")]
    public string? Parent2Name { get; set; }

    [Column("parent2_phone")]
    public string? Parent2Phone { get; set; }

    // --- Системные поля ---
    [Column("notes", TypeName = "text")]
    public string? Notes { get; set; }

    [Column("status")]
    public ClientStatus Status { get; set; } = ClientStatus.Active;

    [Column("balance")]
    public decimal Balance { get; set; } = 0;

    [Column("source")]
    public string? Source { get; set; } // Откуда узнал (Реклама, Друзья)

    [Column("discount_percent")]
    public int DiscountPercent { get; set; } = 0; // Персональная скидка (0-100%)

    [Column("additional_data", TypeName = "jsonb")]
    public string? AdditionalData { get; set; }

    [Column("external_id")]
    public string? ExternalId { get; set; }
}