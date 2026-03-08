namespace ChessCrm.MigrationTool.Dtos;

// Представляет одну строку с данными клиента из Google-таблицы
public class RawClientDto
{
    public int RowIndex { get; set; } // Номер строки в таблице
    public string Id { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public string Parent1 { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string Parent2 { get; set; } = string.Empty;
    public string Phone2 { get; set; } = string.Empty;
}