#!/usr/bin/env bash
# =============================================================================
# check-dotnet.sh — Verify .NET SDK requirements for DataEntry Compile/Build
# =============================================================================
# Checks:
#   1. dotnet is on the PATH
#   2. SDK version is net10.0 or higher
#   3. VB.NET (Roslyn) compiler is functional
#   4. Self-contained publish runtime pack is available for this OS/arch
#   5. A quick dotnet build smoke-test succeeds
#
# Exit codes:
#   0 = all checks passed
#   1 = one or more checks failed
# =============================================================================

set -euo pipefail

PASS="  ✅"
FAIL="  ❌"
WARN="  ⚠️ "
SEP="──────────────────────────────────────────────────────"

overall=0

print_header() {
    echo ""
    echo "$SEP"
    echo "  DataEntry — .NET SDK Environment Check"
    echo "$SEP"
    echo ""
}

check_pass() { echo "$PASS $1"; }
check_fail() { echo "$FAIL $1"; overall=1; }
check_warn() { echo "$WARN $1"; }

# ── 1. dotnet on PATH ─────────────────────────────────────────────────────────
check_dotnet_on_path() {
    echo "[ 1/5 ] Checking: dotnet is on PATH"
    if command -v dotnet &>/dev/null; then
        local loc
        loc=$(command -v dotnet)
        check_pass "dotnet found at: $loc"
    else
        check_fail "dotnet not found on PATH"
        echo ""
        echo "        Install from: https://dotnet.microsoft.com/download/dotnet/10.0"
        return 1
    fi
}

# ── 2. SDK version ≥ 10.0 ─────────────────────────────────────────────────────
check_sdk_version() {
    echo ""
    echo "[ 2/5 ] Checking: .NET SDK version (need 10.0+)"
    local sdk_list
    sdk_list=$(dotnet --list-sdks 2>/dev/null || true)

    if echo "$sdk_list" | grep -qE '^10\.'; then
        local sdks
        sdks=$(echo "$sdk_list" | grep -E '^10\.' | awk '{print $1}' | tr '\n' ' ')
        check_pass "SDK 10.x found: $sdks"
    else
        check_fail "No .NET 10.x SDK found"
        if [ -n "$sdk_list" ]; then
            echo "        Installed SDKs:"
            echo "$sdk_list" | sed 's/^/          /'
        else
            echo "        No SDKs detected at all."
        fi
        echo "        Install from: https://dotnet.microsoft.com/download/dotnet/10.0"
        return 1
    fi
}

# ── 3. VB.NET compiler accessible ─────────────────────────────────────────────
check_vb_compiler() {
    echo ""
    echo "[ 3/5 ] Checking: VB.NET compiler (Roslyn) is functional"

    local tmpdir
    tmpdir=$(mktemp -d)
    trap "rm -rf '$tmpdir'" RETURN

    cat > "$tmpdir/Hello.vb" <<'EOF'
Module Hello
    Sub Main()
        Console.WriteLine("VB.NET OK")
    End Sub
End Module
EOF

    cat > "$tmpdir/probe.vbproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF

    local out
    if out=$(dotnet build "$tmpdir/probe.vbproj" --nologo -v quiet 2>&1); then
        check_pass "VB.NET compiler compiled a test project successfully"
    else
        check_fail "VB.NET compiler failed:"
        echo "$out" | sed 's/^/        /'
        return 1
    fi
}

# ── 4. Self-contained runtime pack available ───────────────────────────────────
check_runtime_pack() {
    echo ""
    echo "[ 4/5 ] Checking: self-contained publish runtime pack"

    # Determine current RID
    local os arch rid
    os=$(uname -s)
    arch=$(uname -m)

    case "$os" in
        Darwin) os_part="osx";;
        Linux)  os_part="linux";;
        *)      os_part="unknown";;
    esac

    case "$arch" in
        arm64|aarch64) arch_part="arm64";;
        *)             arch_part="x64";;
    esac

    rid="${os_part}-${arch_part}"
    echo "        Detected RID: $rid"

    # Try a minimal self-contained publish
    local tmpdir
    tmpdir=$(mktemp -d)
    trap "rm -rf '$tmpdir'" RETURN

    cat > "$tmpdir/Hello.vb" <<'EOF'
Module Hello
    Sub Main()
        Console.WriteLine("SC OK")
    End Sub
End Module
EOF

    cat > "$tmpdir/probe.vbproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF

    local out
    if out=$(dotnet publish "$tmpdir/probe.vbproj" \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        --output "$tmpdir/publish" \
        --nologo -v quiet 2>&1); then
        check_pass "Self-contained publish for '$rid' succeeded"
    else
        check_fail "Self-contained publish for '$rid' failed:"
        echo "$out" | head -20 | sed 's/^/        /'
        echo ""
        echo "        This usually means the runtime pack for '$rid' is not installed."
        echo "        Run:  dotnet workload restore"
        echo "        Or:   dotnet workload install microsoft-net-runtime-$rid"
        return 1
    fi
}

# ── 5. DataEntry project builds ────────────────────────────────────────────────
check_dataentry_build() {
    echo ""
    echo "[ 5/5 ] Checking: DataEntry project builds"

    # Find repo root (go up from this script's location)
    local script_dir
    script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

    local proj="$script_dir/src/DataEntry/DataEntry.vbproj"
    if [ ! -f "$proj" ]; then
        check_warn "DataEntry.vbproj not found at expected path: $proj"
        check_warn "Skipping project build check (run from the repo root)"
        return 0
    fi

    local out
    if out=$(dotnet build "$proj" --nologo -v quiet 2>&1); then
        check_pass "DataEntry project built successfully"
    else
        check_fail "DataEntry project build failed:"
        echo "$out" | head -30 | sed 's/^/        /'
        return 1
    fi
}

# ── Summary ────────────────────────────────────────────────────────────────────
print_summary() {
    echo ""
    echo "$SEP"
    if [ "$overall" -eq 0 ]; then
        echo "  ✅  All checks passed — DataEntry Compile/Build is ready to use."
    else
        echo "  ❌  One or more checks FAILED — see details above."
        echo "      Fix the issues listed, then re-run this script."
    fi
    echo "$SEP"
    echo ""
}

# ── Main ───────────────────────────────────────────────────────────────────────
print_header

check_dotnet_on_path  || true
check_sdk_version     || true
check_vb_compiler     || true
check_runtime_pack    || true
check_dataentry_build || true

print_summary
exit $overall
