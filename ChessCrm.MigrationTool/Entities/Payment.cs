using System.ComponentModel.DataAnnotations.Schema;
using ChessCrm.MigrationTool.Enums;

namespace ChessCrm.MigrationTool.Entities;

[Table("payments")]
public class Payment : BaseEntity
{
    [Column("client_id")]
    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("subscription_id")]
    public int? SubscriptionId { get; set; } // За какой абонемент платили?
    public Subscription? Subscription { get; set; }

    [Column("payment_date")]
    public DateTime? PaymentDate { get; set; }

    [Column("method")]
    public PaymentMethod Method { get; set; }

    [Column("card_last_digits")]
    public string? CardLastDigits { get; set; }

    [Column("receiver_name")]
    public string ReceiverName { get; set; } = string.Empty;

    [Column("is_refunded")]
    public bool IsRefunded { get; set; } = false;

    [Column("comment")]
    public string? Comment { get; set; }
}