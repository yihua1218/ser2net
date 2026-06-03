#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "$0")/.." && pwd)"
jobs="${JOBS:-$(nproc)}"
gensio_ref="${GENSIO_REF:-master}"

deps_dir="$root_dir/_deps"
stage_dir="$root_dir/_stage/windows-ucrt64"
build_dir="$root_dir/build-ucrt64"
dist_dir="$root_dir/dist/Ser2Net"

export PATH="/ucrt64/bin:$PATH"
export PKG_CONFIG_PATH="$stage_dir/usr/lib/pkgconfig:${PKG_CONFIG_PATH:-}"
export CPPFLAGS="-I$stage_dir/usr/include ${CPPFLAGS:-}"
export LDFLAGS="-L$stage_dir/usr/lib ${LDFLAGS:-}"

run_reconf() {
    if [ -x ./reconf ]; then
        ./reconf
    else
        autoreconf -fiv
    fi
}

mkdir -p "$deps_dir" "$stage_dir"

if [ ! -d "$deps_dir/gensio/.git" ]; then
    git clone https://github.com/cminyard/gensio.git "$deps_dir/gensio"
fi

cd "$deps_dir/gensio"
git fetch --tags --force
git checkout "$gensio_ref"
run_reconf
./configure --prefix="$stage_dir/usr"
make -j"$jobs"
make install

rm -rf "$build_dir"
mkdir -p "$build_dir"
cd "$root_dir"
run_reconf
cd "$build_dir"
"$root_dir/configure" --prefix="$stage_dir/usr"
make -j"$jobs"
make install

rm -rf "$dist_dir"
mkdir -p \
    "$dist_dir/bin" \
    "$dist_dir/etc/ser2net" \
    "$dist_dir/share" \
    "$dist_dir/docs" \
    "$dist_dir/man/man5" \
    "$dist_dir/man/man8"

cp "$stage_dir/usr/sbin/ser2net.exe" "$dist_dir/bin/"
cp "$stage_dir/usr/bin/"*.exe "$dist_dir/bin/" 2>/dev/null || true
cp "$stage_dir/usr/bin/"*.dll "$dist_dir/bin/" 2>/dev/null || true

cp "$root_dir/ser2net.yaml" "$dist_dir/etc/ser2net/ser2net.yaml"
cp "$root_dir/etc/ser2net/windows.yml" "$dist_dir/etc/ser2net/windows.yml"
cat > "$dist_dir/etc/ser2net/smoke-echo.yaml" <<'EOF'
%YAML 1.1
---
connection: &smoke-echo
  accepter: tcp,3023
  connector: echo
EOF

cp "$root_dir/docs/"*.md "$dist_dir/docs/" 2>/dev/null || true
cp "$root_dir/ser2net.yaml.5" "$dist_dir/man/man5/"
cp "$root_dir/ser2net.8" "$dist_dir/man/man8/"

"$root_dir/scripts/collect-runtime-dlls.sh" "$dist_dir/bin" \
    > "$dist_dir/docs/dll-inventory.txt"

"$dist_dir/bin/ser2net.exe" -v

echo "Portable package prepared at $dist_dir"
