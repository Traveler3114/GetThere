namespace GetThereShared.Enums;

public enum WalletTransactionType
{
    Deposit,
    Withdrawal,
    TicketPurchase,
    Refund,

    // For Hold/Release the transaction's Amount is the reserved magnitude, not a balance delta —
    // Balance is unchanged (BalanceBefore == BalanceAfter); only the wallet's Reserved moves.
    Hold,
    Release,
}
