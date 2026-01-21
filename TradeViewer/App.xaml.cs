using System;
using System.Windows;
using SciChart.Charting.Visuals;

namespace TradeViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var licenseKey = Environment.GetEnvironmentVariable("SCICHART_LICENSE_KEY");
        if (!string.IsNullOrWhiteSpace(licenseKey))
        {
            SciChartSurface.SetRuntimeLicenseKey(licenseKey);
        }
    }
}
