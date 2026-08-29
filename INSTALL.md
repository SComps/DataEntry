# DataEntry — Installation Guide

## Overview

DataEntry ships as a **self-contained single-file executable** — it does not need
the .NET runtime installed on the machine where you *run* forms.

However, to **compile forms** (the `--build` flag, or pressing **F10** in the
preview) DataEntry shells out to `dotnet publish`, which means the **.NET 10 SDK**
must be installed on the machine running the compiler.

| Task | .NET SDK needed? |
|------|-----------------|
| Run the DataEntry compiler (preview / `--build`) | **Yes — .NET 10 SDK** |
| Run a generated form application | **No** — self-contained |

---

## Step 1 — Verify or install the .NET 10 SDK

### Windows x64

1. Open <https://dotnet.microsoft.com/download/dotnet/10.0> in a browser.
2. Under **.NET 10.0 SDK**, click **Windows** → **x64** → **Installer (.exe)**.
3. Run the downloaded installer and follow the prompts.
4. Open a new **PowerShell** or **Command Prompt** window and verify:

```
dotnet --version
```

Expected output starts with `10.` (e.g. `10.0.100`).

**Winget alternative (Windows 10 / 11):**

```
winget install Microsoft.DotNet.SDK.10
```

---

### Linux x64

**Ubuntu / Debian:**

```bash
# Add the Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install the SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

**Fedora / RHEL / Rocky / AlmaLinux:**

```bash
sudo dnf install dotnet-sdk-10.0
```

**Snap (any distro with snapd):**

```bash
sudo snap install dotnet-sdk --channel=10.0/stable --classic
```

**Verify:**

```bash
dotnet --version
```

---

### Linux arm64 (Raspberry Pi 4/5, AWS Graviton, etc.)

The Microsoft package feeds include arm64 packages.

**Ubuntu / Debian arm64:**

```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

If the package feed does not yet have a build for your exact distro version,
use the **manual binary install** method instead:

```bash
# Download the arm64 tar.gz from:
# https://dotnet.microsoft.com/download/dotnet/10.0
# Then:
mkdir -p $HOME/.dotnet
tar -xzf dotnet-sdk-10.0.*-linux-arm64.tar.gz -C $HOME/.dotnet

# Add to PATH (add these lines to ~/.bashrc or ~/.profile)
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$HOME/.dotnet
```

**Verify:**

```bash
dotnet --version
```

---

### macOS x64 (Intel)

**Option A — Installer:**

1. Open <https://dotnet.microsoft.com/download/dotnet/10.0>.
2. Under **.NET 10.0 SDK**, click **macOS** → **x64** → **Installer (.pkg)**.
3. Double-click the downloaded `.pkg` and follow the prompts.

**Option B — Homebrew:**

```bash
brew install --cask dotnet-sdk
```

*(Homebrew installs the latest stable SDK; verify it is 10.x with `dotnet --version`.)*

**Verify:**

```bash
dotnet --version
```

> **Gatekeeper note:** On first run macOS may show a security warning for
> unsigned binaries.  Go to **System Settings → Privacy & Security** and
> click **Allow Anyway**, or run:
> ```bash
> xattr -d com.apple.quarantine ./dataentry
> ```

---

## Step 2 — Install DataEntry

### Windows

1. Download `DataEntry-win-x64.zip` from the releases page.
2. Extract to a folder, e.g. `C:\Tools\DataEntry\`.
3. Confirm the following files are present in the same folder:
   ```
   DataEntry.exe
   libonigwrap.dll      ← must stay beside DataEntry.exe
   MANUAL.md
   Samples\
   ```
4. *(Optional)* Add the folder to your **PATH**:
   - Search **"Edit the system environment variables"** in Start.
   - Click **Environment Variables** → select **Path** → **Edit** → **New**.
   - Paste the folder path and click **OK**.

5. Verify from PowerShell:
   ```
   DataEntry.exe --help
   ```

### Linux x64 / arm64

```bash
# Extract
tar -xzf DataEntry-linux-x64.tar.gz        # or DataEntry-linux-arm64.tar.gz
cd linux-x64                                # or linux-arm64

# Confirm files
ls -1
# dataentry
# libonigwrap.so   ← must stay beside dataentry
# MANUAL.md
# Samples/

