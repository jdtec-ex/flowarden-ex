using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Flowarden.Ui.Models;

namespace Flowarden.Ui.ViewModels.Overview;

internal static class OverviewChartPaths
{
    private const double Width = 640;
    private const double Height = 84;

    public static ulong CalculateMaxTimelineValue(IReadOnlyList<TimelinePointDto> timelinePoints)
    {
        var max = 0UL;
        for (var i = 0; i < timelinePoints.Count; i++)
        {
            var point = timelinePoints[i];
            if (point.InboundBytes > max)
            {
                max = point.InboundBytes;
            }

            if (point.OutboundBytes > max)
            {
                max = point.OutboundBytes;
            }
        }

        return max;
    }

    public static string BuildTimelinePath(
        IReadOnlyList<TimelinePointDto> timelinePoints,
        ulong maxTimelineValue,
        bool selectOutbound
    )
    {
        if (timelinePoints.Count == 0)
        {
            return string.Empty;
        }

        var maxValue = (double)Math.Max(maxTimelineValue, 1);

        if (timelinePoints.Count == 1)
        {
            var point = timelinePoints[0];
            var value = selectOutbound ? point.OutboundBytes : point.InboundBytes;
            var y = Height - ((double)value / maxValue * Height);
            return FormattableString.Invariant($"M 0,{y:0.##} L {Width:0.##},{y:0.##}");
        }

        var step = Width / (timelinePoints.Count - 1);
        var coordinates = new List<(double X, double Y)>(timelinePoints.Count);

        for (var i = 0; i < timelinePoints.Count; i++)
        {
            var point = timelinePoints[i];
            var value = selectOutbound ? point.OutboundBytes : point.InboundBytes;
            var x = i * step;
            var y = Height - ((double)value / maxValue * Height);
            coordinates.Add((x, y));
        }

        return BuildSmoothPath(coordinates);
    }

    public static string BuildAreaPath(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        return $"{line} L {Width.ToString(CultureInfo.InvariantCulture)},{Height.ToString(CultureInfo.InvariantCulture)} L 0,{Height.ToString(CultureInfo.InvariantCulture)} Z";
    }

    private static string BuildSmoothPath(IReadOnlyList<(double X, double Y)> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return string.Empty;
        }

        if (coordinates.Count == 1)
        {
            return FormattableString.Invariant($"M {coordinates[0].X:0.##},{coordinates[0].Y:0.##}");
        }

        if (coordinates.Count == 2)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"M {coordinates[0].X:0.##},{coordinates[0].Y:0.##} L {coordinates[1].X:0.##},{coordinates[1].Y:0.##}"
            );
        }

        var builder = new StringBuilder();
        builder.Append(FormattableString.Invariant($"M {coordinates[0].X:0.##},{coordinates[0].Y:0.##}"));

        for (var i = 0; i < coordinates.Count - 1; i++)
        {
            var previous = i == 0 ? coordinates[i] : coordinates[i - 1];
            var start = coordinates[i];
            var end = coordinates[i + 1];
            var next = i + 2 < coordinates.Count ? coordinates[i + 2] : coordinates[i + 1];

            var firstControlX = start.X + (end.X - previous.X) / 6d;
            var firstControlY = start.Y + (end.Y - previous.Y) / 6d;
            var secondControlX = end.X - (next.X - start.X) / 6d;
            var secondControlY = end.Y - (next.Y - start.Y) / 6d;

            builder.Append(
                FormattableString.Invariant(
                    $" C {firstControlX:0.##},{firstControlY:0.##} {secondControlX:0.##},{secondControlY:0.##} {end.X:0.##},{end.Y:0.##}"
                )
            );
        }

        return builder.ToString();
    }
}
