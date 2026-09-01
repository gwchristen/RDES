# RDES - Portable Device Data Entry System

A zero-installation, high-performance Windows 11 desktop application designed for entering device records into a shared database over a local network drive with multi-user concurrency and automatic Windows user audit tracking.

---

## 🌟 Dual Build Variations

RDES provides **two distinct build variations** tailored for multi-user enterprise and shared-folder deployments:

| Feature | 🖥️ **RDES-Server (Host Edition)** | 🏢 **RDES-Client (Workstation Edition)** |
| :--- | :--- | :--- |
| **Intended Location** | File Server, Shared Network Folder, or Primary Admin PC | Distributed locally to operator PCs or USB drives |
| **Database Creation** | **Yes** (`ReadWriteCreate`) — auto-initializes tables, schema & default lookups | **No** (`ReadWrite`) — never spawns local DBs; only connects to central DB |
| **Connection Safety** | Builds and manages the master SQLite WAL database | Warns user gracefully if network share/central DB is unreachable |
| **Configuration** | Defaults to adjacent `Data\rdes_shared.db` or configured path | Links to shared UNC path (e.g. `\\Server\Shared\RDES_Data\rdes_shared.db`) |
| **Header Badge** | `⚡ RDES [HUB / SERVER]` | `⚡ RDES [WORKSTATION CLIENT]` |
| **Executable Name** | `RDES-Server.exe` | `RDES-Client.exe` |

---

## 🔨 Building in Visual Studio 2022 (No Scripts or Command Line Required)

You can build and publish both portable single-file executables directly from **Visual Studio 2022**:

### Method 1: Using Visual Studio Publish Profiles (1-Click)
1. Open `RDES.sln` in **Visual Studio 2022**.
2. In **Solution Explorer**, right-click the **`RDES.App`** project and select **`Publish...`**.
3. In the Publish window, you will see two pre-configured profiles:
   - **`Server-Portable`** (Builds `dist\Server\RDES-Server.exe`)
   - **`Client-Portable`** (Builds `dist\Client\RDES-Client.exe`)
4. Click the **`Publish`** button in the upper right.
5. Visual Studio will produce the self-contained single-file `.exe` in `dist\Server\` or `dist\Client\`.

### Method 2: Building via Visual Studio Solution Configurations
1. In Visual Studio's top toolbar configuration dropdown:
   - Choose **`Release`** for **Server Edition**.
   - Choose **`Client-Release`** for **Workstation Client Edition**.
2. Go to **Build** $\rightarrow$ **Build Solution** (`Ctrl+Shift+B`).

---

## 📦 Multi-User Deployment Guide

### Step 1: Deploy the Central Server / Database
1. Place **`RDES-Server.exe`** in your central network shared folder (e.g., `\\Server\Shared\RDES_Data\`).
2. Launch it once — it will automatically create and initialize the master SQLite database (`rdes_shared.db`) with full WAL concurrency and default OpCo/Defect catalogs.

### Step 2: Distribute Workstation Clients to Operators
1. Copy **`RDES-Client.exe`** to operators' PCs (or let them run it from their desktop).
2. On first launch, if not yet configured, the client displays a banner:
   `"⚠️ Central Shared Database not connected."`
3. In **Settings & Shared DB**, unlock with Admin PIN (`1234`), browse to the network file `\\Server\Shared\RDES_Data\rdes_shared.db`, and click **Save & Connect**.
4. The client will store this link in its local `config.json` and immediately synchronize live with the server without creating any unwanted local databases.

---

## 🧪 Automated Testing
Run automated unit and concurrency tests:
```powershell
dotnet test
```
*(All 16 concurrency and multi-user tests pass with full WAL verification)*
