## Phase 1 Fixtures

- `offline_mixed_ethernet.pcap`
  - Link type: Ethernet
  - Packet 1: valid outbound IPv4/TCP packet to `https`
  - Packet 2: malformed packet with the same second timestamp
  - Packet 3: valid inbound IPv4/UDP packet from `dns`
  - Timestamp gap: packet timestamps jump from second `1` to second `3`

This fixture is intentionally small and deterministic. It is used to verify:

- fixed offline replay
- malformed packet tolerance
- offline second-gap snapshot emission
- stable final snapshot and golden JSON output
