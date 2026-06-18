#!/usr/bin/env python3
"""Oloraculo project-context MCP server.

This server intentionally exposes only committed source-of-truth documents and
deterministic routing helpers. It does not read local secret files.
"""

from __future__ import annotations

import json
import re
import sys
import traceback
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]

DOCS: dict[str, tuple[str, str]] = {
    "active": ("docs/source-of-truth/ACTIVE.md", "Current operating checkpoint"),
    "commands": ("docs/source-of-truth/COMMANDS.md", "Command gates and side effects"),
    "data-and-secrets": (
        "docs/source-of-truth/DATA_AND_SECRETS.md",
        "Data refresh, local config, and secret hygiene",
    ),
    "feed-status-contract": (
        "docs/source-of-truth/FEED_STATUS_CONTRACT.md",
        "Canonical sanitized feed status and /snapshot.json schema",
    ),
    "production-architecture": (
        "docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md",
        "Clean Oloraculo production architecture",
    ),
    "production-backlog": (
        "docs/source-of-truth/OLORACULO_PRODUCTION_BACKLOG.md",
        "Phase backlog and remaining work",
    ),
    "production-readiness-plan": (
        "docs/source-of-truth/OLORACULO_PRODUCTION_READINESS_PLAN.md",
        "Concrete production readiness plan, gaps, priorities, and gates",
    ),
    "production-todo": (
        "docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md",
        "Executable production readiness TODO board",
    ),
    "release-scope-audit": (
        "docs/source-of-truth/RELEASE_SCOPE_AUDIT.md",
        "Release scope audit and tracked/deferred production file decisions",
    ),
    "live-path-donor-audit": (
        "docs/source-of-truth/OLORACULO_LIVE_PATH_DONOR_AUDIT.md",
        "Read-only NativePM/Polytrade live-path donor audit",
    ),
    "sports-scalp-cockpit": (
        "docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md",
        "Read-only sports scalp cockpit spec",
    ),
    "combo-lab": (
        "docs/source-of-truth/POLYMARKET_COMBO_LAB.md",
        "Read-only Polymarket combo lab spec",
    ),
    "agents": ("AGENTS.md", "Project agent entrypoint and hard rules"),
    "claude": ("CLAUDE.md", "Durable compact context"),
}

ROUTE_RULES: list[tuple[tuple[str, ...], str, str, str]] = [
    (("aws", "deploy", "ssm", "ec2", "container", "healthz"), "aws-runtime-operator", "oloraculo-aws-r2-ops", "AWS runtime/deployment"),
    (("r2", "s3", "archive", "parquet", "manifest", "zstd", "prune"), "r2-archive-lake-engineer", "oloraculo-aws-r2-ops", "R2 archive/lake"),
    (("rust", "hotpath", "clob", "vwap", "book", "latency"), "rust-hotpath-engineer", "oloraculo-feed-hotpath", "Rust CLOB/scanner hotpath"),
    (("feed", "databet", "oddspapi", "pinnacle", "grid"), "feed-status-integrator", "oloraculo-feed-hotpath", "External feed status"),
    (("monitor", "cockpit", "ui", "blazor", "razor", "css", "visual", "screenshot", "parity"), "cockpit-ux-engineer", "oloraculo-cockpit-parity", "Cockpit UI/visual parity"),
    (("ev", "hedge", "quant", "markout", "backtest", "calibration", "prediction", "pnl"), "quant-evidence-scientist", "oloraculo-quant-evidence", "Quant evidence"),
    (("secret", "key", "env", "wallet", "order", "approve", "cancel", "execution"), "security-risk-sentinel", "oloraculo-security-boundary", "Security/live-order boundary"),
    (("mcp", "opencode", "agent", "skill", "command", "tooling"), "mcp-toolsmith", "oloraculo-mcp-tooling", "MCP/orchestration tooling"),
    (("test", "build", "release", "ci", "coverage", "verify"), "release-verification-lead", "oloraculo-release-gate", "Verification/release"),
]


