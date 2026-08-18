import argparse
import glob
import os
import sys

FORBIDDEN_PATTERNS = [
    "*.winmd",
    "*.xml",
    "*.pdb",
    "Microsoft.Windows.Vision*.dll",
    "Microsoft.Windows.Workloads*.dll",
    "Microsoft.Windows.AI*.dll",
    "Microsoft.Windows.Widgets*.dll",
    "Microsoft.ML*.dll",
    "DirectML.dll",
    "onnxruntime*.*",
    "workloads*.json",
    # 调试/符号专用运行时文件：PDB 已删除，运行时无需这些文件
    "mscordaccore*.dll",
    "Microsoft.DiaSymReader.Native*.dll",
]


def cleanup_publish_dir(publish_dir: str) -> list[str]:
    removed: list[str] = []
    for pattern in FORBIDDEN_PATTERNS:
        for path in glob.glob(os.path.join(publish_dir, pattern)):
            try:
                os.remove(path)
                removed.append(os.path.relpath(path, publish_dir))
            except OSError as exc:
                print(f"ERROR: failed to remove {path}: {exc}", file=sys.stderr)
                sys.exit(1)

    leftovers = [
        path
        for pattern in FORBIDDEN_PATTERNS
        for path in glob.glob(os.path.join(publish_dir, pattern))
    ]
    if leftovers:
        print("Publish cleanup validation failed. Unexpected files remain:", file=sys.stderr)
        for path in leftovers:
            print(f"  {path}", file=sys.stderr)
        sys.exit(1)

    if removed:
        print(f"Removed {len(removed)} files from publish output.")
    else:
        print("Publish output already clean.")
    return removed


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Remove and validate forbidden files from a publish directory.")
    parser.add_argument("publish_dir", help="Path to the publish directory.")
    args = parser.parse_args()

    if not os.path.isdir(args.publish_dir):
        print(f"ERROR: publish directory not found: {args.publish_dir}", file=sys.stderr)
        sys.exit(1)

    cleanup_publish_dir(args.publish_dir)
