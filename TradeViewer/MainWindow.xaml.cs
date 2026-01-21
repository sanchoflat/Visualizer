using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Plottable;
using TradeViewer.Models;
using TradeViewer.Parsing;

namespace TradeViewer;

public partial class MainWindow : Window
{
    private const string DefaultFilePath = "C:\\Path\\To\\Cache.csv";

    public MainWindow()
    {
        InitializeComponent();
        ConfigurePlots();
        LoadFile(DefaultFilePath);
    }

    private void ConfigurePlots()
    {
        ApplyPlotStyle(PricePlot, "Prices & Orders", "Price");
        ApplyPlotStyle(SpreadPlot, "Spreads & Borders", "Spread");
    }

    private void ApplyPlotStyle(ScottPlot.WPF.WpfPlot plot, string title, string yLabel)
    {
        plot.Plot.Style(Style.Black);
        plot.Plot.Title(title, color: Color.White);
        plot.Plot.YLabel(yLabel, color: Color.White);
        plot.Plot.XAxis.DateTimeFormat(true);
        plot.Plot.XAxis.Color(Color.White);
        plot.Plot.YAxis.Color(Color.White);
        plot.Plot.Legend(location: Alignment.UpperRight);
    }

    private void OnLoadClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV or log files (*.csv;*.log)|*.csv;*.log|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadFile(dialog.FileName);
        }
    }

    private void LoadFile(string path)
    {
        FilePathBox.Text = path;
        var data = LogParser.Parse(path);
        if (data is null)
        {
            MessageBox.Show(this, "File not found or could not be parsed.", "Load error", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        PricePlot.Plot.Clear();
        SpreadPlot.Plot.Clear();

        ConfigurePlots();
        AddPriceSeries(data);
        AddOrderSeries(data);
        AddTradeSeries(data);
        AddSpreadSeries(data);
        AddBorderSeries(data);
        SyncXAxisLimits(data);

        PricePlot.Refresh();
        SpreadPlot.Refresh();
    }

    private void SyncXAxisLimits(ParsedData data)
    {
        var allTimes = data.SpotPrices.Select(p => p.Time)
            .Concat(data.LinearPrices.Select(p => p.Time))
            .Concat(data.Orders.Select(o => o.StartTime))
            .Concat(data.Orders.Select(o => o.EndTime))
            .Concat(data.Trades.Select(t => t.Time))
            .Concat(data.Spreads.Select(s => s.Time))
            .Concat(data.Borders.Select(b => b.Time))
            .ToList();

        if (allTimes.Count == 0)
        {
            return;
        }

        var min = allTimes.Min();
        var max = allTimes.Max();
        var minX = min.ToOADate();
        var maxX = max.ToOADate();

        PricePlot.Plot.SetAxisLimitsX(minX, maxX);
        SpreadPlot.Plot.SetAxisLimitsX(minX, maxX);
    }

    private void AddPriceSeries(ParsedData data)
    {
        if (data.SpotPrices.Count > 0)
        {
            AddLineSeries(PricePlot.Plot, "Spot Bid", data.SpotPrices.Select(p => p.Time).ToArray(),
                data.SpotPrices.Select(p => p.Bid).ToArray(), Color.FromArgb(34, 139, 34), LineStyle.Dash);
            AddLineSeries(PricePlot.Plot, "Spot Ask", data.SpotPrices.Select(p => p.Time).ToArray(),
                data.SpotPrices.Select(p => p.Ask).ToArray(), Color.FromArgb(178, 34, 34), LineStyle.Dash);
        }

        if (data.LinearPrices.Count > 0)
        {
            AddLineSeries(PricePlot.Plot, "Futures Bid", data.LinearPrices.Select(p => p.Time).ToArray(),
                data.LinearPrices.Select(p => p.Bid).ToArray(), Color.FromArgb(0, 170, 0), LineStyle.Solid);
            AddLineSeries(PricePlot.Plot, "Futures Ask", data.LinearPrices.Select(p => p.Time).ToArray(),
                data.LinearPrices.Select(p => p.Ask).ToArray(), Color.FromArgb(255, 0, 0), LineStyle.Solid);
        }
    }

    private void AddOrderSeries(ParsedData data)
    {
        if (data.Orders.Count == 0)
        {
            return;
        }

        var buyPoints = new List<(DateTime Time, double Value)>();
        var sellPoints = new List<(DateTime Time, double Value)>();

        foreach (var order in data.Orders)
        {
            var target = order.Side == OrderSide.Buy ? buyPoints : sellPoints;
            target.Add((order.StartTime, order.Price));
            target.Add((order.EndTime, order.Price));
            target.Add((order.EndTime, double.NaN));
        }

        if (buyPoints.Count > 0)
        {
            AddLineSeries(PricePlot.Plot, "My Buy", buyPoints.Select(p => p.Time).ToArray(),
                buyPoints.Select(p => p.Value).ToArray(), Color.FromArgb(0, 128, 0), LineStyle.Solid, 3,
                MarkerShape.filledCircle, 5);
        }

        if (sellPoints.Count > 0)
        {
            AddLineSeries(PricePlot.Plot, "My Sell", sellPoints.Select(p => p.Time).ToArray(),
                sellPoints.Select(p => p.Value).ToArray(), Color.FromArgb(204, 0, 0), LineStyle.Solid, 3,
                MarkerShape.filledCircle, 5);
        }
    }

    private void AddTradeSeries(ParsedData data)
    {
        var buys = data.Trades.Where(t => t.Side == OrderSide.Buy).ToList();
        var sells = data.Trades.Where(t => t.Side == OrderSide.Sell).ToList();

        if (buys.Count > 0)
        {
            var scatter = PricePlot.Plot.AddScatter(
                buys.Select(t => t.Time).ToArray(),
                buys.Select(t => t.Price).ToArray(),
                color: Color.LimeGreen,
                lineWidth: 0,
                markerSize: 10,
                markerShape: MarkerShape.filledTriangleUp);
            scatter.Label = "Trade Buy";
        }

        if (sells.Count > 0)
        {
            var scatter = PricePlot.Plot.AddScatter(
                sells.Select(t => t.Time).ToArray(),
                sells.Select(t => t.Price).ToArray(),
                color: Color.OrangeRed,
                lineWidth: 0,
                markerSize: 10,
                markerShape: MarkerShape.filledTriangleDown);
            scatter.Label = "Trade Sell";
        }
    }

    private void AddBorderSeries(ParsedData data)
    {
        if (data.Borders.Count == 0)
        {
            return;
        }

        AddLineSeries(SpreadPlot.Plot, "B0", data.Borders.Select(b => b.Time).ToArray(),
            data.Borders.Select(b => b.B1).ToArray(), Color.FromArgb(139, 69, 0), LineStyle.Solid, 2);
        AddLineSeries(SpreadPlot.Plot, "B1", data.Borders.Select(b => b.Time).ToArray(),
            data.Borders.Select(b => b.B2).ToArray(), Color.FromArgb(255, 140, 0), LineStyle.Solid, 2);
        AddLineSeries(SpreadPlot.Plot, "B2", data.Borders.Select(b => b.Time).ToArray(),
            data.Borders.Select(b => b.B3).ToArray(), Color.FromArgb(0, 128, 128), LineStyle.Solid, 2);
        AddLineSeries(SpreadPlot.Plot, "B3", data.Borders.Select(b => b.Time).ToArray(),
            data.Borders.Select(b => b.B4).ToArray(), Color.FromArgb(0, 0, 139), LineStyle.Solid, 2);
    }

    private void AddSpreadSeries(ParsedData data)
    {
        if (data.Spreads.Count == 0)
        {
            return;
        }

        AddLineSeries(SpreadPlot.Plot, "S0", data.Spreads.Select(s => s.Time).ToArray(),
            data.Spreads.Select(s => s.S1).ToArray(), Color.FromArgb(31, 119, 180), LineStyle.Solid, 2);
        AddLineSeries(SpreadPlot.Plot, "S1", data.Spreads.Select(s => s.Time).ToArray(),
            data.Spreads.Select(s => s.S2).ToArray(), Color.FromArgb(148, 103, 189), LineStyle.Solid, 2);
    }

    private static ScatterPlot AddLineSeries(
        Plot plot,
        string label,
        DateTime[] xs,
        double[] ys,
        Color color,
        LineStyle lineStyle,
        double lineWidth = 1,
        MarkerShape markerShape = MarkerShape.none,
        double markerSize = 0)
    {
        var scatter = plot.AddScatter(xs, ys, color: color, lineWidth: lineWidth, markerSize: markerSize,
            markerShape: markerShape, lineStyle: lineStyle);
        scatter.Label = label;
        return scatter;
    }
}
