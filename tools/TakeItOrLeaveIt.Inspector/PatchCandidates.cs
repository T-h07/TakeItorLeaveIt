namespace TakeItOrLeaveIt.Inspector;

/// <summary>
/// Members listed in docs/PATCH_MAP.md as "probable" (seen in S1API source, not yet in a local game build).
/// The inspector reports each as FOUND / MISSING in the installed game so the patch map can be promoted
/// to "verified" or corrected. Type names are given without the IL2CPP "Il2Cpp" namespace prefix;
/// both forms are searched.
/// </summary>
internal static class PatchCandidates
{
    public static readonly (string Id, string Type, string Member)[] All =
    {
        ("CUS-01", "ScheduleOne.Economy.Customer", "CustomerData"),
        ("CUS-02", "ScheduleOne.Economy.Customer", "OfferContract"),
        ("CUS-03", "ScheduleOne.Economy.Customer", "RequestProduct"),
        ("CUS-04", "ScheduleOne.Economy.Customer", "CompletedDeliveries"),
        ("CUS-05", "ScheduleOne.Economy.Customer", "currentAffinityData"),
        ("CUS-06", "ScheduleOne.Economy.CustomerData", "PreferredProperties"),
        ("CUS-07", "ScheduleOne.Economy.CustomerData", "DefaultAffinityData"),
        ("CUS-08", "ScheduleOne.Economy.CustomerAffinityData", "ProductAffinities"),
        ("CUS-09", "ScheduleOne.Economy.ProductTypeAffinity", "Affinity"),
        ("ORD-01", "ScheduleOne.Quests.Contract", "ProductList"),
        ("ORD-02", "ScheduleOne.Quests.Contract", "Payment"),
        ("ORD-03", "ScheduleOne.Quests.Contract", "DeliveryWindow"),
        ("ORD-04", "ScheduleOne.Quests.ContractInfo", ".ctor"),
        ("ORD-05", "ScheduleOne.Product.ProductList", "entries"),
        ("ORD-06", "ScheduleOne.Product.ProductList+Entry", "ProductID"),
        ("ORD-07", "ScheduleOne.Product.ProductList+Entry", "Quantity"),
        ("ORD-08", "ScheduleOne.Product.ProductList+Entry", "Quality"),
        ("PRD-01", "ScheduleOne.Product.ProductManager", "DiscoveredProducts"),
        ("PRD-02", "ScheduleOne.Product.ProductManager", "ListedProducts"),
        ("PRD-03", "ScheduleOne.Product.ProductManager", "MethDiscovered"),
        ("PRD-04", "ScheduleOne.Product.ProductManager", "CocaineDiscovered"),
        ("PRD-05", "ScheduleOne.Product.ProductManager", "ShroomsDiscovered"),
        ("PRD-06", "ScheduleOne.Product.ProductManager", "IsAcceptingOrders"),
        ("PRD-07", "ScheduleOne.Product.ProductManager", "FinishAndNameMix"),
        ("PRD-08", "ScheduleOne.Product.ProductManager", "GetPrice"),
        ("PRD-09", "ScheduleOne.Product.ProductDefinition", "DrugType"),
        ("PRD-10", "ScheduleOne.Product.ProductDefinition", "DrugTypes"),
        ("PRD-11", "ScheduleOne.Product.ProductDefinition", "Properties"),
        ("PRD-12", "ScheduleOne.Product.ProductItemInstance", ".ctor"),
        ("PRD-13", "ScheduleOne.Product.EDrugType", "Marijuana"),
        ("PRD-14", "ScheduleOne.ItemFramework.EQuality", "Heavenly"),
        ("DLR-01", "ScheduleOne.Economy.Dealer", "AssignedCustomers"),
        ("DLR-02", "ScheduleOne.Economy.Dealer", "ActiveContracts"),
        ("DLR-03", "ScheduleOne.Economy.Dealer", "Cut"),
        ("DLR-04", "ScheduleOne.Economy.Dealer", "Cash"),
        ("DLR-05", "ScheduleOne.Economy.Dealer", "CollectCash"),
        ("DLR-06", "ScheduleOne.Economy.Dealer", "onContractAccepted"),
        ("DLR-07", "ScheduleOne.UI.Phone.Messages.DealerManagementApp", "Refresh"),
        ("ECO-01", "ScheduleOne.Money.MoneyManager", "ChangeCashBalance"),
        ("ECO-02", "ScheduleOne.Money.MoneyManager", "cashBalance"),
        ("ECO-03", "ScheduleOne.Levelling.LevelManager", "AddXP"),
        ("LIF-01", "ScheduleOne.Persistence.LoadManager", "onLoadComplete"),
        ("LIF-02", "ScheduleOne.Persistence.LoadManager", "onPreSceneChange"),
        ("LIF-03", "ScheduleOne.Persistence.LoadManager", "ExitToMenu"),
        ("LIF-04", "ScheduleOne.Persistence.SaveManager", "Save"),
    };

    /// <summary>Keywords for discovering members that S1API does not expose (handover, acceptance, payment...).</summary>
    public static readonly string[] DiscoveryKeywords =
    {
        "Handover", "Offer", "Counter", "Accept", "Reject", "Evaluate", "Satisf", "Enjoy", "Preferred",
        "Affinity", "Contract", "Deal", "Sample", "Discover", "Unlock", "Payment", "Pay", "Cash",
        "Commission", "Cut", "XP", "Relationship", "Relation", "Quality", "Quantity", "Mix", "Recipe",
        "Inventory", "Reserve", "Complete", "Fail", "Expire", "Cancel",
    };

    /// <summary>Namespaces (without the IL2CPP prefix) dumped in full.</summary>
    public static readonly string[] Namespaces =
    {
        "ScheduleOne.Economy", "ScheduleOne.Quests", "ScheduleOne.Product", "ScheduleOne.ItemFramework",
        "ScheduleOne.Money", "ScheduleOne.Levelling", "ScheduleOne.NPCs.Relation", "ScheduleOne.UI.Handover",
        "ScheduleOne.Effects", "ScheduleOne.Persistence", "ScheduleOne.UI.Phone",
    };
}