def read_doc(name: str) -> str:
    if name not in DOCS:
        known = ", ".join(sorted(DOCS))
        raise ValueError(f"Unknown document '{name}'. Known: {known}")
    rel, _ = DOCS[name]
    path = ROOT / rel
    return path.read_text(encoding="utf-8")


def resource_uri(name: str) -> str:
    return f"oloraculo://source-of-truth/{name}"


def list_resources() -> list[dict[str, Any]]:
    return [
        {
            "uri": resource_uri(name),
            "name": f"Oloraculo {name}",
            "description": description,
            "mimeType": "text/markdown",
        }
        for name, (_, description) in DOCS.items()
    ]


def parse_resource_name(uri: str) -> str:
    prefix = "oloraculo://source-of-truth/"
    if not uri.startswith(prefix):
        raise ValueError(f"Unsupported resource URI: {uri}")
    return uri[len(prefix) :]


def backlog_phase(phase: str | int | None) -> str:
    text = read_doc("production-backlog")
    if phase is None or str(phase).lower() in {"", "all"}:
        headings = re.findall(r"^## (Phase \d+ .*)$", text, flags=re.MULTILINE)
        return "\n".join(f"- {heading}" for heading in headings)

    phase_text = str(phase).strip()
    match = re.search(
        rf"^## Phase {re.escape(phase_text)}\b.*?(?=^## Phase \d+\b|^## Polytrade|\Z)",
        text,
        flags=re.MULTILINE | re.DOTALL,
    )
    if not match:
        raise ValueError(f"Phase {phase_text} was not found in production backlog.")
    return match.group(0).strip()


def guardrail_report() -> str:
    return "\n".join(
        [
            "# Oloraculo Guardrails",
            "",
            "- Preserve unrelated dirty worktree changes.",
            "- Do not paste or commit raw secrets, token fragments, signed URLs, wallet material, or private keys.",
            "- Oloraculo is analysis-only until Phase 7 is explicitly implemented and armed.",
            "- Treat NativePM/Polytrade live services as donor evidence only; do not depend on their runtime paths, services, lake files, or scripts.",
            "- Data refresh, README export, archive prune, deploy, and service operations are side-effecting.",
            "- Use ask for buys, bid for sells, and visible depth/freshness for executable liquidity.",
            "- Claim readiness only with tests, screenshots, command output, or measured operational evidence.",
            "- End every status update, report, audit, and final handoff with the recommended next step and why it matters.",
        ]
    )


def route_work(task: str) -> str:
    lowered = task.lower()
    matches: list[str] = []
    for keywords, agent, skill, reason in ROUTE_RULES:
        if any(keyword in lowered for keyword in keywords):
            matches.append(f"- {reason}: agent `{agent}`, skill `{skill}`")

    if not matches:
        matches.append("- General architecture: agent `chief-systems-architect`, skill `oloraculo-architecture-map`")

    return "\n".join(
        [
            "# Suggested Oloraculo Routing",
            "",
            *matches,
            "",
            "Always start with `git status --short`, source-of-truth reads, and an explicit verification gate. End the handoff with the recommended next step and why it matters.",
        ]
    )


def tool_result(text: str) -> dict[str, Any]:
    return {"content": [{"type": "text", "text": text}]}


def handle_tool_call(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    if name == "read_source_doc":
        return tool_result(read_doc(str(arguments.get("name", ""))))
    if name == "list_backlog_phase":
        return tool_result(backlog_phase(arguments.get("phase")))
    if name == "guardrail_report":
        return tool_result(guardrail_report())
    if name == "route_work":
        return tool_result(route_work(str(arguments.get("task", ""))))
    raise ValueError(f"Unknown tool: {name}")


def tools_list() -> list[dict[str, Any]]:
    return [
        {
            "name": "read_source_doc",
            "description": "Read a committed Oloraculo source-of-truth document by name.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "name": {"type": "string", "enum": sorted(DOCS.keys())},
                },
                "required": ["name"],
            },
        },
        {
            "name": "list_backlog_phase",
            "description": "Return a single production backlog phase or all phase headings.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "phase": {"type": ["string", "integer"], "description": "Phase number, or all."},
                },
            },
        },
        {
            "name": "guardrail_report",
            "description": "Return Oloraculo safety and side-effect guardrails.",
            "inputSchema": {"type": "object", "properties": {}},
        },
        {
            "name": "route_work",
            "description": "Suggest senior agent and skill routing for a task.",
            "inputSchema": {
                "type": "object",
                "properties": {"task": {"type": "string"}},
                "required": ["task"],
            },
        },
    ]


