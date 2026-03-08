using System.ComponentModel.DataAnnotations.Schema;

namespace ChessCrm.MigrationTool.Entities;

[Table("subscriptions")]
public class Subscription : BaseEntity
{
    [Column("client_id")]
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("total_lessons")]
    public int TotalLessons { get; set; }

    [Column("lessons_left")]
    public int LessonsLeft { get; set; }

    [Column("start_date")]
    public DateOnly StartDate { get; set; }

    [Column("end_date")]
    public DateOnly EndDate { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}