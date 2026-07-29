using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flowarden.Ui.Models;
using Flowarden.Ui.ViewModels;

namespace Flowarden.Ui.ViewModels.Overview;

internal static class OverviewMapProjection
{
    public static IReadOnlyList<OverviewRegionMarkerViewModel> BuildTopRegionMarkers(
        IReadOnlyList<DestinationSummaryDto> rows
    )
    {
        var maxBytes = rows.Count == 0 ? 0 : rows.Max(row => row.Bytes);
        return rows
            .Select(row => CreateRegionMarker(row, maxBytes))
            .Where(marker => marker is not null)
            .Cast<OverviewRegionMarkerViewModel>()
            .ToArray();
    }

    private static OverviewRegionMarkerViewModel? CreateRegionMarker(
        DestinationSummaryDto row,
        ulong maxBytes
    )
    {
        var countryCode = string.IsNullOrWhiteSpace(row.CountryCode)
            ? OverviewFormatting.ExtractOwnerCode(row.CountryLabel)
            : row.CountryCode;
        if (!TryGetCountryCoordinate(countryCode, out var longitude, out var latitude))
        {
            return null;
        }

        var (x, y) = ProjectEqualEarth(longitude, latitude);
        var normalized = maxBytes == 0 ? 0.0 : Math.Sqrt(row.Bytes / (double)maxBytes);
        var size = 7.0 + normalized * 11.0;
        var label = string.IsNullOrWhiteSpace(row.Label) ? row.CountryLabel : row.Label;
        var pivot = string.IsNullOrWhiteSpace(countryCode) ? label : countryCode;
        return new OverviewRegionMarkerViewModel(
            label,
            row.Ratio.ToString("P0", CultureInfo.InvariantCulture),
            OverviewFormatting.FormatBytes(row.Bytes),
            x - size / 2,
            y - size / 2,
            size,
            OverviewRankingsBuilder.DataAccentBrush,
            pivotValue: pivot,
            shortLabel: string.IsNullOrWhiteSpace(countryCode) ? label : countryCode.ToUpperInvariant()
        );
    }

    private static (double X, double Y) ProjectEqualEarth(double longitude, double latitude)
    {
        const double width = 360.0;
        const double height = 170.0;
        const double scale = 64.52285022416004;
        const double centerX = width / 2.0;
        const double centerY = height / 2.0;
        const double a1 = 1.340264;
        const double a2 = -0.081106;
        const double a3 = 0.000893;
        const double a4 = 0.003796;
        const double sqrt3 = 1.7320508075688772;
        const double sqrt3Over2 = 0.8660254037844386;

        var lambda = longitude * Math.PI / 180.0;
        var phi = latitude * Math.PI / 180.0;
        var theta = Math.Asin(sqrt3Over2 * Math.Sin(phi));
        var theta2 = theta * theta;
        var theta6 = theta2 * theta2 * theta2;
        var denominator = 3.0
            * (a1 + 3.0 * a2 * theta2 + theta6 * (7.0 * a3 + 9.0 * a4 * theta2));
        var x = (2.0 * sqrt3 * lambda * Math.Cos(theta)) / denominator;
        var y = a1 * theta + a2 * theta * theta2 + theta6 * theta * (a3 + a4 * theta2);

        return (centerX + x * scale, centerY - y * scale);
    }

    private static bool TryGetCountryCoordinate(
        string countryCode,
        out double longitude,
        out double latitude
    )
    {
        if (CountryCoordinates.TryGetValue(countryCode.Trim().ToUpperInvariant(), out var coordinate))
        {
            longitude = coordinate.Longitude;
            latitude = coordinate.Latitude;
            return true;
        }

        longitude = 0;
        latitude = 0;
        return false;
    }