def prompts_list() -> list[dict[str, Any]]:
    return [
        {
            "name": "audit-and-route",
            "description": "Audit Oloraculo current state and route the next slice.",
            "arguments": [{"name": "task", "description": "Requested work", "required": True}],
        }
    ]


def prompt_get(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    if name != "audit-and-route":
        raise ValueError(f"Unknown prompt: {name}")
    task = arguments.get("task", "Audit Oloraculo and choose the next slice.")
    text = (
        "Use Oloraculo source-of-truth docs to audit current state, gaps, owner "
        f"agent/skill, files, verification gate, recommended next step, and why it matters for: {task}"
    )
    return {"messages": [{"role": "user", "content": {"type": "text", "text": text}}]}


def handle_request(message: dict[str, Any]) -> dict[str, Any] | None:
    msg_id = message.get("id")
    method = message.get("method")
    params = message.get("params") or {}

    try:
        if method == "initialize":
            result = {
                "protocolVersion": params.get("protocolVersion", "2024-11-05"),
                "capabilities": {
                    "resources": {"subscribe": False, "listChanged": False},
                    "tools": {"listChanged": False},
                    "prompts": {"listChanged": False},
                },
                "serverInfo": {"name": "oloraculo-context", "version": "1.0.0"},
            }
        elif method == "ping":
            result = {}
        elif method == "resources/list":
            result = {"resources": list_resources()}
        elif method == "resources/read":
            uri = params.get("uri", "")
            name = parse_resource_name(str(uri))
            result = {
                "contents": [
                    {"uri": str(uri), "mimeType": "text/markdown", "text": read_doc(name)}
                ]
            }
        elif method == "tools/list":
            result = {"tools": tools_list()}
        elif method == "tools/call":
            result = handle_tool_call(str(params.get("name", "")), params.get("arguments") or {})
        elif method == "prompts/list":
            result = {"prompts": prompts_list()}
        elif method == "prompts/get":
            result = prompt_get(str(params.get("name", "")), params.get("arguments") or {})
        elif method and method.startswith("notifications/"):
            return None
        else:
            raise ValueError(f"Unsupported method: {method}")

        if msg_id is None:
            return None
        return {"jsonrpc": "2.0", "id": msg_id, "result": result}
    except Exception as exc:  # Keep MCP clients alive with structured errors.
        if msg_id is None:
            print(traceback.format_exc(), file=sys.stderr)
            return None
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "error": {"code": -32603, "message": str(exc)},
        }


def read_message() -> dict[str, Any] | None:
    headers: dict[str, str] = {}
    while True:
        line = sys.stdin.buffer.readline()
        if line == b"":
            return None
        decoded = line.decode("utf-8").strip()
        if not decoded:
            break
        if ":" in decoded:
            key, value = decoded.split(":", 1)
            headers[key.lower()] = value.strip()

    length = int(headers.get("content-length", "0"))
    if length <= 0:
        return None
    payload = sys.stdin.buffer.read(length)
    return json.loads(payload.decode("utf-8"))


def write_message(message: dict[str, Any]) -> None:
    payload = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    sys.stdout.buffer.write(f"Content-Length: {len(payload)}\r\n\r\n".encode("ascii"))
    sys.stdout.buffer.write(payload)
    sys.stdout.buffer.flush()


def main() -> int:
    while True:
        message = read_message()
        if message is None:
            return 0
        response = handle_request(message)
        if response is not None:
            write_message(response)


if __name__ == "__main__":
    raise SystemExit(main())
