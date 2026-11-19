#!/usr/bin/env bash
# Builds release tarballs for the requested runtimes.
#
#   ./packaging/package.sh                 # linux-x64 and linux-arm64
#   ./packaging/package.sh linux-x64       # just one
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
csproj="$root/src/Gyropad.Linux/Gyropad.Linux.csproj"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -n1)"
version="${version:-1.39.5}"

if [ "$#" -gt 0 ]; then
    rids=("$@")
else
    rids=(linux-x64 linux-arm64)
fi

dist="$root/dist"
rm -rf "$dist"
mkdir -p "$dist"

for rid in "${rids[@]}"; do
    name="gyropad-$version-$rid"
    stage="$dist/$name"
    echo "==> publishing $rid"
    dotnet publish "$csproj" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:DebugType=none \
        -p:DebugSymbols=false \
        -o "$stage"

    cp "$root/packaging/70-gyropad.rules" "$stage/"
    cp "$root/packaging/install-udev-rule.sh" "$stage/"
    cp "$root/packaging/README-linux.txt" "$stage/README.txt"

    echo "==> packaging $name.tar.gz"
    # Two passes: the archive is first created normally, then the two files
    # that must be executable are appended with explicit modes. That keeps the
    # +x bit correct even when the build host cannot store Unix permissions
    # (for example a Windows cross-publish).
    archive="$dist/$name.tar"
    tar -C "$dist" -cf "$archive" \
        --exclude="$name/Gyropad" \
        --exclude="$name/install-udev-rule.sh" \
        "$name"
    tar -C "$dist" -rf "$archive" --mode=0755 "$name/Gyropad" "$name/install-udev-rule.sh"
    gzip -f "$archive"
    rm -rf "$stage"
done

echo "==> checksums"
(
    cd "$dist"
    sha256sum ./*.tar.gz | sed 's:\./::' > SHA256SUMS
    cat SHA256SUMS
)
