# Palladium Wallet

**An SPV desktop wallet built specifically for the Palladium (PLM) cryptocurrency** and optimized for its chain.

Unlike generic wallets adapted to many coins, Palladium Wallet is designed around Palladium's consensus parameters — a Bitcoin-derived UTXO chain with 2-minute blocks and LWMA difficulty — and centralizes them in a single network profile. This keeps it lightweight, predictable and faithful to the chain: no client-side difficulty recalculation (trust is anchored to hardcoded checkpoints), mandatory Merkle verification on every confirmed transaction, and a network client written specifically for Palladium's indexing server.

## Features

- **Lightweight SPV**: syncs against an indexing server (ElectrumX-like protocol) without downloading the full chain.
- **Security**: seed and private keys encrypted on disk (AES-GCM, PBKDF2-SHA512), never in plaintext in logs or on the wire; every server response is validated with Merkle proofs + checkpoints.
- **HD wallet** (BIP39/BIP32), SegWit/wrapped/legacy addresses, watch-only from xpub.
- **PSBT-centric**: signing flows go through PSBT (offline / air-gapped / multisig).
- **Multi-network**: mainnet, testnet, regtest.
- **GUI** (Avalonia) and **CLI** on the same core.
- **Multilingual**: Italian, English, Spanish, French, Portuguese, German.

## Architecture

```
PalladiumWallet.sln
├─ src/Core/   Chain/ Crypto/ Wallet/ Spv/ Net/ Storage/  (no UI dependency)
├─ src/App/    Avalonia GUI
├─ src/Cli/    CLI on the same Core
└─ tests/      xUnit
```

Stack: **.NET 8 + Avalonia UI + NBitcoin**.

---

## Development environment

You only need the **.NET 8 SDK**. The core and crypto are fully testable without the GUI or a real network.

### Windows

1. Install the .NET 8 SDK:
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
   (alternatively, the installer from <https://dotnet.microsoft.com/download/dotnet/8.0>)
2. Clone the repository and restore dependencies:
   ```powershell
   git clone <repo-URL>
   cd PalladiumWallet
   dotnet restore
   ```

### Linux

1. Install the .NET 8 SDK through your distro's package manager, or without root via the official script:
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
   export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
   ```
   (add the two `export` lines to your `~/.bashrc` to make them permanent)
2. Clone and restore:
   ```bash
   git clone <repo-URL>
   cd PalladiumWallet
   dotnet restore
   ```

> The GUI uses Avalonia, which runs natively on both platforms with no extra graphics dependencies.

---

## Running it

**GUI** (with hot reload for development):
```bash
dotnet watch --project src/App
```
or a single run:
```bash
dotnet run --project src/App
```

**CLI** (same core, useful for scripts and headless environments):
```bash
dotnet run --project src/Cli -- <command>
```
Run without arguments for the full list of commands.

---

## Running tests

Tests are the **primary verification layer** — the core logic and crypto run headless, without the GUI or a real network.

Run the whole suite:
```bash
dotnet test
```

Run a single test (or a group) by name:
```bash
dotnet test --filter "FullyQualifiedName~TestName"
```

Run only the tests in one project:
```bash
dotnet test tests/PalladiumWallet.Tests
```

> Cross-implementation tests compare addresses, txids and PSBTs against reference golden vectors: a different address or txid is a blocking bug.

---

## Building

**Development build:**
```bash
dotnet build
```

**Windows publish** (single self-contained executable):
```bash
dotnet publish src/App -r win-x64 -p:PublishSingleFile=true --self-contained
```

**Linux publish** (self-contained; the AppImage is then produced with [PupNet Deploy](https://github.com/kuiperzone/PupNet-Deploy)):
```bash
dotnet publish src/App -r linux-x64 --self-contained
```

The application **version** is set in a single place: the `<Version>` tag in [`src/App/PalladiumWallet.App.csproj`](src/App/PalladiumWallet.App.csproj). It appears in the window title and is stamped into the published binaries.

---

## User guide (quick)

### First launch
1. On first launch, choose **where to store data** (wallet, configuration, certificates) — the default path or a folder of your choice.
2. Create a new wallet, restore from seed, or open an existing wallet.
3. If you create a wallet, **write the seed phrase down on paper**: it will not be shown again. You can protect the file with a password.

### Main tabs
- **History** — list of transactions. *Double-click* a row to open the full detail (amount, fee, addresses, sizes, confirmations).
- **Send** — recipient + amount (or "send all"), adjustable fee; for watch-only wallets a PSBT is produced to be signed offline.
- **Receive** — next unused address, with a **QR code** and a **Copy** button.
- **Addresses** — all derived addresses with balances; click for details (keys, derivation path).
- **Contacts** — address book with labels.

### Connection
- The status indicator at the bottom shows the connection to the **indexing server**; tapping it opens the server settings.
- Sync is SPV: it downloads only what concerns your wallet and verifies every confirmed transaction with a Merkle proof.

### Settings and Help
- **Settings**: language, display unit (PLM / mPLM / µPLM / sat), server.
- **Help**: software information and version.

### CLI in brief
```bash
# Wallet
dotnet run --project src/Cli -- create   [--words 12|24] [--kind segwit|wrapped|legacy] [--net mainnet|testnet|regtest] [--password P]
dotnet run --project src/Cli -- restore  "<mnemonic>" [...]
dotnet run --project src/Cli -- info     [--net ...] [--password P]

# Network
dotnet run --project src/Cli -- sync     [--server host[:port]] [--ssl]
dotnet run --project src/Cli -- send     --to ADDRESS (--amount X | --all) [--feerate sat/vB] [--broadcast]
```
The default wallet file is `~/.palladium-wallet/<network>/wallets/default.wallet.json` (override with `--file`).

---

## License

Released under the MIT License. See the [LICENSE](LICENSE) file.
