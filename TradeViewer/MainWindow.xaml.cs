using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.PointMarkers;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Charting.Visuals.RenderableSeries.DataPointSelection;
using SciChart.Data.Model;
using TradeViewer.Models;
using TradeViewer.Parsing;

namespace TradeViewer;

public partial class MainWindow : Window
{
    private const string DefaultFilePath = "C:\\Path\\To\\Cache.csv";

    private readonly RangeSynchronizer _rangeSynchronizer = new();

    public MainWindow()
    {
        InitializeComponent();
        ConfigureAxes();
        LoadFile(DefaultFilePath);
    }

    private void ConfigureAxes()
    {
        _rangeSynchronizer.AttachAxis(PriceXAxis);
        _rangeSynchronizer.AttachAxis(SpreadXAxis);
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

        PriceSurface.RenderableSeries.Clear();
        SpreadSurface.RenderableSeries.Clear();

        AddPriceSeries(data);
        AddOrderSeries(data);
        AddTradeSeries(data);
        AddSpreadSeries(data);
        AddBorderSeries(data);

        PriceSurface.ZoomExtents();
        SpreadSurface.ZoomExtents();
    }

    private void AddPriceSeries(ParsedData data)
    {
        if (data.SpotPrices.Count > 0)
        {
            var spotBid = CreateDataSeries("Spot Bid", data.SpotPrices.Select(p => (p.Time, p.Bid)));
            var spotAsk = CreateDataSeries("Spot Ask", data.SpotPrices.Select(p => (p.Time, p.Ask)));

            PriceSurface.RenderableSeries.Add(CreateLineSeries(spotBid, ColorFromHex("#228B22"), dash: true));
            PriceSurface.RenderableSeries.Add(CreateLineSeries(spotAsk, ColorFromHex("#B22222"), dash: true));
        }

        if (data.LinearPrices.Count > 0)
        {
            var futuresBid = CreateDataSeries("Futures Bid", data.LinearPrices.Select(p => (p.Time, p.Bid)));
            var futuresAsk = CreateDataSeries("Futures Ask", data.LinearPrices.Select(p => (p.Time, p.Ask)));

            PriceSurface.RenderableSeries.Add(CreateLineSeries(futuresBid, ColorFromHex("#00AA00")));
            PriceSurface.RenderableSeries.Add(CreateLineSeries(futuresAsk, ColorFromHex("#FF0000")));
        }
    }

    private void AddOrderSeries(ParsedData data)
    {
        if (data.Orders.Count == 0)
        {
            return;
        }

        var buySeries = new XyDataSeries<DateTime, double> { SeriesName = "My Buy", AcceptsUnsortedData = true };
        var sellSeries = new XyDataSeries<DateTime, double> { SeriesName = "My Sell", AcceptsUnsortedData = true };

        foreach (var order in data.Orders)
        {
            var target = order.Side == OrderSide.Buy ? buySeries : sellSeries;
            target.Append(order.StartTime, order.Price);
            target.Append(order.EndTime, order.Price);
            target.Append(order.EndTime, double.NaN);
        }

        if (buySeries.Count > 0)
        {
            PriceSurface.RenderableSeries.Add(CreateLineSeries(buySeries, ColorFromHex("#008000"), 3,
                new EllipsePointMarker { Width = 6, Height = 6, Fill = Brushes.LimeGreen }));
        }

        if (sellSeries.Count > 0)
        {
            PriceSurface.RenderableSeries.Add(CreateLineSeries(sellSeries, ColorFromHex("#CC0000"), 3,
                new EllipsePointMarker { Width = 6, Height = 6, Fill = Brushes.OrangeRed }));
        }
    }

    private void AddTradeSeries(ParsedData data)
    {
        var buys = data.Trades.Where(t => t.Side == OrderSide.Buy).ToList();
        var sells = data.Trades.Where(t => t.Side == OrderSide.Sell).ToList();

        if (buys.Count > 0)
        {
            var series = CreateDataSeries("Trade Buy", buys.Select(t => (t.Time, t.Price)));
            PriceSurface.RenderableSeries.Add(new XyScatterRenderableSeries
            {
                DataSeries = series,
                PointMarker = new TrianglePointMarker
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.LimeGreen,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    RotationAngle = 0
                }
            });
        }

        if (sells.Count > 0)
        {
            var series = CreateDataSeries("Trade Sell", sells.Select(t => (t.Time, t.Price)));
            PriceSurface.RenderableSeries.Add(new XyScatterRenderableSeries
            {
                DataSeries = series,
                PointMarker = new TrianglePointMarker
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.OrangeRed,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    RotationAngle = 180
                }
            });
        }
    }

    private void AddBorderSeries(ParsedData data)
    {
        if (data.Borders.Count == 0)
        {
            return;
        }

        SpreadSurface.RenderableSeries.Add(CreateLineSeries(
            CreateDataSeries("B0", data.Borders.Select(b => (b.Time, b.B1))),
            ColorFromHex("#8B4500"), 1.5));
        SpreadSurface.RenderableSeries.Add(CreateLineSeries(
            CreateDataSeries("B1", data.Borders.Select(b => (b.Time, b.B2))),
            ColorFromHex("#FF8C00"), 1.5));
        SpreadSurface.RenderableSeries.Add(CreateLineSeries(
            CreateDataSeries("B2", data.Borders.Select(b => (b.Time, b.B3))),
            ColorFromHex("#008080"), 1.5));
        SpreadSurface.RenderableSeries.Add(CreateLineSeries(
            CreateDataSeries("B3", data.Borders.Select(b => (b.Time, b.B4))),
            ColorFromHex("#00008B"), 1.5));
    }

    private void AddSpreadSeries(ParsedData data)
    {
        if (data.Spreads.Count == 0)
        {
            return;
        }

        SpreadSurface.RenderableSeries.Add(CreateLineSeries(
            CreateDataSeries("S0", data.Spreads.Select(s => (s.Time, s.S1))),
            ColorFromHex("#1f77b4"), 1.5));
        SpreadSurface.RenderableSeries.Add(CreateLineSeries(
            CreateDataSeries("S1", data.Spreads.Select(s => (s.Time, s.S2))),
            ColorFromHex("#9467bd"), 1.5));
    }

    private static XyDataSeries<DateTime, double> CreateDataSeries(string name, IEnumerable<(DateTime Time, double Value)> points)
    {
        var series = new XyDataSeries<DateTime, double> { SeriesName = name, AcceptsUnsortedData = true };
        foreach (var (time, value) in points)
        {
            series.Append(time, value);
        }

        return series;
    }

    private static FastLineRenderableSeries CreateLineSeries(
        IDataSeries series,
        Color color,
        double thickness = 1,
        IPointMarker? marker = null,
        bool dash = false)
    {
        var line = new FastLineRenderableSeries
        {
            DataSeries = series,
            Stroke = color,
            StrokeThickness = thickness,
            PointMarker = marker
        };

        if (dash)
        {
            line.StrokeDashArray = new DoubleCollection { 2, 2 };
        }

        return line;
    }

    private static Color ColorFromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
