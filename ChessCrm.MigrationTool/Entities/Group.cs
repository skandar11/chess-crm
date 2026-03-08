using System.ComponentModel.DataAnnotations.Schema;
using ChessCrm.MigrationTool.Enums;

namespace ChessCrm.MigrationTool.Entities;

[Table("groups")]
public class Group : BaseEntity
{
    [Column("name")]
    public string Name { get; set; } = string.Empty; // Например: "Старшая Пн 16:30"

    [Column("level")]
    public GroupLevel Level { get; set; }

    [Column("format")]
    public TrainingFormat Format { get; set; }

    [Column("coach_name")]
    public string? CoachName { get; set; }

    // --- Параметры конкретного занятия ---
    [Column("day_of_week")]
    public DayOfWeek DayOfWeek { get; set; }

    [Column("start_time")]
    public TimeSpan StartTime { get; set; }

    [Column("end_time")]
    public TimeSpan EndTime { get; set; }

    // --- Версионирование ---
    [Column("effective_from")]
    public DateOnly EffectiveFrom { get; set; }

    [Column("effective_to")]
    public DateOnly? EffectiveTo { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("max_students")]
    public int MaxStudents { get; set; } = 8;
}