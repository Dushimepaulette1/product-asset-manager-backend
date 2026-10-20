#!/usr/bin/env bash
#
# Shared by the scripts in this folder. Source it from the repository root:
# it picks the settings file (.env, or .env.example when there is no .env)
# and provides "setting NAME" to read one value from it.

SETTINGS_FILE=".env"
[ -f "$SETTINGS_FILE" ] || SETTINGS_FILE=".env.example"

setting() {
  grep -E "^$1=" "$SETTINGS_FILE" | cut -d= -f2- || true
}
