#!/usr/bin/env python3
"""Fetch the official Polymarket Mintlify docs into a local vendor snapshot.

The docs site publishes llms.txt with direct markdown links and llms-full.txt
with the combined corpus. Those source files are less noisy and more faithful
than HTML scraping, so this fetcher uses them as the canonical crawl map.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path


DOCS_ORIGIN = "https://docs.polymarket.com"
LLMS_URL = f"{DOCS_ORIGIN}/llms.txt"
LLMS_FULL_URL = f"{DOCS_ORIGIN}/llms-full.txt"
DEFAULT_OUTPUT = Path("docs/vendor/polymarket-docs")
REQUEST_HEADERS = {
    "User-Agent": "OloraculoDocsFetcher/1.0 (+local vendor snapshot)",
    "Accept": "text/markdown,text/plain,text/html;q=0.8,*/*;q=0.5",
}
LINK_RE = re.compile(
    r"^\s*-\s+\[(?P<title>[^\]]+)\]\((?P<url>https://docs\.polymarket\.com/[^\)]+)\)"
    r"(?:\s*:\s*(?P<description>.*))?\s*$",
    re.MULTILINE,
)
HEADING_RE = re.compile(r"^(#{1,6})\s+(.+?)\s*$", re.MULTILINE)
ENDPOINT_RE = re.compile(
    r"\b(?P<method>GET|POST|PUT|PATCH|DELETE|DEL|WSS)\s+"
    r"(?P<path>/(?:[A-Za-z0-9._~:/?#\[\]@!$&'()*+,;=%{}-]+)?)",
    re.IGNORECASE,
)


@dataclass
class PageRecord:
    title: str
    description: str
    source_url: str
    page_url: str
    local_path: str
    section: str
    slug: str
    bytes: int
    sha256: str
    headings: list[str]
    endpoints: list[dict[str, str]]


def fetch_text(url: str, retries: int = 3, timeout: int = 30) -> str:
    url = quote_url(url)
    last_error: Exception | None = None
    for attempt in range(1, retries + 1):
        try:
            request = urllib.request.Request(url, headers=REQUEST_HEADERS)
            with urllib.request.urlopen(request, timeout=timeout) as response:
                charset = response.headers.get_content_charset() or "utf-8"
                return response.read().decode(charset, errors="replace")
        except (urllib.error.URLError, TimeoutError) as exc:
            last_error = exc
            if attempt == retries:
                break
            time.sleep(0.5 * attempt)
    raise RuntimeError(f"failed to fetch {url}: {last_error}") from last_error


def quote_url(url: str) -> str:
    parsed = urllib.parse.urlsplit(url)
    path = urllib.parse.quote(parsed.path, safe="/%")
    query = urllib.parse.quote(parsed.query, safe="=&?/%:+,;@")
    return urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, path, query, parsed.fragment))


def safe_relative_markdown_path(source_url: str) -> Path:
    parsed = urllib.parse.urlparse(source_url)
    if parsed.netloc != "docs.polymarket.com":
        raise ValueError(f"unexpected docs host: {source_url}")
    path = parsed.path.lstrip("/")
    if not path.endswith(".md"):
        path = f"{path.rstrip('/')}.md"
    parts = []
    for part in Path(path).parts:
        cleaned = re.sub(r"[^A-Za-z0-9._-]+", "-", part).strip(".-")
        parts.append(cleaned or "index")
    return Path(*parts)


def page_url_from_source(source_url: str) -> str:
    return source_url[:-3] if source_url.endswith(".md") else source_url


def extract_headings(markdown: str, limit: int = 12) -> list[str]:
    headings = []
    for match in HEADING_RE.finditer(markdown):
        title = match.group(2).strip()
        if title and title.lower() not in {"documentation index"}:
            headings.append(title)
        if len(headings) >= limit:
            break
    return headings


def extract_endpoints(markdown: str) -> list[dict[str, str]]:
    seen = set()
    endpoints = []
    for match in ENDPOINT_RE.finditer(markdown):
        method = match.group("method").upper()
        if method == "DEL":
            method = "DELETE"
        path = match.group("path").strip()
        key = (method, path)
        if key in seen:
            continue
        seen.add(key)
        endpoints.append({"method": method, "path": path})
    return endpoints[:20]


def section_for_relative_path(relative_path: Path) -> str:
    parts = list(relative_path.parts)
    if len(parts) >= 3 and parts[0] == "api-reference":
        return f"{parts[0]}/{parts[1]}"
    if parts:
        return parts[0]
    return "root"


def parse_llms_index(llms_text: str) -> list[dict[str, str]]:
    entries = []
    seen = set()
    for match in LINK_RE.finditer(llms_text):
        source_url = match.group("url").strip()
        if source_url in seen:
            continue
        seen.add(source_url)
        entries.append(
            {
                "title": match.group("title").strip(),
                "source_url": source_url,
                "description": (match.group("description") or "").strip(),
            }
        )
    return entries


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def render_quick_reference(records: list[PageRecord], generated_at: str) -> str:
    by_section: dict[str, list[PageRecord]] = {}
    endpoint_records: list[tuple[str, str, PageRecord]] = []
    for record in sorted(records, key=lambda r: (r.section, r.title.lower())):
        by_section.setdefault(record.section, []).append(record)
        for endpoint in record.endpoints:
            endpoint_records.append((endpoint["method"], endpoint["path"], record))

    lines = [
        "# Polymarket Docs Quick Reference",
        "",
        f"Generated: `{generated_at}`",
        "",
        "This is a local vendor snapshot of the official Polymarket docs. Use it for fast lookup while building Oloraculo integrations; refresh with:",
        "",
        "```powershell",
        "python tools\\docs\\fetch_polymarket_docs.py",
        "```",
        "",
        "## Source Files",
        "",
        f"- Upstream index: [{LLMS_URL}]({LLMS_URL})",
        f"- Upstream full corpus: [{LLMS_FULL_URL}]({LLMS_FULL_URL})",
        "- Local manifest: [manifest.json](manifest.json)",
        "- Combined local corpus: [llms-full.txt](raw/llms-full.txt)",
        "",
        "## Fast Lookup",
        "",
        "- Authentication and API keys: `api-reference/authentication.md`",
        "- SDK setup: `api-reference/clients-sdks.md`, `dev-tooling/python.md`, `dev-tooling/typescript.md`, `trading/clients/*`",
        "- Public market discovery: `api-reference/events/*`, `api-reference/markets/*`, `api-reference/search/*`, `api-reference/sports/*`",
        "- CLOB books and prices: `api-reference/market-data/*`, `trading/orderbook.md`, `market-data/websocket/*`",
        "- Trading lifecycle: `api-reference/trade/*`, `trading/orders/*`, `api-reference/relayer/*`",
        "- Account state: `api-reference/core/*`",
        "- Real-time streams: `api-reference/wss/*`, `market-data/websocket/*`",
        "- Combo markets: `api-reference/combo-markets/*`",
        "- Negative-risk mechanics: `advanced/neg-risk.md`",
        "",
        "## Page Inventory",
        "",
        f"Total pages downloaded: `{len(records)}`",
        "",
    ]

    for section, section_records in sorted(by_section.items()):
        lines.append(f"### {section} ({len(section_records)})")
        lines.append("")
        for record in section_records:
            description = f" - {record.description}" if record.description else ""
            lines.append(
                f"- [{record.title}](pages/{record.local_path})"
                f" ([source]({record.page_url})){description}"
            )
        lines.append("")

    lines.extend(["## Endpoint Index", ""])
    if endpoint_records:
        for method, path, record in sorted(endpoint_records, key=lambda item: (item[0], item[1], item[2].title)):
            lines.append(f"- `{method} {path}` -> [{record.title}](pages/{record.local_path})")
    else:
        lines.append("- No endpoint-like strings were detected in the markdown corpus.")
    lines.append("")

    lines.extend(
        [
            "## Local Search Tips",
            "",
            "```powershell",
            "rg -n \"heartbeat|User Channel|positions|api key|signature\" docs\\vendor\\polymarket-docs",
            "```",
            "",
            "The manifest stores title, source URL, local path, section, headings, endpoint hints, byte size, and SHA-256 for every page.",
            "",
        ]
    )
    return "\n".join(lines)


def render_readme(records: list[PageRecord], generated_at: str, method: str) -> str:
    sections = sorted({record.section for record in records})
    return "\n".join(
        [
            "# Polymarket Documentation Vendor Snapshot",
            "",
            f"Generated: `{generated_at}`",
            f"Crawl method: `{method}`",
            "",
            "This directory contains a local markdown snapshot of the official Polymarket documentation used by Oloraculo agents and implementation work.",
            "",
            "## Files",
            "",
            "- [quick-reference.md](quick-reference.md): human-friendly map for fast lookup.",
            "- [manifest.json](manifest.json): machine-readable page inventory.",
            "- [raw/llms.txt](raw/llms.txt): upstream documentation index.",
            "- [raw/llms-full.txt](raw/llms-full.txt): upstream combined documentation corpus.",
            "- [pages/](pages/): one markdown file per upstream docs page.",
            "",
            "## Coverage",
            "",
            f"- Pages downloaded: `{len(records)}`",
            f"- Sections: `{', '.join(sections)}`",
            "",
            "## Refresh",
            "",
            "```powershell",
            "python tools\\docs\\fetch_polymarket_docs.py",
            "```",
            "",
            "Use the local snapshot for speed, but treat the upstream docs as authoritative when behavior may have changed.",
            "",
        ]
    )


def run(output_dir: Path) -> int:
    generated_at = datetime.now(timezone.utc).isoformat(timespec="seconds")
    output_dir.mkdir(parents=True, exist_ok=True)
    raw_dir = output_dir / "raw"
    pages_dir = output_dir / "pages"

    llms_text = fetch_text(LLMS_URL)
    llms_full_text = fetch_text(LLMS_FULL_URL)
    write_text(raw_dir / "llms.txt", llms_text)
    write_text(raw_dir / "llms-full.txt", llms_full_text)

    entries = parse_llms_index(llms_text)
    if not entries:
        print("No pages found in llms.txt", file=sys.stderr)
        return 1

    records: list[PageRecord] = []
    failures: list[dict[str, str]] = []
    for index, entry in enumerate(entries, start=1):
        source_url = entry["source_url"]
        relative_path = safe_relative_markdown_path(source_url)
        local_path = pages_dir / relative_path
        try:
            markdown = fetch_text(source_url)
        except Exception as exc:  # noqa: BLE001 - recorded in manifest for auditability
            failures.append({"source_url": source_url, "error": str(exc)})
            continue
        write_text(local_path, markdown)
        content_bytes = markdown.encode("utf-8")
        records.append(
            PageRecord(
                title=entry["title"],
                description=entry["description"],
                source_url=source_url,
                page_url=page_url_from_source(source_url),
                local_path=relative_path.as_posix(),
                section=section_for_relative_path(relative_path),
                slug=relative_path.with_suffix("").as_posix(),
                bytes=len(content_bytes),
                sha256=hashlib.sha256(content_bytes).hexdigest(),
                headings=extract_headings(markdown),
                endpoints=extract_endpoints(markdown),
            )
        )
        print(f"[{index:03d}/{len(entries):03d}] {relative_path.as_posix()}")

    manifest = {
        "generated_at": generated_at,
        "source": {
            "origin": DOCS_ORIGIN,
            "llms_url": LLMS_URL,
            "llms_full_url": LLMS_FULL_URL,
            "crawl_method": "mintlify-llms-markdown",
            "requested_firecrawl": True,
            "firecrawl_status": "not available in current Codex tool/plugin set; used official Mintlify markdown corpus",
        },
        "counts": {
            "pages_indexed": len(entries),
            "pages_downloaded": len(records),
            "failures": len(failures),
        },
        "failures": failures,
        "pages": [asdict(record) for record in records],
    }
    write_text(output_dir / "manifest.json", json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
    write_text(output_dir / "quick-reference.md", render_quick_reference(records, generated_at))
    write_text(output_dir / "README.md", render_readme(records, generated_at, "mintlify-llms-markdown"))
    print(f"Downloaded {len(records)} of {len(entries)} Polymarket docs pages into {output_dir}")
    if failures:
        print(f"Failures: {len(failures)}", file=sys.stderr)
        return 2
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT,
        help=f"output directory (default: {DEFAULT_OUTPUT})",
    )
    args = parser.parse_args()
    return run(args.output_dir)


if __name__ == "__main__":
    raise SystemExit(main())
