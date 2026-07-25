using System;
using System.Globalization;
using System.IO;

namespace Flowarden.Ui.ViewModels.Source;

internal static class SourceFormatting
{
    public static string FormatNumber(ulong value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

    public static string FormatOfflineDisplayName(string path)
        {
            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }

    public static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1_073_741_824)
            {
                return $"{bytes / 1_073_741_824d:0.##} GB";
            }
    
            if (bytes >= 1_048_576)
            {
                return $"{bytes / 1_048_576d:0.##} MB";
            }
    
            if (bytes >= 1024)
            {
                return $"{bytes / 1024d:0.##} KB";
            }
    
            return $"{bytes} B";
        }

    public static string FormatBitRate(ulong bytes, ulong seconds)
        {
            if (seconds == 0)
            {
                return "0 bps";
            }
    
            var bitsPerSecond = bytes * 8d / seconds;
            if (bitsPerSecond >= 1_000_000)
            {
                return $"{bitsPerSecond / 1_000_000d:0.##} Mbps";
            }
    
            if (bitsPerSecond >= 1_000)
            {
                return $"{bitsPerSecond / 1_000d:0.##} Kbps";
            }
    
            return $"{bitsPerSecond:0} bps";
        }
}
