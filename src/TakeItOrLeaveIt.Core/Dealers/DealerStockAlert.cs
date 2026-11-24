using System;
using TakeItOrLeaveIt.Core.Products;

namespace TakeItOrLeaveIt.Core.Dealers;

public enum DealerAlertType
{
    /// <summary>Type A: the dealer has no permitted units left of a category they stocked or have demand for.</summary>
    OutOfStock,

    /// <summary>Type B: a waiting appointment needs more permitted units than the dealer can reserve.</summary>
    AppointmentShortage,
}

/// <summary>
/// One restock message the dealer should send to the player through the game's own phone conversation. Immutable.
/// <see cref="Key"/> is the deduplication identity: save, dealer, category, type (and the contract for a shortage).
/// </summary>
public sealed class DealerStockAlert
{
    internal DealerStockAlert(
        string saveId,
        string dealerId,
        CategoryKey category,
        DealerAlertType type,
        string? contractId,
        long required,
        long available,
        string text)
    {
        SaveId = saveId;
        DealerId = dealerId;
        Category = category;
        Type = type;
        ContractId = contractId;
        Required = required;
        Available = available;
        Text = text;
        Key = MakeKey(saveId, dealerId, category, type, contractId);
    }

    public string SaveId { get; }

    public string DealerId { get; }

    public CategoryKey Category { get; }

    public DealerAlertType Type { get; }

    /// <summary>The waiting appointment (shortage alerts only).</summary>
    public string? ContractId { get; }

    /// <summary>Units the appointment needs (shortage alerts; 0 for out-of-stock).</summary>
    public long Required { get; }

    /// <summary>Permitted units the dealer can use for it now (eligible stock minus other appointments' holds).</summary>
    public long Available { get; }

    public long Shortfall => Math.Max(0, Required - Available);

    /// <summary>The message text, written as the dealer.</summary>
    public string Text { get; }

    public string Key { get; }

    public static string MakeKey(string saveId, string dealerId, CategoryKey category, DealerAlertType type, string? contractId)
        => $"{saveId}|{dealerId}|{category.Value}|{type}" + (contractId is null ? string.Empty : "|" + contractId);

    public static string OutOfStockText(string categoryName)
        => $"Yo boss, I'm out of {categoryName} I can sell. Need you to restock me.";

    public static string ShortageText(string categoryName, long required, long available)
        => $"Boss, I've got a customer waiting for {required} {categoryName}, but only {available} permitted units are available. Need {Math.Max(0, required - available)} more.";

    public override string ToString() => $"{Type} {DealerId}/{Category}{(ContractId is null ? string.Empty : " " + ContractId)}: \"{Text}\"";
}