    internal static readonly IReadOnlyDictionary<string, (double Longitude, double Latitude)> CountryCoordinates =
        new Dictionary<string, (double Longitude, double Latitude)>(StringComparer.OrdinalIgnoreCase)
        {
            ["AE"] = (54.20, 23.87),
            ["AF"] = (66.00, 33.84),
            ["AL"] = (20.03, 41.13),
            ["AM"] = (45.01, 40.21),
            ["AO"] = (17.47, -12.23),
            ["AR"] = (-64.75, -34.74),
            ["AT"] = (14.06, 47.62),
            ["AU"] = (134.31, -25.76),
            ["AZ"] = (47.56, 40.22),
            ["BA"] = (17.82, 44.18),
            ["BD"] = (90.28, 23.83),
            ["BE"] = (4.59, 50.65),
            ["BF"] = (-1.78, 12.31),
            ["BG"] = (25.19, 42.76),
            ["BI"] = (29.91, -3.38),
            ["BJ"] = (2.34, 9.64),
            ["BN"] = (114.92, 4.69),
            ["BO"] = (-64.65, -16.70),
            ["BR"] = (-53.17, -10.66),
            ["BS"] = (-77.93, 25.51),
            ["BT"] = (90.47, 27.43),
            ["BW"] = (23.78, -22.08),
            ["BY"] = (27.96, 53.50),
            ["BZ"] = (-88.70, 17.19),
            ["CA"] = (-96.40, 60.48),
            ["CD"] = (23.58, -2.84),
            ["CF"] = (20.37, 6.55),
            ["CG"] = (15.14, -0.84),
            ["CH"] = (8.12, 46.79),
            ["CI"] = (-5.61, 7.55),
            ["CL"] = (-71.18, -37.31),
            ["CM"] = (12.61, 5.65),
            ["CN"] = (103.45, 36.68),
            ["CO"] = (-73.07, 3.92),
            ["CR"] = (-84.17, 9.97),
            ["CU"] = (-78.93, 21.65),
            ["CY"] = (33.04, 34.91),
            ["CZ"] = (15.34, 49.78),
            ["DE"] = (10.27, 51.08),
            ["DJ"] = (42.50, 11.77),
            ["DK"] = (9.89, 56.06),
            ["DO"] = (-70.46, 18.89),
            ["DZ"] = (2.61, 28.09),
            ["EC"] = (-78.38, -1.45),
            ["EE"] = (25.83, 58.64),
            ["EG"] = (29.86, 26.47),
            ["EH"] = (-12.19, 24.28),
            ["ER"] = (38.69, 15.43),
            ["ES"] = (-3.62, 40.32),
            ["ET"] = (39.56, 8.65),
            ["FI"] = (26.14, 64.26),
            ["FJ"] = (178.57, -17.32),
            ["FK"] = (-59.42, -51.72),
            ["FR"] = (-6.80, 43.14),
            ["GA"] = (11.69, -0.65),
            ["GB"] = (-2.76, 53.81),
            ["GE"] = (43.50, 42.17),
            ["GH"] = (-1.24, 7.92),
            ["GL"] = (-41.96, 73.15),
            ["GM"] = (-15.43, 13.48),
            ["GN"] = (-11.06, 10.45),
            ["GQ"] = (10.37, 1.65),
            ["GR"] = (22.72, 39.04),
            ["GT"] = (-90.37, 15.70),
            ["GW"] = (-15.11, 12.02),
            ["GY"] = (-58.97, 4.79),
            ["HN"] = (-86.59, 14.83),
            ["HR"] = (16.57, 45.01),
            ["HT"] = (-72.66, 18.90),
            ["HU"] = (19.34, 47.20),
            ["ID"] = (117.36, -2.27),
            ["IE"] = (-8.02, 53.17),
            ["IL"] = (35.00, 31.48),
            ["IN"] = (79.54, 22.82),
            ["IQ"] = (43.79, 33.01),
            ["IR"] = (54.45, 32.47),
            ["IS"] = (-18.77, 65.08),
            ["IT"] = (12.27, 42.67),
            ["JM"] = (-77.32, 18.14),
            ["JO"] = (36.77, 31.24),
            ["JP"] = (137.71, 37.54),
            ["KE"] = (37.79, 0.60),
            ["KG"] = (74.59, 41.52),
            ["KH"] = (104.87, 12.68),
            ["KP"] = (127.13, 40.13),
            ["KR"] = (127.82, 36.42),
            ["KW"] = (47.60, 29.31),
            ["KZ"] = (67.24, 48.41),
            ["LA"] = (103.79, 18.43),
            ["LB"] = (35.87, 33.91),
            ["LK"] = (80.67, 7.70),
            ["LR"] = (-9.41, 6.43),
            ["LS"] = (28.17, -29.62),
            ["LT"] = (23.89, 55.28),
            ["LU"] = (5.97, 49.76),
            ["LV"] = (24.84, 56.82),
            ["LY"] = (18.03, 26.99),
            ["MA"] = (-8.69, 29.82),
            ["MD"] = (28.42, 47.20),
            ["ME"] = (19.29, 42.79),
            ["MG"] = (46.73, -19.30),
            ["MK"] = (21.70, 41.61),
            ["ML"] = (-3.59, 17.24),
            ["MM"] = (96.51, 20.94),
            ["MN"] = (103.02, 46.95),
            ["MR"] = (-10.35, 20.18),
            ["MW"] = (34.19, -13.16),
            ["MX"] = (-102.22, 23.91),
            ["MY"] = (109.70, 3.75),
            ["MZ"] = (35.54, -17.15),
            ["NA"] = (17.14, -22.04),
            ["NC"] = (165.53, -21.26),
            ["NE"] = (9.27, 17.34),
            ["NG"] = (7.99, 9.54),
            ["NI"] = (-85.02, 12.85),
            ["NL"] = (5.50, 52.29),
            ["NO"] = (12.83, 66.65),
            ["NP"] = (84.04, 28.25),
            ["NZ"] = (172.95, -41.55),
            ["OM"] = (56.07, 20.59),
            ["PA"] = (-80.11, 8.53),
            ["PE"] = (-74.43, -9.15),
            ["PG"] = (145.31, -6.46),
            ["PH"] = (122.94, 11.72),
            ["PK"] = (69.23, 29.91),
            ["PL"] = (19.34, 52.13),
            ["PR"] = (-66.48, 18.24),
            ["PS"] = (35.27, 31.94),
            ["PT"] = (-8.06, 39.61),
            ["PY"] = (-58.43, -23.23),
            ["QA"] = (51.18, 25.32),
            ["RO"] = (24.95, 45.85),
            ["RS"] = (20.84, 44.22),
            ["RU"] = (95.79, 66.07),
            ["RW"] = (29.92, -2.01),
            ["SA"] = (44.64, 24.09),
            ["SB"] = (159.96, -8.85),
            ["SD"] = (29.83, 15.97),
            ["SE"] = (16.11, 62.42),
            ["SI"] = (14.93, 46.13),
            ["SK"] = (19.50, 48.73),
            ["SL"] = (-11.80, 8.53),
            ["SN"] = (-14.51, 14.35),
            ["SO"] = (46.23, 9.76),
            ["SR"] = (-55.91, 4.12),
            ["SS"] = (30.20, 7.29),
            ["SV"] = (-88.87, 13.73),
            ["SY"] = (38.52, 35.01),
            ["SZ"] = (31.40, -26.49),
            ["TD"] = (18.57, 15.28),
            ["TF"] = (69.53, -49.31),
            ["TG"] = (1.00, 8.43),
            ["TH"] = (101.00, 14.98),
            ["TJ"] = (71.05, 38.59),
            ["TL"] = (125.97, -8.77),
            ["TM"] = (59.35, 39.10),
            ["TN"] = (9.54, 34.14),
            ["TR"] = (35.12, 39.15),
            ["TT"] = (-61.33, 10.43),
            ["TW"] = (120.97, 23.74),
            ["TZ"] = (34.74, -6.25),
            ["UA"] = (31.29, 49.19),
            ["UG"] = (32.36, 1.30),
            ["US"] = (-103.57, 44.76),
            ["UY"] = (-56.01, -32.77),
            ["UZ"] = (63.37, 41.77),
            ["VE"] = (-66.15, 7.16),
            ["VN"] = (106.33, 16.56),
            ["VU"] = (167.07, -15.54),
            ["XK"] = (20.90, 42.58),
            ["YE"] = (47.52, 15.92),
            ["ZA"] = (25.16, -28.92),
            ["ZM"] = (27.76, -13.39),
            ["ZW"] = (29.79, -18.90),
        };
}
