#!/bin/bash
set -e

# Locate dotnet — prefer PATH, fall back to the known Windows install location.
if command -v dotnet &>/dev/null; then
    DOTNET=dotnet
elif [ -x "/c/Program Files/dotnet/dotnet.exe" ]; then
    DOTNET="/c/Program Files/dotnet/dotnet.exe"
else
    echo "ERROR: dotnet not found. Add it to PATH or install it from https://dot.net"
    exit 1
fi

echo "Building LogicRelay mod..."
"$DOTNET" restore LogicRelay.csproj --source https://api.nuget.org/v3/index.json
"$DOTNET" build LogicRelay.csproj -c Release --no-restore

# Copy compiled DLL to mod root (where 7D2D expects it)
cp bin/LogicRelay.dll ./LogicRelay.dll
echo "DLL copied to mod root."

# Verify 0_TFP_Harmony is in Mods/ — it provides 0Harmony.dll at runtime.
# DO NOT copy 0Harmony.dll into this mod folder; that causes a version conflict.
GAME_ROOT="$(cd ../.. && pwd)"
TFP_HARMONY_MOD="$GAME_ROOT/Mods/0_TFP_Harmony"

if [ ! -d "$TFP_HARMONY_MOD" ]; then
    echo ""
    echo "WARNING: 0_TFP_Harmony not found at $TFP_HARMONY_MOD"
    echo "  Harmony is required at runtime. Copy it now from OldMods:"
    OLDMODS_SRC="$GAME_ROOT/OldMods/0_TFP_Harmony"
    if [ -d "$OLDMODS_SRC" ]; then
        cp -r "$OLDMODS_SRC" "$TFP_HARMONY_MOD"
        echo "  Copied 0_TFP_Harmony from OldMods to Mods/. OK."
    else
        echo "  OldMods/0_TFP_Harmony not found either — install it manually."
        echo "  See: https://community.7daystodie.com/topic/TFP_Harmony"
    fi
else
    echo "0_TFP_Harmony present at $TFP_HARMONY_MOD. OK."
fi

echo ""
echo "Build complete! LogicRelay.dll ready."
echo "Mods folder contents:"
ls -1 "$GAME_ROOT/Mods/"
echo ""
echo "Launch 7D2D with EAC disabled to load the mod."
