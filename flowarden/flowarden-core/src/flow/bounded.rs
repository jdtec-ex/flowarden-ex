//! Soft-cap map helpers for resident aggregation.
//!
//! Policy: existing keys always update; new keys at capacity replace the current
//! minimum-byte entry only when the candidate has strictly more bytes.

use std::{collections::HashMap, hash::Hash};

/// Update an existing map entry, or insert a new one under an optional byte soft-cap.
pub fn upsert_by_bytes<K, V>(
    map: &mut HashMap<K, V>,
    key: K,
    cap: Option<usize>,
    bytes_of: impl Fn(&V) -> u64,
    update_existing: impl FnOnce(&mut V),
    create: impl FnOnce() -> V,
) where
    K: Eq + Hash + Clone,
{
    if let Some(entry) = map.get_mut(&key) {
        update_existing(entry);
        return;
    }

    let value = create();
    insert_or_replace_min_by_bytes(map, key, value, cap, bytes_of);
}

/// Insert `value` for a new key, respecting an optional soft-cap by bytes.
pub fn insert_or_replace_min_by_bytes<K, V>(
    map: &mut HashMap<K, V>,
    key: K,
    value: V,
    cap: Option<usize>,
    bytes_of: impl Fn(&V) -> u64,
) where
    K: Eq + Hash + Clone,
{
    match cap {
        None => {
            map.insert(key, value);
        }
        Some(limit) if map.len() < limit => {
            map.insert(key, value);
        }
        Some(_) => {
            let new_bytes = bytes_of(&value);
            let Some(victim_key) = min_bytes_key(map, &bytes_of) else {
                map.insert(key, value);
                return;
            };
            let min_bytes = map.get(&victim_key).map(&bytes_of).unwrap_or(0);
            if new_bytes > min_bytes {
                map.remove(&victim_key);
                map.insert(key, value);
            }
        }
    }
}

fn min_bytes_key<K, V>(map: &HashMap<K, V>, bytes_of: &impl Fn(&V) -> u64) -> Option<K>
where
    K: Eq + Hash + Clone,
{
    map.iter()
        .min_by(|(_, left), (_, right)| bytes_of(left).cmp(&bytes_of(right)))
        .map(|(key, _)| key.clone())
}

/// Rank items by bytes desc, then packets desc, then a stable key comparator.
pub fn rank_by_traffic<T>(
    mut items: Vec<T>,
    bytes: impl Fn(&T) -> u64,
    packets: impl Fn(&T) -> u64,
    key_cmp: impl Fn(&T, &T) -> std::cmp::Ordering,
    limit: Option<usize>,
) -> Vec<T> {
    items.sort_by(|left, right| {
        bytes(right)
            .cmp(&bytes(left))
            .then_with(|| packets(right).cmp(&packets(left)))
            .then_with(|| key_cmp(left, right))
    });
    if let Some(limit) = limit {
        items.truncate(limit);
    }
    items
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn forensic_cap_none_keeps_all_keys() {
        let mut map = HashMap::new();
        for index in 0_u64..5 {
            upsert_by_bytes(
                &mut map,
                index,
                None,
                |value| *value,
                |value| *value += 1,
                || 10 + index,
            );
        }
        assert_eq!(map.len(), 5);
    }

    #[test]
    fn resident_cap_replaces_lightest_with_heavier() {
        let mut map = HashMap::new();
        upsert_by_bytes(&mut map, "a", Some(2), |v| *v, |_| {}, || 10_u64);
        upsert_by_bytes(&mut map, "b", Some(2), |v| *v, |_| {}, || 20_u64);
        upsert_by_bytes(&mut map, "c", Some(2), |v| *v, |_| {}, || 30_u64);
        upsert_by_bytes(&mut map, "d", Some(2), |v| *v, |_| {}, || 5_u64);

        assert_eq!(map.len(), 2);
        assert!(map.contains_key("c"));
        assert!(map.values().any(|v| *v == 20) || map.values().any(|v| *v == 30));
        assert!(!map.contains_key("d"));
        assert_eq!(map.get("c"), Some(&30));
    }

    #[test]
    fn existing_key_updates_even_at_capacity() {
        let mut map = HashMap::new();
        upsert_by_bytes(&mut map, "a", Some(1), |v| *v, |_| {}, || 10_u64);
        upsert_by_bytes(&mut map, "a", Some(1), |v| *v, |v| *v += 5, || 0_u64);
        assert_eq!(map.len(), 1);
        assert_eq!(map.get("a"), Some(&15));
    }

    #[test]
    fn rank_by_traffic_orders_and_limits() {
        let ranked = rank_by_traffic(
            vec![("a", 10_u64, 2_u64), ("b", 30, 1), ("c", 30, 5)],
            |item| item.1,
            |item| item.2,
            |left, right| left.0.cmp(right.0),
            Some(2),
        );
        assert_eq!(ranked, vec![("c", 30, 5), ("b", 30, 1)]);
    }
}
