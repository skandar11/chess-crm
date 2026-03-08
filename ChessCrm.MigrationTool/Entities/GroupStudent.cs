using System.ComponentModel.DataAnnotations.Schema;

namespace ChessCrm.MigrationTool.Entities;

[Table("group_students")]
public class GroupStudent : BaseEntity
{
    [Column("group_id")]
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    [Column("client_id")]
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
}