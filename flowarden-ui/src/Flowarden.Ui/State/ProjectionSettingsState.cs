using System;

namespace Flowarden.Ui.State;

public sealed class ProjectionSettingsState
{
    public const uint DefaultTopN = 10;
    public const uint MinTopN = 1;
    public const uint MaxTopN = 100;

    private uint _topN = DefaultTopN;

    public event Action<uint>? TopNChanged;

    public uint TopN => _topN;

    public void SetTopN(uint topN)
    {
        var normalized = NormalizeTopN(topN);
        if (normalized == _topN)
        {
            return;
        }

        _topN = normalized;
        TopNChanged?.Invoke(_topN);
    }

    public static uint NormalizeTopN(uint topN)
    {
        if (topN < MinTopN)
        {
            return MinTopN;
        }

        if (topN > MaxTopN)
        {
            return MaxTopN;
        }

        return topN;
    }
}
