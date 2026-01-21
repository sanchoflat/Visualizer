# Trade Viewer (SciChart WPF)

This repository now contains a WPF application that mirrors the Python Plotly chart using SciChart.

## Requirements

- Windows with .NET 8 SDK
- A valid SciChart WPF license key (set `SCICHART_LICENSE_KEY` in your environment)

## Run

1. Restore packages:

```bash
dotnet restore TradeViewer/TradeViewer.csproj
```

2. Start the app:

```bash
dotnet run --project TradeViewer/TradeViewer.csproj
```

3. Click **Load** and select your CSV/log file.

## Notes

- The parser expects the same pipe-delimited log format as the original Python script.
- Update the default file path in `MainWindow.xaml.cs` if you want a different initial file.
