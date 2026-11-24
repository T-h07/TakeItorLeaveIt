using System;
using System.Collections.Generic;
using TakeItOrLeaveIt.Core.Diagnostics;
using TakeItOrLeaveIt.Core.Discovery;
using TakeItOrLeaveIt.Core.Policies;

namespace TakeItOrLeaveIt.Core.Sales;

/// <summary>
/// The single entry point the game hooks call for player sales. It owns the active order book of the current
/// save and turns game events into Core decisions:
/// <list type="bullet">
/// <item>contract accepted → maybe create a category order (snapshot of the live policy),</item>
/// <item>handover items → validate against the snapshot (before anything is consumed),</item>
/// <item>handover processed → record the commit once,</item>
/// <item>contract ended or vanished → drop the snapshot.</item>
/// </list>
/// It never pays, awards XP, moves items or completes contracts: vanilla does all of that.
/// Main-thread only.
/// </summary>
public sealed class PlayerSaleCoordinator
{
    private readonly IPolicyProvider _policies;
    private readonly Func<ProductCatalogSnapshot?> _catalog;
    private readonly IModLog? _log;
    private readonly ActiveOrderBook _book = new();
    private long _savedVersion;

    public PlayerSaleCoordinator(IPolicyProvider policies, Func<ProductCatalogSnapshot?> catalog, IModLog? log = null)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _log = log;
    }

    public ActiveOrderBook Book => _book;

    /// <summary>
    /// Whether the live policy is trustworthy for new orders (the save's settings were read). Null means always.
    /// When it returns false, new contracts stay vanilla.
    /// </summary>
    public Func<bool>? PolicyReady { get; set; }

    /// <summary>The game's unit prices, read at acceptance for the fair-price rule. Null disables substitution.</summary>
    public IProductPriceSource? Prices { get; set; }

    /// <summary>The save the book belongs to; null outside a loaded save.</summary>
    public string? SaveIdentity { get; private set; }

    /// <summary>
    /// False in sessions Phase 03 does not support (a client, or more than one player): every hook then
    /// leaves the game's own rules in charge. Set by the game layer from verified session state.
    /// </summary>
    public bool IsAuthoritative { get; private set; }

    public string AuthorityDetail { get; private set; } = "No session.";

    /// <summary>True when the book changed since the last <see cref="MarkPersisted"/>.</summary>
    public bool HasUnsavedOrders => _book.Version != _savedVersion;

    public void SetAuthority(bool isServer, int playerCount)
    {
        var authoritative = isServer && playerCount >= 0 && playerCount <= 1;
        var detail = !isServer
            ? "Not the host: player sales use vanilla rules."
            : playerCount > 1
                ? $"{playerCount} players: player sales use vanilla rules (legacy single-player gate; the game uses the Phase 07 session model)."
                : "Single player host.";
        Apply(authoritative, detail);
    }

    /// <summary>
    /// Phase 07 gate: authoritative only when the session model allows this feature (the host, single player or
    /// multiplayer). Clients, unsupported and disconnected sessions leave every contract vanilla.
    /// </summary>
    public void ApplySession(TakeItOrLeaveIt.Core.Multiplayer.SessionDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        Apply(decision.Capabilities.PlayerOrders, decision.Detail);
    }

    private void Apply(bool authoritative, string detail)
    {
        if (authoritative != IsAuthoritative || detail != AuthorityDetail)
        {
            IsAuthoritative = authoritative;
            AuthorityDetail = detail;
            _log?.Info($"Player sales authority: {detail}");
        }
    }

    /// <summary>Switches to another save's orders (or none). Loaded orders are pruned once the game's contracts are known.</summary>
    public void BindSave(string? saveIdentity, IEnumerable<(CategoryOrder Order, SaleState State)>? orders)
    {
        _book.Clear();
        SaveIdentity = saveIdentity;
        foreach (var (order, state) in orders ?? Array.Empty<(CategoryOrder, SaleState)>())
        {
            _book.TryAdd(order, state);
        }

        _savedVersion = _book.Version;
    }

    public void MarkPersisted() => _savedVersion = _book.Version;

    /// <summary>Contract accepted: decide whether it becomes a category order and take the snapshot.</summary>
    public OrderCreation OnContractAccepted(OrderRequest request)
    {
        OrderCreation result;
        if (!IsAuthoritative)
        {
            result = new OrderCreation(OrderOutcome.ModDisabled, null, AuthorityDetail);
        }
        else if (SaveIdentity is null)
        {
            result = new OrderCreation(OrderOutcome.CatalogNotReady, null, "No active save: vanilla rules.");
        }
        else if (PolicyReady is not null && !PolicyReady())
        {
            result = new OrderCreation(OrderOutcome.PolicyNotLoaded, null, "This save's Take It settings are not loaded or unreadable: vanilla rules.");
        }
        else
        {
            result = CategoryOrderFactory.Create(request, _policies.Current, _catalog(), Prices);
        }

        if (result.Order is not null && !_book.TryAdd(result.Order))
        {
            // A snapshot already exists (repeated callback): keep the first one.
            _book.TryGet(result.Order.ContractId, out var existing);
            return new OrderCreation(OrderOutcome.Managed, existing.Order, "Snapshot already taken; kept the original.");
        }

        return result;
    }

    /// <summary>
    /// The managed order for a contract, or null when the contract follows vanilla rules (not managed,
    /// unsupported session).
    /// </summary>
    public SaleTransaction? Find(string? contractId)
        => IsAuthoritative && _book.TryGet(contractId, out var transaction) && !transaction.IsTerminal ? transaction : null;

    /// <summary>Validates handover items for a managed contract; null when the contract follows vanilla rules.</summary>
    public DeliveryValidation? Validate(string? contractId, IReadOnlyList<DeliveredItem?> items)
    {
        var transaction = Find(contractId);
        if (transaction is null)
        {
            return null;
        }

        var catalog = _catalog();
        var order = transaction.Order;
        var validation = DeliveryValidator.Validate(order, items, catalog,
            product => RuleExplainer.ExplainNotAllowed(order, _policies.Current, catalog, product));
        _book.RecordValidation(validation);
        return validation;
    }

    public CommitObservation OnHandoverProcessed(string? contractId)
    {
        var outcome = _book.ObserveCommit(contractId);
        if (outcome == CommitObservation.Duplicate || outcome == CommitObservation.AlreadyEnded)
        {
            _log?.Warning($"Handover for order {contractId} reported again ({outcome}); ignored. Vanilla stays in charge of the transaction.");
        }

        return outcome;
    }

    public SaleTransaction? OnContractEnded(string? contractId, SaleEnd end) => _book.End(contractId, end);

    /// <summary>Drops orders whose contracts the game no longer has. Call when the contract list is known (after load).</summary>
    public int Prune(IEnumerable<string?> liveContractIds) => _book.Prune(liveContractIds);
}
