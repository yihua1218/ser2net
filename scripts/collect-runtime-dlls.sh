#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
    echo "usage: $0 <portable-bin-dir>" >&2
    exit 2
fi

bin_dir="$1"
changed=1

while [ "$changed" -eq 1 ]; do
    changed=0
    while IFS= read -r file; do
        while IFS= read -r dll; do
            [ -n "$dll" ] || continue
            base="$(basename "$dll")"
            if [ ! -f "$bin_dir/$base" ]; then
                cp "$dll" "$bin_dir/"
                echo "copied $base"
                changed=1
            fi
        done < <(ldd "$file" | awk '{print $3}' | grep -E '^/ucrt64/bin/.*\.dll$' || true)
    done < <(find "$bin_dir" -maxdepth 1 -type f \( -name '*.exe' -o -name '*.dll' \))
done

find "$bin_dir" -maxdepth 1 -type f -name '*.dll' -printf '%f\n' | sort
