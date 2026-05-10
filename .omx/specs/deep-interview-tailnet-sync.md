# Deep Interview Spec: Wander (Tailnet Sync)

## Metadata
- **Profile:** Standard
- **Final Ambiguity:** ~15%
- **Threshold:** 20%
- **Context Type:** Greenfield

## Clarity Breakdown
- **Intent:** High. Decentralized P2P sync over Tailscale for files and safe settings.
- **Outcome:** High. A local background service/app that synchronizes directories across peers seamlessly.
- **Scope:** High. Synced directories and a restricted list of visual Windows settings. Small peer networks (< 4-5 nodes).
- **Constraints:** High. Must survive offline edits without data loss. Must not break devices via misconfigured settings.
- **Success Criteria:** High. Files sync when online. Offline edits are saved as copies alongside the network true state when coming back online.

## In-Scope
- Distributed P2P synchronization over Tailscale.
- SQLite-backed state management per device.
- Unidirectional "network truth" conflict resolution.
- Syncing a heavily restricted, curated list of visual Windows settings.

## Out-of-Scope / Non-goals
- Large-scale scalable P2P architectures (like BitTorrent DHT).
- Automatic file merging (e.g., git-style diff merges).
- Syncing complex hardware, display, or network driver settings.
- Complex user permission levels (everyone has equal read/write).

## Decision Boundaries
- The AI may select the specific file hashing algorithm (e.g., SHA-256 vs BLAKE3).
- The AI may determine the exact SQLite schema for file states.
- The AI may build the default list of visual Windows settings to offer the user.

## Pressure-Pass Findings
- *Initial Assumption:* The user wanted a client-server mirroring backup.
- *Resolution:* Shifted completely to a decentralized multi-master P2P engine.
- *Initial Assumption:* Syncing "Windows Settings" meant all settings.
- *Resolution:* Clarified to only visual/safe settings to avoid hardware breakage.

## Acceptance Criteria
1. Devices can discover each other over Tailscale.
2. Modifying a tracked file on Device A replicates to Device B.
3. If Device A is offline and modifies a file, when it reconnects and finds Device B also modified it, Device A will download Device B's version as the truth, and save its own edit as `(offline-edit)`.
4. Visual settings like dark mode can be toggled to sync across peers safely.
