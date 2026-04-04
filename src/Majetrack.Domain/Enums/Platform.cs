namespace Majetrack.Domain.Enums;

/// <summary>
/// Identifies the brokerage or investment platform through which assets are held
/// and transactions are executed. Each platform has its own import format and fee structure.
/// </summary>
public enum Platform
{
    /// <summary>
    /// XTB (X-Trade Brokers) — a European online brokerage offering stocks, ETFs,
    /// and CFDs with commission-free trading on selected instruments.
    /// </summary>
    Xtb = 1,

    /// <summary>
    /// eToro — a social trading and multi-asset brokerage platform known for
    /// copy-trading features and fractional share support.
    /// </summary>
    Etoro = 2,

    /// <summary>
    /// Investown — a Czech peer-to-peer lending platform that allows users
    /// to invest in consumer and business loans for interest income.
    /// </summary>
    Investown = 3,

    /// <summary>
    /// XTB Trading History — the XTB (X-Trade Brokers) trading history export format.
    /// Differs from <see cref="Xtb"/> (closed-positions) in column layout:
    /// OpenTime, Type, Symbol, Volume, Profit, Commission, Swap, OpenPrice, ClosePrice,
    /// StopLoss, TakeProfit, Magic, Comment.
    /// </summary>
    XtbTradingHistory = 4,
}
