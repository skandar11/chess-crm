using System.ComponentModel.DataAnnotations.Schema;
using ChessCrm.MigrationTool.Enums;

namespace ChessCrm.MigrationTool.Entities;

[Table("attendance")]
public class Attendance : BaseEntity
{
    [Column("client_id")]
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Column("group_id")]
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    [Column("date")]
    public DateOnly Date { get; set; }

    [Column("status")]
    public AttendanceStatus Status { get; set; }

    [Column("subscription_id")]
    public int? SubscriptionId { get; set; } // С какого абонемента списали занятие?
    public Subscription? Subscription { get; set; }

    [Column("topic")]
    public string? Topic { get; set; }

    [Column("teacher_comment")]
    public string? TeacherComment { get; set; }
}