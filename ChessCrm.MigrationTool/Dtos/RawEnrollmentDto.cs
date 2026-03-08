namespace ChessCrm.MigrationTool.Dtos;

public class RawEnrollmentDto
{
    public int GroupId { get; set; } // ID группы из нашей БД (1, 2, 3...)
    public string StudentName { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public int RowIndex { get; set; }
}