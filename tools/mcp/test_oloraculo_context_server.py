#!/usr/bin/env python3
"""Smoke test for the Oloraculo context MCP server."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
SERVER = ROOT / "tools" / "mcp" / "oloraculo_context_server.py"


def send(proc: subprocess.Popen[bytes], message: dict[str, Any]) -> None:
    payload = json.dumps(message, separators=(",", ":")).encode("utf-8")
    proc.stdin.write(f"Content-Length: {len(payload)}\r\n\r\n".encode("ascii"))
    proc.stdin.write(payload)
    proc.stdin.flush()


def recv(proc: subprocess.Popen[bytes]) -> dict[str, Any]:
    headers: dict[str, str] = {}
    while True:
        line = proc.stdout.readline()
        if line == b"":
            raise RuntimeError("MCP server closed stdout")
        decoded = line.decode("utf-8").strip()
        if not decoded:
            break
        key, value = decoded.split(":", 1)
        headers[key.lower()] = value.strip()
    length = int(headers["content-length"])
    payload = proc.stdout.read(length)
    return json.loads(payload.decode("utf-8"))


def request(proc: subprocess.Popen[bytes], msg_id: int, method: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
    send(proc, {"jsonrpc": "2.0", "id": msg_id, "method": method, "params": params or {}})
    response = recv(proc)
    if "error" in response:
        raise AssertionError(response["error"])
    return response["result"]


def main() -> int:
    proc = subprocess.Popen(
        [sys.executable, str(SERVER)],
        cwd=str(ROOT),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    assert proc.stdin is not None
    assert proc.stdout is not None
    try:
        init = request(proc, 1, "initialize", {"protocolVersion": "2024-11-05"})
        assert init["serverInfo"]["name"] == "oloraculo-context"

        resources = request(proc, 2, "resources/list")["resources"]
        assert any(item["uri"].endswith("/production-backlog") for item in resources)
        assert any(item["uri"].endswith("/production-readiness-plan") for item in resources)
        assert any(item["uri"].endswith("/production-todo") for item in resources)
        assert any(item["uri"].endswith("/live-path-donor-audit") for item in resources)

        tools = request(proc, 3, "tools/list")["tools"]
        assert {tool["name"] for tool in tools} >= {
            "read_source_doc",
            "list_backlog_phase",
            "guardrail_report",
            "route_work",
        }

        phase = request(proc, 4, "tools/call", {"name": "list_backlog_phase", "arguments": {"phase": 3}})
        assert "R2/Object Archive" in phase["content"][0]["text"]

        plan = request(proc, 5, "tools/call", {"name": "read_source_doc", "arguments": {"name": "production-readiness-plan"}})
        assert "Priority List" in plan["content"][0]["text"]

        todo = request(proc, 6, "tools/call", {"name": "read_source_doc", "arguments": {"name": "production-todo"}})
        assert "P0 - Release Boundary" in todo["content"][0]["text"]

        donor = request(proc, 7, "tools/call", {"name": "read_source_doc", "arguments": {"name": "live-path-donor-audit"}})
        assert "NativePM/Polytrade live-path components as donor evidence" in donor["content"][0]["text"]

        route = request(proc, 8, "tools/call", {"name": "route_work", "arguments": {"task": "build monitor UI and R2 archive"}})
        route_text = route["content"][0]["text"]
        assert "cockpit-ux-engineer" in route_text
        assert "r2-archive-lake-engineer" in route_text

        guardrails = request(proc, 9, "tools/call", {"name": "guardrail_report", "arguments": {}})
        guardrail_text = guardrails["content"][0]["text"]
        assert "analysis-only" in guardrail_text
        assert "recommended next step and why it matters" in guardrail_text

        print("Oloraculo context MCP smoke test passed.")
        return 0
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()


if __name__ == "__main__":
    raise SystemExit(main())
