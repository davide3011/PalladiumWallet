# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Role

Operate as an **expert in cryptocurrencies and cryptography**: reason with the domain's rigor about UTXO consensus, HD key derivation (BIP32/39/SLIP-132), signature schemes and scripts (P2PKH/P2SH/P2WPKH, PSBT), address encoding (base58/bech32), Merkle/SPV proofs, and at-rest encryption. When a choice touches cryptographic correctness or fund safety, judge it through that lens and flag known risks and pitfalls (nonce reuse, missing validation, exposed keys/seed, wrong fee/coin-selection, unverified server responses). Explain trade-offs with technical precision; never take for granted what hasn't been verified.

## Language policy

- **Conversation with the user**: Italian.
- **All code, comments, commit messages, and documentation files**: English only.

## How to assist

On **every requested change**, before implementing, judge whether it makes sense and say so plainly: if a request is useful and consistent with the project, proceed; if it is useless, redundant, already covered elsewhere, or risks degrading the code, **say so** with a short rationale and propose the better alternative (or doing nothing). No automatic agreement — an honest opinion is worth more than blind execution.

## What it is

SPV wallet (Sparrow-style) for the **Palladium (PLM)** cryptocurrency, a Bitcoin-derived UTXO chain. Targets desktop (Windows/Linux) and Android, from the same source. Lightning is excluded from the first release.

[blueprint.md](blueprint.md) is a **reference for understanding** (consensus parameters verified against the node, algorithms, network protocol): consult it when it helps to understand an area, but **it is no longer binding** — it need not be followed to the letter or read before every change. The source of truth is the current code; the `§` references below point to the blueprint only as further reading.

## Stack and structure

.NET 10 + Avalonia UI 12 + NBitcoin.

```
src/Core/         Chain/ Crypto/ Wallet/ Spv/ Net/ Storage/  (no UI dependency)
src/App/          shared Avalonia UI library (App, Views, ViewModels, Loc, Assets)
src/App.Desktop/  desktop head (WinExe): Program.cs, app.manifest, .ico → runnable
src/App.Android/  Android head (net10.0-android): MainApplication/MainActivity → apk
src/Cli/          CLI on the same Core            tests/   xUnit
```

The Avalonia UI lives **once** in `src/App` (a library); the two heads only carry the
per-platform entry point and packages. `MainView` (UserControl) is the shared root, hosted
by `MainWindow` on desktop and as the single-view root on Android.

**Non-negotiable dependency rule:** `App`/`Cli` depend only on `Core`; the UI goes through the wallet domain, never directly through network or cryptography. `Core` knows nothing about the UI.

## Commands

.NET 10 SDK lives in `~/.dotnet10`: in non-interactive shells, before any `dotnet` command run
`export PATH="$HOME/.dotnet10:$PATH" DOTNET_ROOT="$HOME/.dotnet10"`.

- Build: `dotnet build`
- Tests (headless, the primary verification layer): `dotnet test` — single: `dotnet test --filter "FullyQualifiedName~TestName"`; property-based tests (CsCheck, `PropertyTests.cs`) run in the same command and take ~30 s
- GUI hot reload: `dotnet watch --project src/App.Desktop` (on WSL2/WSLg the window shows on the Windows desktop, no graphics dependencies to install)
- CLI: `dotnet run --project src/Cli -- <command>` (no args → usage)
- Windows publish: `dotnet publish src/App.Desktop -r win-x64 -p:PublishSingleFile=true --self-contained`
- Linux publish: `dotnet publish src/App.Desktop -r linux-x64 --self-contained` (then AppImage via PupNet Deploy)

**Android (apk).** Needs the `android` workload (`dotnet workload install android`), a JDK
(`JAVA_HOME`), and the Android SDK. To provision the SDK once:
`dotnet build src/App.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory=$HOME/android-sdk -p:AcceptAndroidSDKLicenses=true`.
Then build a debug apk (output in `src/App.Android/bin/Debug/net10.0-android/*-Signed.apk`):
`JAVA_HOME=<jdk> dotnet build src/App.Android -c Debug -t:SignAndroidPackage -p:AndroidSdkDirectory=$HOME/android-sdk`
(set `ANDROID_HOME` to skip the `-p:AndroidSdkDirectory` flag). The head is an application,
not a library, because it sets `<OutputType>Exe</OutputType>`; min SDK 23 (AndroidX requirement).
Note: a plain `dotnet build` at the solution level needs the Android SDK path for the Android head.

**CLI** (`src/Cli`): `create`/`restore`/`restore-xpub`/`info`; `sync`/`send`/`servers`/`reset-certs` (`--server host:port [--ssl]`); `newseed`/`addresses`. Default wallet file `~/.palladium-wallet/<network>/wallets/default.wallet.json` (`--file` to change it).

## Architecture (points that require reading multiple files)

