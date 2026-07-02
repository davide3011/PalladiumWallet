# Changelog

Technical changelog for PalladiumWallet. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/); entries are grouped
by subsystem rather than strictly by date, since `0.9.0` is the first
release and covers the full history from the initial commit.

## [0.9.0] — 2026-07-02

First release. SPV wallet (Sparrow-style) for the Palladium (PLM) network —
Bitcoin-derived UTXO chain — targeting desktop (Windows/Linux) and Android
from a single Avalonia UI codebase on .NET 10 + NBitcoin.

### Core — Chain

- Network profiles and consensus constants centralized in `Core/Chain`
  (`ChainProfiles`/`PalladiumNetworks`): address prefixes, BIP32 headers,
  bech32 HRP, genesis, ports, `coin_type` 746 — selectable per network
  (mainnet/testnet/regtest), no scattered magic numbers.
- LWMA difficulty / 2-minute blocks: SPV cannot recompute LWMA retargeting,
  so PoW validation is skipped and trust is anchored to hardcoded
  checkpoints instead.

### Core — Crypto

- HD key derivation: BIP32/39/84, HD accounts, SLIP-132 extended key
  serialization.
- Address types: P2PKH, P2SH-P2WPKH, and P2TR (Taproot/BIP86).
- `IWalletAccount` abstraction with WIF/xpub/xprv keystore import (including
  watch-only accounts from public material only).

### Core — Net

- Custom `ElectrumClient`: JSON-RPC 2.0 client for the ElectrumX-like
  indexing server, with TLS support and TOFU certificate pinning.
- `ServerRegistry`: bootstrap server list, peer discovery, persisted last-used
  server, fallback resolution for `--server`.
- Batched writes, zero-allocation reads, bounded in-flight requests for
  network throughput.
- Fix: allow changing the indexing server at any time, including during an
  active sync.

### Core — Spv

- Header sync with Merkle proof verification and scripthash subscriptions
  against hardcoded checkpoints (no full PoW recomputation, per the chain
  profile above).
- Per-address balance and transaction-count aggregation in sync results.
- Electrum-style continuous updates: incremental, parallelized sync across
  address chains, with a persistent header cache.
- Fix: resilient sync — history discovery via `GetHistory`, transaction
  caching, automatic reconnect fallback.

### Core — Storage

- Versioned, encrypted JSON wallet file (`WalletDocument`) with dedicated
  persistence and loader layer.
- `WalletLock`: prevents concurrent access to the same wallet file; acquired
  before load and before close (fixed a race where it wasn't).
- XDG-compliant data paths (`AppPaths`) with per-platform override seam used
  by the Android head; English as default UI language.
- Contacts list (name + address) persisted in `WalletDocument`.
- Documented caveat: `WalletStore.Save` writes plaintext JSON when the
  wallet is unencrypted — seed/keys are only ever encrypted-at-rest when the
  user opts into a password.

### Core — Wallet

- Coin selection, PSBT-centric transaction factory, and wallet loader.
- Confirmation-threshold enforcement before spending UTXOs; immature
  (coinbase) balance surfaced separately from confirmed balance; pending
  mempool balance shown, with unconfirmed UTXOs excluded from spending by
  default.
- Fix: reject amounts with sub-satoshi precision.

### App — Avalonia UI (shared, `src/App`)

- Single shared `MainView` (UserControl) hosted by `MainWindow` on desktop
  and as the single-view root on Android; single `MainWindowViewModel`
  (CommunityToolkit.Mvvm), later split into partial files by area
  (Wizard/Settings/Sync/Send/Contacts/Receive/...).
- Step-by-step setup wizard (replacing a single-form panel), including a
  first-run data-location step, multi-wallet chooser, confirm-password and
  encrypt toggle, and dedicated flows for creating a wallet vs. importing
  from xpub/xprv/WIF.
- In-app overlay pattern for details/settings/help (`IsXxxOpen` flags, no OS
  windows) replacing earlier nested submenus and a separate
  `AddressInfoWindow`: server settings, app settings (language/unit),
  wallet info (xpub, password-gated seed reveal), address detail
  (password-gated private key reveal), transaction detail with full
  on-chain data (inputs/outputs, no truncation), and a Help overlay
  (Info/Donate tabs).
- Connection-status indicator in the bottom bar; connect-before-wallet-open
  flow; persisted last-used server; server discovery split into its own
  always-usable button; mainnet hardcoded (network selector removed for the
  first release).
- Localization: `Loc` key → 6-language dictionary (it/en/es/fr/pt/de) with
  live language switching.
- Receive: QR code generation and copy-to-clipboard for the receive address.
- Android: QR code scanner for the Send address field.
- Centralized design system (color tokens, gradient hero, SVG tab icons);
  responsive layout for portrait mobile, unified tab bar (desktop +
  mobile), two-column desktop layout for Send/Receive; various mobile-only
  fixes (tab bar sizing/indicator overlap, text overflow, full-screen server
  overlay on mobile).
- App version shown in window title and Help overlay, read from the single
  `<Version>` source in the App csproj.
- In-app update check: compares the running version against the latest
  GitHub release tag on startup and shows an overlay with the new tag when
  one is available (best-effort — silent on network/parse failure).
- In-app bug-report button (Help overlay) opening a pre-filled GitHub issue
  template; issue/PR templates completed.

### Android head (`src/App.Android`)

- Architecture split: shared UI library (`src/App`) + `src/App.Desktop` +
  `src/App.Android` heads from the same source (`refactor(arch)`), each
  carrying only the per-platform entry point and packages.
- App logo as launcher icon.
- Persistent release-signing keystore workflow
  (`docker/keystore/generate-keystore.sh`, git-ignored output): every
  release APK is signed with the same key so installing a newer build
  updates a previous install in place instead of requiring an uninstall.
  `versionCode` derived from `<Version>` instead of a fixed constant.

### CLI (`src/Cli`)

- Commands: `create`/`restore`/`restore-xpub`/`info`,
  `sync`/`send`/`servers`/`reset-certs`, `newseed`/`addresses`.
- `servers` command, `info --addresses`, and registry-based fallback
  resolution for `--server`.

### Build & Distribution

- Docker-based reproducible build system (`docker/build.sh` +
  `docker/Dockerfile.*`): pinned toolchain (.NET 10 SDK, JDK, Android SDK),
  builds Windows/Linux single-file executables and a signed Android APK
  without any SDK installed on the host.
- Android release signing wired into `build_android` (see Android head
  above): requires the persistent keystore, prompts for its passwords at
  build time, mounts it read-only into the build container.

### Testing

- Unit test coverage across all Core modules, later expanded to 209 tests.
- Property-based tests via CsCheck (`PropertyTests.cs`), bringing total
  coverage to 218 tests; dedicated `WalletLock` concurrency tests.

### Documentation

- `README.md` (project overview, quickstart, reproducible builds), `CLAUDE.md`
  (codebase guidance for AI tooling, kept in sync with the multi-head
  architecture and .NET 10 migration), `SECURITY.md` (threat model and SPV
  trust assumptions), coding-agent guide.
- Code comments translated to English project-wide, per the language policy
  (Italian conversation, English code/docs).
