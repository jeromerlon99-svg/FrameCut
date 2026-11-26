#!/usr/bin/env bash
set -euo pipefail
SOURCE_ROOT="$1"
# Native Windows make's shell echo may truncate a long object list at the
# Windows shell command limit. GNU make's own file function avoids that shell.
TARGET="$SOURCE_ROOT/ffbuild/library.mak"
if grep -Fq '$(Q)echo $^ > $@.objs' "$TARGET"; then
    sed -i 's/$(Q)echo \$\^ > \$@\.objs/$(file >$@.objs,$^)/' "$TARGET"
fi
grep -Fq '$(file >$@.objs,$^)' "$TARGET"
