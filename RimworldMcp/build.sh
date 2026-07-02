#!/bin/bash
# Build the RimWorld MCP Bridge mod using a Mono Docker container.
# Usage: ./build.sh [output_dir]
#
# Downloads Harmony DLL, compiles the mod, and produces RimworldMcp.dll
# plus supporting files ready to drop into RimWorld/Mods/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="${1:-$SCRIPT_DIR/build}"

echo "=== RimWorld MCP Bridge Build ==="
echo "Output: $OUTPUT_DIR"

# Create output dirs
mkdir -p "$OUTPUT_DIR/Assemblies"
mkdir -p "$OUTPUT_DIR/About"

# Copy About.xml
cp "$SCRIPT_DIR/About/About.xml" "$OUTPUT_DIR/About/"

# Get Harmony DLL from NuGet
HARMONY_VERSION="2.3.3"
HARMONY_DIR="$SCRIPT_DIR/Lib"
mkdir -p "$HARMONY_DIR"

if [ ! -f "$HARMONY_DIR/0Harmony.dll" ]; then
    echo "Downloading Harmony v$HARMONY_VERSION..."
    HARMONY_NUPKG="$HARMONY_DIR/harmony.$HARMONY_VERSION.nupkg"
    curl -sL "https://www.nuget.org/api/v2/package/Lib.Harmony/$HARMONY_VERSION" -o "$HARMONY_NUPKG"
    unzip -q -o "$HARMONY_NUPKG" -d "$HARMONY_DIR/harmony_extract"
    cp "$HARMONY_DIR/harmony_extract/lib/net472/0Harmony.dll" "$HARMONY_DIR/"
    rm -rf "$HARMONY_DIR/harmony_extract" "$HARMONY_NUPKG"
    echo "Harmony downloaded."
fi

# Build using Docker + mono
echo "Compiling with Mono in Docker..."

# Use mono image directly — mcs is included and we already have Harmony
MONO_IMAGE="mono:6.12"

# Write the inside-compile script
cat > "$SCRIPT_DIR/compile_inside.sh" << 'COMPILE_EOF'
#!/bin/bash
set -euo pipefail

GAME_ASSEMBLIES=/game_assemblies
MOD_SRC=/mod_src
OUTPUT=/output

echo "--- Compiling RimWorld MCP Bridge ---"

# Reference ALL DLLs from the game's managed folder
# Using -nostdlib to avoid conflicts with Mono SDK's own base types
REFS=""
for dll in "$GAME_ASSEMBLIES"/*.dll; do
    REFS="$REFS -r:$dll"
done
REFS="$REFS -r:/harmony/0Harmony.dll"

# Compile using ONLY the game's assemblies (no Mono SDK base libraries)
mcs -target:library \
    -out:/output/Assemblies/RimworldMcp.dll \
    -nostdlib \
    -noconfig \
    $REFS \
    -recurse:"$MOD_SRC/Source/*.cs"

echo "--- Build complete ---"
ls -la /output/Assemblies/
COMPILE_EOF

chmod +x "$SCRIPT_DIR/compile_inside.sh"

# Pull the mono image
docker pull $MONO_IMAGE 2>&1 | tail -2

# Run the build directly with mono image + mount compile_inside.sh
docker run --rm \
    -v "$SCRIPT_DIR:/mod_src:ro" \
    -v "$OUTPUT_DIR:/output" \
    -v "$HARMONY_DIR:/harmony:ro" \
    -v "$SCRIPT_DIR/compile_inside.sh:/build/compile_inside.sh:ro" \
    -v "/mnt/c/Program Files (x86)/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed:/game_assemblies:ro" \
    --entrypoint "" \
    $MONO_IMAGE \
    bash /build/compile_inside.sh

echo ""
echo "=== Build successful! ==="
echo "Mod output: $OUTPUT_DIR"
echo ""
echo "To install:"
echo "  1. Copy the '$OUTPUT_DIR' folder to your RimWorld/Mods/ directory"
echo "  2. Rename it to 'RimworldMcpBridge'"
echo "  3. Enable the mod in RimWorld's mod menu"
echo "  4. Start a game — the API will be at http://localhost:8765"
echo ""
echo "Cleanup..."
rm -f "$SCRIPT_DIR/compile_inside.sh"

# Copy 0Harmony.dll to output
cp "$HARMONY_DIR/0Harmony.dll" "$OUTPUT_DIR/Assemblies/"

echo "Done!"
