using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TradeViewer.Models;

namespace TradeViewer.Parsing;

public static class LogParser
{
    private sealed class TimeState
    {
        public DateTime BaseDate { get; } = new(2024, 1, 1);
        public TimeSpan Offset { get; set; }
        public DateTime? LastRawDt { get; set; }
        public DateTime? LastAdjustedDt { get; set; }
    }

    public static ParsedData? Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var data = new ParsedData();
        var activeSlots = new Dictionary<(string Symbol, OrderSide Side), OrderEntry>();
        var activeOrdersById = new Dictionary<string, OrderEntry>(StringComparer.OrdinalIgnoreCase);
        var timeState = new TimeState();
        DateTime lastTimestamp = timeState.BaseDate;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            if (!rawLine.Contains('|'))
            {
                continue;
            }

            var parts = rawLine.Split('|');
            if (parts.Length < 2)
            {
                continue;
            }

            if (!TryParseTimestamp(parts[0].Trim(), timeState, out var timestamp))
            {
                continue;
            }

            lastTimestamp = timestamp;
            var eventType = parts[1].Trim();

            try
            {
                if (string.Equals(eventType, "Candle", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                switch (eventType)
                {
                    case "Top":
                        ParseTop(parts, timestamp, data);
                        break;
                    case "UserOrder":
                        ParseOrder(parts, timestamp, data, activeSlots, activeOrdersById);
                        break;
                    case "UserTrade":
                        ParseTrade(parts, timestamp, data);
                        break;
                    case "Border":
                        ParseBorder(parts, timestamp, data);
                        break;
                    case "Spreads":
                        ParseSpread(parts, timestamp, data);
                        break;
                }
            }
            catch
            {
                continue;
            }
        }

        var endTime = timeState.LastAdjustedDt ?? lastTimestamp;
        foreach (var order in activeSlots.Values)
        {
            order.EndTime = endTime;
            order.FinalStatus = "ActiveAtEnd";
            data.Orders.Add(order);
        }

        data.SpotPrices.Sort((a, b) => a.Time.CompareTo(b.Time));
        data.LinearPrices.Sort((a, b) => a.Time.CompareTo(b.Time));
        data.Trades.Sort((a, b) => a.Time.CompareTo(b.Time));
        data.Borders.Sort((a, b) => a.Time.CompareTo(b.Time));
        data.Spreads.Sort((a, b) => a.Time.CompareTo(b.Time));
        data.Orders.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        return data;
    }

    private static void ParseTop(string[] parts, DateTime timestamp, ParsedData data)
    {
        if (parts.Length < 5)
        {
            return;
        }

        var symbol = parts[2].Trim();
        if (!TryParseDouble(parts[3], out var ask) || !TryParseDouble(parts[4], out var bid))
        {
            return;
        }

        var record = new PriceEntry(timestamp, ask, bid);
        if (symbol.Contains("Spot", StringComparison.OrdinalIgnoreCase))
        {
            data.SpotPrices.Add(record);
        }
        else if (symbol.Contains("Linear", StringComparison.OrdinalIgnoreCase))
        {
            data.LinearPrices.Add(record);
        }
    }

    private static void ParseOrder(
        string[] parts,
        DateTime timestamp,
        ParsedData data,
        Dictionary<(string Symbol, OrderSide Side), OrderEntry> activeSlots,
        Dictionary<string, OrderEntry> activeOrdersById)
    {
        if (parts.Length < 9)
        {
            return;
        }

        var symbol = parts[2].Trim();
        var price = TryParseDouble(parts[3], out var parsedPrice) ? parsedPrice : 0.0;
        var side = ParseSide(parts[6]);
        var orderId = parts[7].Trim();
        var status = parts[8].Trim();

        if (status == "New")
        {
            if (side is null)
            {
                return;
            }

            var slotKey = (symbol, side.Value);
            if (activeSlots.TryGetValue(slotKey, out var previous))
            {
                previous.EndTime = timestamp;
                previous.FinalStatus = "Replaced";
                data.Orders.Add(previous);
                if (!string.IsNullOrWhiteSpace(previous.OrderId))
                {
                    activeOrdersById.Remove(previous.OrderId);
                }
            }

            var newOrder = new OrderEntry(timestamp, price, side.Value, symbol, orderId, status);
            activeSlots[slotKey] = newOrder;
            if (!string.IsNullOrWhiteSpace(orderId) && orderId != "0")
            {
                activeOrdersById[orderId] = newOrder;
            }
        }
        else if (status is "PartiallyFilled" or "Untriggered" or "Triggered")
        {
            if (!string.IsNullOrWhiteSpace(orderId) && activeOrdersById.TryGetValue(orderId, out var target))
            {
                target.Status = status;
            }
        }
        else if (status is "Filled" or "Cancelled" or "Canceled" or "Rejected")
        {
            var target = FindOrder(orderId, side, symbol, activeSlots, activeOrdersById);
            if (target is null)
            {
                return;
            }

            target.EndTime = timestamp;
            target.FinalStatus = status;
            data.Orders.Add(target);
            if (!string.IsNullOrWhiteSpace(target.OrderId))
            {
                activeOrdersById.Remove(target.OrderId);
            }

            activeSlots.Remove((target.Symbol, target.Side));
        }
    }

