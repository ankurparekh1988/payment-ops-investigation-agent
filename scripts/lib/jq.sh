#!/usr/bin/env bash
# A native jq.exe under Git Bash ends its output lines with CRLF, which breaks comparisons,
# arithmetic and URLs built from its values. Strip the carriage returns; elsewhere this changes nothing.
jq() { command jq "$@" | tr -d '\r'; }
