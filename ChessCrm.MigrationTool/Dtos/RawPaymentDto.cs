namespace ChessCrm.MigrationTool.Dtos;

public class RawPaymentDto
{
    public string SheetName { get; set; } = string.Empty;
    public int RowIndex { get; set; }
    public string Date { get; set; } = string.Empty;
    public int SheetYear { get; set; } = DateTime.Now.Year;
    public string ClientIdentifier { get; set; } = string.Empty; // "Фамилия" или "Турнир"
    public string Amount { get; set; } = string.Empty;
    public string PaymentMethodHint { get; set; } = string.Empty; // "Карта"
    public string ReceiverName { get; set; } = string.Empty; // "Нурсултан" или "Никита"
}