    private static OrderEntry? FindOrder(
        string orderId,
        OrderSide? side,
        string symbol,
        Dictionary<(string Symbol, OrderSide Side), OrderEntry> activeSlots,
        Dictionary<string, OrderEntry> activeOrdersById)
    {
        if (!string.IsNullOrWhiteSpace(orderId) && activeOrdersById.TryGetValue(orderId, out var target))
        {
            return target;
        }

        if (side.HasValue && activeSlots.TryGetValue((symbol, side.Value), out target))
        {
            return target;
        }

        return null;
    }

    private static void ParseTrade(string[] parts, DateTime timestamp, ParsedData data)
    {
        if (parts.Length < 6)
        {
            return;
        }

        var symbol = parts[2].Trim();
        if (!TryParseDouble(parts[3], out var price))
        {
            return;
        }

        var side = ParseSide(parts[5]);
        if (side is null)
        {
            return;
        }

        data.Trades.Add(new TradeEntry(timestamp, price, side.Value, symbol));
    }

    private static void ParseBorder(string[] parts, DateTime timestamp, ParsedData data)
    {
        if (parts.Length < 6)
        {
            return;
        }

        if (!TryParseDouble(parts[2], out var b1) || !TryParseDouble(parts[3], out var b2)
            || !TryParseDouble(parts[4], out var b3) || !TryParseDouble(parts[5], out var b4))
        {
            return;
        }

        data.Borders.Add(new BorderEntry(timestamp, b1, b2, b3, b4));
    }

    private static void ParseSpread(string[] parts, DateTime timestamp, ParsedData data)
    {
        if (parts.Length < 3)
        {
            return;
        }

        if (!TryParseDouble(parts[2], out var s1))
        {
            return;
        }

        var s2 = s1;
        if (parts.Length > 3 && TryParseDouble(parts[3], out var parsedS2))
        {
            s2 = parsedS2;
        }

        data.Spreads.Add(new SpreadEntry(timestamp, s1, s2));
    }

    private static OrderSide? ParseSide(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "buy" => OrderSide.Buy,
            "sell" => OrderSide.Sell,
            _ => null
        };
    }

    private static bool TryParseTimestamp(string raw, TimeState state, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var timeParts = raw.Split('.');
        var hms = timeParts[0].Split(':');
        if (hms.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(hms[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(hms[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(hms[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
        {
            return false;
        }

        var microSeconds = 0;
        if (timeParts.Length > 1)
        {
            var fraction = timeParts[1];
            if (fraction.Length > 6)
            {
                fraction = fraction[..6];
            }

            if (!int.TryParse(fraction.PadRight(6, '0'), NumberStyles.Integer, CultureInfo.InvariantCulture, out microSeconds))
            {
                return false;
            }
        }

        var timeSpan = new TimeSpan(0, h, m, s).Add(TimeSpan.FromTicks(microSeconds * 10L));
        var currentRawDt = state.BaseDate.Add(timeSpan);

        if (state.LastRawDt.HasValue)
        {
            var delta = currentRawDt - state.LastRawDt.Value;
            var deltaSeconds = delta.TotalSeconds;
            if (deltaSeconds < -36000)
            {
                if (deltaSeconds is > -46800 and < -39600)
                {
                    state.Offset += TimeSpan.FromHours(12);
                }
                else if (deltaSeconds < -80000)
                {
                    state.Offset += TimeSpan.FromHours(24);
                }
            }
        }

        state.LastRawDt = currentRawDt;
        timestamp = currentRawDt + state.Offset;
        state.LastAdjustedDt = timestamp;
        return true;
    }

    private static bool TryParseDouble(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