- **Layers (§2):** GUI → wallet domain → SPV/Sync → Network → Cryptography → Persistence; each layer depends only downward.
- **Network profile (§3):** all chain constants (address prefixes, BIP32 headers, bech32 HRP, genesis, ports, coin_type 746) **centralized in `Core/Chain`** (`ChainProfiles`/`PalladiumNetworks`), selectable per network (mainnet/testnet/regtest). No scattered magic numbers.
- **LWMA / skip PoW (§3, §7):** LWMA difficulty, 2-minute blocks; an SPV client cannot recompute it → `SkipPowValidation = true`, trust anchored to **hardcoded checkpoints** (§7.3). Custom layer: NBitcoin assumes Bitcoin's retargeting.
- **NBitcoin vs custom (§19.2):** NBitcoin covers the custom network, BIP32/39, addresses, transactions, PSBT, signing, encoding, hashing — **do not reimplement these**. Hand-written custom code: JSON-RPC client for the indexing server (ElectrumX-like, §10); SPV sync with Merkle verification (§7.4); header/checkpoint validation; coin selection and fee policy; versioned encrypted JSON wallet file.
- **PSBT-centric (§6.5):** every signing flow goes through PSBT (offline/air-gapped/multisig/hardware).
- **Ports:** 50001/50002 = indexing server (what the SPV wallet talks to), **not** the node's P2P port (2333).

## GUI conventions (`src/App`)

- **Shared `MainView` + heads:** the whole UI is a single `MainView` (UserControl), so it works both as a desktop window's content and as Android's single-view root. Top-level APIs (file/folder picker, clipboard) are reached via `TopLevel.GetTopLevel(this)` since a UserControl doesn't expose them. `MainWindowViewModel.IsDesktop` (from `OperatingSystem.IsAndroid()`) hides filesystem-only features (open-from-file; the data-location wizard step auto-skips on Android because the head sets `AppPaths.OverrideDataRoot`).
- **Single ViewModel** `MainWindowViewModel` (CommunityToolkit.Mvvm: `[ObservableProperty]`, `[RelayCommand]`); `Core` is driven directly from here.
- **In-app overlays, not OS windows:** details (address, transaction), settings, and help are full-screen `Border`s gated by an `IsXxxOpen` flag, not separate `Window`s — instant open/close, mobile-friendly, and popups/top-levels are slow on WSLg. Pattern: bool property + Open/Close commands + backdrop handler and Esc key in `MainView`'s code-behind; overlay close buttons bind via `$parent[UserControl]` (not `$parent[Window]`, absent on mobile). Heavy network work runs off the UI thread (`Task.Run`) so the overlay never freezes.
- **Localization:** `Localization/Loc.cs`, key→6 languages dictionary (it/en/es/fr/pt/de); in XAML `{Binding Loc[key]}`, in C# `Loc.Tr("key")`. On language change the `Loc` instance is replaced.
- **App version:** single source = `<Version>` in `src/App/PalladiumWallet.App.csproj`; read at runtime (`MainWindowViewModel.AppVersion`) and shown in the title.
- **Storage paths:** `Core/Storage/AppPaths` resolves data locations; `AppPaths.OverrideDataRoot` (top priority) is the per-platform seam — the Android head sets it to the app sandbox (`Context.FilesDir`), desktop leaves it null.

## Implementation state (§16 steps 1–7 + GUI)

`Core/Chain` network profiles; `Core/Crypto` BIP39/32/SLIP-132/`HdAccount`; `Core/Storage` JSON wallet v1 + AES-GCM (PBKDF2-SHA512) + data paths; `Core/Net` `ElectrumClient` (newline JSON-RPC over TCP/TLS, TOFU in `server-certs.json`, concurrent requests) + `ElectrumApi`; `Core/Spv` scripthash, mandatory Merkle verification on every confirmed tx, sync with gap limit; `Core/Wallet` `TransactionFactory` (RBF on, send-all, watch-only PSBT), `TransactionInspector` (tx detail from the server), `WalletLoader`. GUI: setup wizard, dashboard (history/send/receive with QR+copy/addresses/contacts), transaction detail, settings/server/help, multi-wallet. Runs on desktop and Android from one shared UI (debug apk builds end-to-end).

**TODO (§16 steps 8–9):** multisig, hardware wallet, coin control UI, fee ETA/mempool, RBF/CPFP UI, on-disk header chain, multi-server pool, proxy/Tor.

## Working rules

- **Cross-implementation tests (§16):** compare addresses, txids, and PSBTs against a reference wallet (golden vectors). A different address or txid is a blocking bug.
- **Security (§17):** seed and private keys never in plaintext on disk/logs/network; every server response validated with Merkle + checkpoints; watch-only truly read-only.
- *(Optional)* blueprint features may be deferred but must still be considered in the design.