# Make executable (should already be set)
chmod +x dataentry

# Optional: install system-wide
sudo cp dataentry /usr/local/bin/
sudo cp libonigwrap.so /usr/local/bin/      # keep beside the exe
```

Verify:

```bash
dataentry --help
```

### macOS x64

```bash
tar -xzf DataEntry-osx-x64.tar.gz
cd osx-x64

ls -1
# dataentry
# libonigwrap.dylib   ← must stay beside dataentry
# MANUAL.md
# Samples/

chmod +x dataentry

# Remove quarantine flag if macOS blocked it
xattr -d com.apple.quarantine dataentry 2>/dev/null || true

# Optional: install system-wide
sudo cp dataentry /usr/local/bin/
sudo cp libonigwrap.dylib /usr/local/bin/
```

Verify:

```bash
dataentry --help
```

---

## Step 3 — Quick smoke test

Run the pre-built `sample.def` that ships in the `Samples\` folder:

```
dataentry Samples\sample.def                (Windows)
dataentry Samples/sample.def               (Linux/macOS)
```

The form preview should open in the terminal.  Tab through the fields to
confirm the layout, then press **Esc** → **File → Quit** to close.

Now compile it:

```
dataentry Samples\sample.def --build        (Windows)
dataentry Samples/sample.def --build        (Linux/macOS)
```

Expected output:

```
Generating project in: Samples\sample
Running dotnet publish (AOT self-contained)...
Build succeeded.
```

Run the generated application:

```
Samples\sample\publish\sample.exe           (Windows)
./Samples/sample/publish/sample             (Linux/macOS)
```

The customer-entry form opens.  Enter some data and press **Ctrl+S** to save,
then **F10** to quit.  A file named `output.dat` will appear in the same folder
containing the fixed-length records you entered.

---

## Checking the environment (optional)

The included check scripts verify that all prerequisites are in place before
you try a build:

```
.\check-dotnet.ps1          (Windows PowerShell)
./check-dotnet.sh           (Linux/macOS bash)
```

Each script runs five checks and reports pass/fail:

1. `dotnet` is on the PATH
2. .NET 10 SDK is installed
3. VB.NET (Roslyn) compiler is functional
4. Self-contained publish runtime pack is available for this OS/arch
5. DataEntry project builds successfully (source tree only)

---

## Troubleshooting

### `dotnet: command not found`

The SDK was not added to your PATH.  On Linux/macOS, add the following to
`~/.bashrc` or `~/.zshrc` and restart your shell:

```bash
export PATH=$PATH:$HOME/.dotnet
```

On Windows, re-run the SDK installer or add `C:\Program Files\dotnet` to the
system PATH manually.

### `libonigwrap.dll/.so/.dylib` missing or wrong location

This native library must be in the **same directory** as the `DataEntry` (or
`dataentry`) executable.  If you moved the exe without moving the library,
copy it back.

### `The runtime pack for win-x64 was not found`

The SDK was installed but the self-contained publish runtime pack for your
platform is missing.  Run:

```bash
dotnet workload restore
```

or install the specific workload:

```bash
dotnet workload install microsoft-net-runtime-win-x64      # Windows
dotnet workload install microsoft-net-runtime-linux-x64    # Linux x64
dotnet workload install microsoft-net-runtime-linux-arm64  # Linux arm64
dotnet workload install microsoft-net-runtime-osx-x64      # macOS x64
```

### Terminal display is garbled

DataEntry uses Terminal.Gui which requires a terminal that supports Unicode and
ANSI escape codes.

- **Windows:** Use **Windows Terminal** or **PowerShell 7+**.
  The legacy `cmd.exe` console may not render box-drawing characters correctly.
- **Linux/macOS:** Any modern terminal emulator (`gnome-terminal`, `iTerm2`,
  `kitty`, `alacritty`, etc.) works.  Ensure `TERM=xterm-256color` or similar.
- **SSH sessions:** Pass `-t` to force a pseudo-terminal: `ssh -t user@host`.

### Form builds but generated app crashes immediately

The generated application also requires `libonigwrap` beside its executable in
the `publish\` folder.  The build step copies it automatically; if you move only
the exe, copy the library alongside it.
