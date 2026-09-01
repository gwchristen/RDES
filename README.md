# RDES - Portable Device Data Entry System

A zero-installation, high-performance Windows 11 desktop application designed for entering device records into a shared database over a local network drive with multi-user concurrency and automatic Windows user audit tracking.

---

## Key Features

- **Zero-Installation Portability**:
  - Standalone single-file executable (`RDES.exe`).
  - Self-contained (.NET 8 runtime bundled) — runs on any Windows 11 PC out of the box with no installers or admin privileges.
  - Can be launched directly from a shared network folder or copied locally to desktops.

- **Multi-User Shared Drive Concurrency**:
  - Embedded SQLite configured with **Write-Ahead Logging (WAL)** mode for non-blocking concurrent reads and serialized atomic writes over SMB shares.
  - Automatic exponential backoff retry handler (`busy_timeout=10000`) to eliminate locking collisions across simultaneous users.
  - Built-in online database backup tool to generate timestamped snapshots without interrupting active users.

- **Automatic User & Machine Auditing**:
  - Automatically captures the active Windows login (`Environment.UserName`) and workstation name (`Environment.MachineName`) on every record creation and edit.
  - Maintains a full `AuditLogs` table for tracking additions, updates, deletions, and bulk imports.

- **Rapid Data Entry & Barcode Support**:
  - Auto-capitalization for scanned serial numbers.
  - Instant duplicate detection warning with one-click record loading.
  - Pre-seeded defect catalog loaded from the RMA lookup tables (35+ predefined categories + custom option).
  - Keyboard-first workflow: `Enter` or `Ctrl+S` to save, `Ctrl+N` to clear/new, `F5` to refresh.
  - Live session feed displaying recently entered records in real-time.

- **Spreadsheet Integration & Data Grid**:
  - **Bulk Import**: Imports records directly from `.xlsm` (Macro-Enabled) or `.xlsx` workbooks (e.g., *RMA Entry*, *AEP*, *Aclara* sheets) with automatic column mapping and duplicate overwrite options.
  - **Data Search & Filter**: Real-time search across serial numbers, module numbers, defects, users, and notes with quick date presets (*Today*, *This Week*).
  - **Export**: Export filtered records directly to styled Excel (`.xlsx`) workbooks and `.csv` files.

---

## Deployment & Multi-User Setup

### Option 1: Run Directly from a Shared Network Folder (Recommended)
1. Copy `RDES.exe` to your network share (e.g., `\\Server\Shared\RDES\` or mapped drive `Z:\RDES\`).
2. Have users create a shortcut to `RDES.exe` on their Windows 11 desktops.
3. Open `RDES.exe`, navigate to **Settings & Shared DB**, and set the database path (e.g., `\\Server\Shared\RDES\Data\rdes_shared.db`).
4. Click **Save & Connect**. All PCs will now read and write to the same shared database simultaneously.

### Option 2: Run Locally Pointing to a Shared DB
1. Copy `RDES.exe` to each user's PC.
2. In **Settings & Shared DB**, point the database location to the shared network path `\\Server\Shared\Data\rdes_shared.db`.

---

## Building from Source

### Prerequisites
- .NET 8.0 SDK or .NET 9.0 SDK installed on Windows.

### Build Standalone Executable
Run the included PowerShell script:
```powershell
.\build.ps1
```
The resulting single-file executable will be generated at:
`dist\RDES.exe`

### Run Tests
```powershell
dotnet test
```
