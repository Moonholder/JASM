#!/usr/bin/env python3
"""Merge translations (JSON: {file.resw: {key: value}}) into Strings/en-us.

Creates missing en-us .resw files and appends missing keys to existing ones.
The JSON files are UTF-8; output .resw files are UTF-8 without BOM (matching
the existing en-us files), with LF line endings.

Usage:
    python Build/merge_resw_translations.py logs/pri263-t1.json
"""

from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


EN_US_DIR = Path(__file__).resolve().parent.parent / "src" / "GIMI-ModManager.WinUI" / "Strings" / "en-us"


def escape(value: str) -> str:
    return (
        value.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
        .replace("'", "&apos;")
    )


def build_data_blocks(entries: dict[str, str]) -> list[str]:
    blocks = []
    for key, value in entries.items():
        blocks.append(
            f'  <data name="{escape(key)}" xml:space="preserve">\n'
            f"    <value>{escape(value)}</value>\n"
            "  </data>"
        )
    return blocks


def write_new_file(path: Path, blocks: list[str]) -> None:
    content = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        "<root>\n"
        "  <!-- English (neutral) resources -->\n"
        + "\n".join(blocks)
        + "\n</root>\n"
    )
    path.write_text(content, encoding="utf-8", newline="\n")


def append_to_existing(path: Path, entries: dict[str, str]) -> int:
    tree = ET.parse(path)
    root = tree.getroot()
    existing = {data.attrib["name"] for data in root.findall("data")}
    added = 0

    # Insert before </root>, keeping a blank line separator.
    last_data = root.findall("data")
    insertion_index = None
    if last_data:
        last = last_data[-1]
        insertion_index = list(root).index(last) + 1

    for key, value in entries.items():
        if key in existing:
            continue
        data = ET.SubElement(root, "data", {"name": key, "xml:space": "preserve"})
        value_el = ET.SubElement(data, "value")
        value_el.text = value
        added += 1

    if added:
        # Serialize manually to control formatting (ElementTree adds ns0/ns1 noise otherwise).
        blocks = []
        for data in root:
            if data.tag != "data":
                blocks.append(ET.tostring(data, encoding="unicode").strip())
                continue
            value = data.find("value").text or ""
            blocks.append(
                f'  <data name="{escape(data.attrib["name"])}" xml:space="preserve">\n'
                f"    <value>{escape(value)}</value>\n"
                "  </data>"
            )
        header = '<?xml version="1.0" encoding="utf-8"?>\n<root>\n'
        path.write_text(header + "\n".join(blocks) + "\n</root>\n", encoding="utf-8", newline="\n")

    return added


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    translations_path = Path(sys.argv[1])
    translations = json.loads(translations_path.read_text(encoding="utf-8"))

    for file_name, entries in translations.items():
        target = EN_US_DIR / file_name
        if not entries:
            print(f"{file_name}: empty, skipped")
            continue
        if target.exists():
            added = append_to_existing(target, entries)
            print(f"{file_name}: appended {added} keys ({len(entries)} requested)")
        else:
            write_new_file(target, build_data_blocks(entries))
            print(f"{file_name}: created with {len(entries)} keys")


if __name__ == "__main__":
    main()
