#!/usr/bin/env bash
# Helpers for the gitignored .env file that holds an environment's configuration.

# Writes or replaces KEY=value in the given file.
set_env_value() {
  local file="$1" key="$2" value="$3"
  touch "$file"
  if grep -q "^${key}=" "$file"; then
    sed -i.bak "s|^${key}=.*|${key}=${value}|" "$file" && rm -f "$file.bak"
  else
    printf '%s=%s\n' "$key" "$value" >> "$file"
  fi
}
