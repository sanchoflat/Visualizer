using System;
using System.Collections.Generic;

namespace TradeViewer.Models;

public enum OrderSide
{
    Buy,
    Sell
}

public sealed class ParsedData
{
    public List<PriceEntry> SpotPrices { get; } = new();
    public List<PriceEntry> LinearPrices { get; } = new();
    public List<OrderEntry> Orders { get; } = new();
    public List<TradeEntry> Trades { get; } = new();
    public List<SpreadEntry> Spreads { get; } = new();
    public List<BorderEntry> Borders { get; } = new();
}

public sealed record PriceEntry(DateTime Time, double Ask, double Bid);

public sealed record TradeEntry(DateTime Time, double Price, OrderSide Side, string Symbol);

public sealed record SpreadEntry(DateTime Time, double S1, double S2);

public sealed record BorderEntry(DateTime Time, double B1, double B2, double B3, double B4);

public sealed class OrderEntry
{
    public OrderEntry(DateTime startTime, double price, OrderSide side, string symbol, string orderId, string status)
    {
        StartTime = startTime;
        Price = price;
        Side = side;
        Symbol = symbol;
        OrderId = orderId;
        Status = status;
        EndTime = startTime;
        FinalStatus = status;
    }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double Price { get; }
    public OrderSide Side { get; }
    public string Symbol { get; }
    public string OrderId { get; }
    public string Status { get; set; }
    public string FinalStatus { get; set; }
}
