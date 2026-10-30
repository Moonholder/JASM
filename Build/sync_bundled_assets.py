#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Synchronizes game assets from JASM-GameAssets repository into JASM's bundled Assets directory
(src/GIMI-ModManager.WinUI/Assets/Games) to prepare the baseline assets for application releases.
"""

import argparse
import filecmp
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import List, Tuple

SUPPORTED_GAMES = ["Endfield", "Genshin", "Honkai", "WuWa", "ZZZ"]
PRESERVED_ROOT_FILES = {".gitattributes", "README.md"}
DEFAULT_REMOTE_REPO = "https://github.com/Moonholder/JASM-GameAssets.git"


def find_default_source(repo_root: Path) -> Path | None:
    """Finds local asset directory if available nearby."""
    candidates = [
        repo_root / "JASM-GameAssets-staging",
        repo_root.parent / "JASM-GameAssets",
        repo_root.parent / "JASM-GameAssets-staging",
    ]
    for candidate in candidates:
        if candidate.is_dir() and any((candidate / game).is_dir() for game in SUPPORTED_GAMES):
            return candidate
    return None


def sync_directory(
    source_dir: Path, target_dir: Path, dry_run: bool = False
) -> Tuple[int, int, int, int]:
    """
    Synchronizes source_dir into target_dir.
    Returns: (added, updated, deleted, unchanged)
    """
    added = 0
    updated = 0
    deleted = 0
    unchanged = 0

    if not source_dir.exists():
        return added, updated, deleted, unchanged

    if not dry_run:
        target_dir.mkdir(parents=True, exist_ok=True)

    # Collect source files (relative to source_dir)
    source_files = set()
    for root, _, files in os.walk(source_dir):
        for f in files:
            full_path = Path(root) / f
            rel_path = full_path.relative_to(source_dir)
            source_files.add(rel_path)

    # Collect target files (relative to target_dir)
    target_files = set()
    if target_dir.exists():
        for root, _, files in os.walk(target_dir):
            for f in files:
                full_path = Path(root) / f
                rel_path = full_path.relative_to(target_dir)
                target_files.add(rel_path)

    # Add or update files
    for rel_path in sorted(source_files):
        src_file = source_dir / rel_path
        dst_file = target_dir / rel_path

        if rel_path not in target_files:
            added += 1
            if not dry_run:
                dst_file.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(src_file, dst_file)
        else:
            if not filecmp.cmp(src_file, dst_file, shallow=False):
                updated += 1
                if not dry_run:
                    shutil.copy2(src_file, dst_file)
            else:
                unchanged += 1

    # Remove orphaned files
    for rel_path in sorted(target_files - source_files):
        deleted += 1
        if not dry_run:
            dst_file = target_dir / rel_path
            dst_file.unlink(missing_ok=True)

    # Clean empty subdirectories in target
    if not dry_run and target_dir.exists():
        for root, dirs, _ in os.walk(target_dir, topdown=False):
            for d in dirs:
                dir_path = Path(root) / d
                try:
                    dir_path.rmdir()
                except OSError:
                    pass

    return added, updated, deleted, unchanged


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Sync game assets into JASM bundled Assets for release baseline."
    )
    parser.add_argument(
        "--source",
        type=Path,
        default=None,
        help="Path to JASM-GameAssets repository. If omitted, discovers local folder or clones shallow remote.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Simulate the sync and show what would change without modifying files.",
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    target_root = repo_root / "src" / "GIMI-ModManager.WinUI" / "Assets" / "Games"

    if not target_root.exists():
        print(f"[ERROR] Target bundled assets directory not found: {target_root}", file=sys.stderr)
        return 1

    temp_clone_dir = None
    source_root = args.source

    if source_root is None:
        source_root = find_default_source(repo_root)

    if source_root is None:
        print("[INFO] No local JASM-GameAssets repository found. Cloning shallow copy from GitHub...")
        temp_clone_dir = tempfile.mkdtemp(prefix="jasm_assets_sync_")
        clone_cmd = [
            "git",
            "clone",
            "--depth=1",
            "--single-branch",
            "--branch=main",
            DEFAULT_REMOTE_REPO,
            temp_clone_dir,
        ]
        result = subprocess.run(clone_cmd, check=False)
        if result.returncode != 0:
            print("[ERROR] Failed to clone game assets repository.", file=sys.stderr)
            return 1
        source_root = Path(temp_clone_dir)

    print(f"[INFO] Source assets : {source_root}")
    print(f"[INFO] Target bundled: {target_root}")
    if args.dry_run:
        print("[INFO] Mode          : Dry Run (no disk changes)")
    print("-" * 65)

    total_added = 0
    total_updated = 0
    total_deleted = 0
    total_unchanged = 0

    try:
        for game in SUPPORTED_GAMES:
            src_game = source_root / game
            dst_game = target_root / game

            if not src_game.is_dir():
                print(f"[{game:8s}] Skipping (not present in source)")
                continue

            added, updated, deleted, unchanged = sync_directory(
                src_game, dst_game, dry_run=args.dry_run
            )
            total_added += added
            total_updated += updated
            total_deleted += deleted
            total_unchanged += unchanged

            status = f"+{added} added, ~{updated} updated, -{deleted} deleted, {unchanged} unchanged"
            print(f"[{game:8s}] {status}")

        print("-" * 65)
        print(
            f"Summary: +{total_added} added, ~{total_updated} updated, -{total_deleted} deleted, {total_unchanged} unchanged."
        )
        if not args.dry_run and (total_added > 0 or total_updated > 0 or total_deleted > 0):
            print("[SUCCESS] Bundled game assets synchronized successfully.")
        elif args.dry_run:
            print("[INFO] Dry run complete.")
        else:
            print("[INFO] Bundled game assets are already up-to-date.")

        return 0

    finally:
        if temp_clone_dir and os.path.exists(temp_clone_dir):
            shutil.rmtree(temp_clone_dir, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())

