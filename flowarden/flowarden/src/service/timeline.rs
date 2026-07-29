//! Overview timeline construction for live and offline captures.

use flowarden_core::flow::{OfflineGap, PacketTimestamp, TickSnapshot};

use super::{
    constants::OFFLINE_TIMELINE_POINTS,
    proto::projection::{PacketTimestamp as ProtoPacketTimestamp, ProjectionMode, TimelinePoint},
    state::OverviewRuntimeSnapshot,
};

fn packet_timestamp_to_proto(timestamp: PacketTimestamp) -> ProtoPacketTimestamp {
    ProtoPacketTimestamp {
        seconds: timestamp.seconds,
        microseconds: timestamp.microseconds,
    }
}

pub(crate) fn timeline_point_from_tick(tick: &TickSnapshot) -> TimelinePoint {
    let (inbound_bytes, outbound_bytes) = tick_timeline_bytes(tick);

    TimelinePoint {
        timestamp: Some(packet_timestamp_to_proto(tick.timestamp)),
        inbound_bytes,
        outbound_bytes,
    }
}

pub(crate) fn tick_timeline_bytes(tick: &TickSnapshot) -> (u64, u64) {
    let inbound_bytes = tick
        .top_connections
        .iter()
        .map(|connection| connection.counters.bytes_in)
        .sum();
    let outbound_bytes = tick
        .top_connections
        .iter()
        .map(|connection| connection.counters.bytes_out)
        .sum();

    (inbound_bytes, outbound_bytes)
}

pub(crate) fn timeline_points_for_snapshot(
    snapshot: &OverviewRuntimeSnapshot,
) -> Vec<TimelinePoint> {
    if matches!(snapshot.mode, ProjectionMode::Offline) {
        return compressed_offline_timeline_points(
            &snapshot.tick_snapshots,
            &snapshot.offline_gaps,
        );
    }

    timeline_points_from_ticks_and_gaps(&snapshot.tick_snapshots, &snapshot.offline_gaps)
}

pub(crate) fn timeline_points_from_ticks_and_gaps(
    ticks: &[TickSnapshot],
    gaps: &[OfflineGap],
) -> Vec<TimelinePoint> {
    let mut points = Vec::new();

    for tick in ticks {
        points.push(timeline_point_from_tick(tick));
        for gap in gaps
            .iter()
            .filter(|gap| gap.after == tick.timestamp && gap.seconds > 0)
        {
            let gap_end = tick.timestamp.seconds + i64::from(gap.seconds);
            points.push(TimelinePoint {
                timestamp: Some(packet_timestamp_to_proto(PacketTimestamp::tick(gap_end))),
                inbound_bytes: 0,
                outbound_bytes: 0,
            });
        }
    }

    points
}

pub(crate) fn compressed_offline_timeline_points(
    ticks: &[TickSnapshot],
    gaps: &[OfflineGap],
) -> Vec<TimelinePoint> {
    let Some((start_second, end_second)) = offline_timeline_range(ticks, gaps) else {
        return Vec::new();
    };

    let duration = i128::from(end_second) - i128::from(start_second);
    let natural_points = duration.saturating_add(1);
    let point_count = if natural_points <= OFFLINE_TIMELINE_POINTS as i128 {
        natural_points as usize
    } else {
        OFFLINE_TIMELINE_POINTS
    };

    let mut points = (0..point_count)
        .map(|index| TimelinePoint {
            timestamp: Some(packet_timestamp_to_proto(PacketTimestamp::tick(
                offline_bucket_timestamp(start_second, end_second, index, point_count),
            ))),
            inbound_bytes: 0,
            outbound_bytes: 0,
        })
        .collect::<Vec<_>>();

    for tick in ticks {
        let index = offline_bucket_index(
            tick.timestamp.seconds,
            start_second,
            end_second,
            point_count,
        );
        let (inbound_bytes, outbound_bytes) = tick_timeline_bytes(tick);
        if let Some(point) = points.get_mut(index) {
            point.inbound_bytes = point.inbound_bytes.saturating_add(inbound_bytes);
            point.outbound_bytes = point.outbound_bytes.saturating_add(outbound_bytes);
        }
    }

    points
}

pub(crate) fn offline_timeline_range(
    ticks: &[TickSnapshot],
    gaps: &[OfflineGap],
) -> Option<(i64, i64)> {
    let mut start_second = ticks.iter().map(|tick| tick.timestamp.seconds).min()?;
    let mut end_second = ticks.iter().map(|tick| tick.timestamp.seconds).max()?;

    for gap in gaps.iter().filter(|gap| gap.seconds > 0) {
        start_second = start_second.min(gap.after.seconds);
        end_second = end_second.max(gap.after.seconds.saturating_add(i64::from(gap.seconds)));
    }

    Some((start_second, end_second.max(start_second)))
}

pub(crate) fn offline_bucket_timestamp(
    start_second: i64,
    end_second: i64,
    index: usize,
    point_count: usize,
) -> i64 {
    if point_count <= 1 || end_second <= start_second {
        return start_second;
    }
    if index == point_count - 1 {
        return end_second;
    }

    let duration = i128::from(end_second) - i128::from(start_second);
    let offset = duration * index as i128 / (point_count - 1) as i128;
    (i128::from(start_second) + offset).clamp(i128::from(i64::MIN), i128::from(i64::MAX)) as i64
}

pub(crate) fn offline_bucket_index(
    timestamp_second: i64,
    start_second: i64,
    end_second: i64,
    point_count: usize,
) -> usize {
    if point_count <= 1 || end_second <= start_second {
        return 0;
    }

    let clamped_second = timestamp_second.clamp(start_second, end_second);
    let duration = i128::from(end_second) - i128::from(start_second);
    let offset = i128::from(clamped_second) - i128::from(start_second);
    ((offset * (point_count - 1) as i128) / duration) as usize
}
