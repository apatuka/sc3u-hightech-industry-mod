"""Round-trip tests for an SC3U patcher .exe on a COPY of an unmodified SimCity 3000 Unlimited (GOG) folder.

  python tests/test_patcher.py <unmodified game folder> [<other SC3U patcher .exe>]

The game folder is copied to a temporary folder first; it is never modified. With a second patcher the tests
also install/uninstall both mods in both orders and check that each one removes only its own changes.
"""
import hashlib, os, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
EXE = [os.path.join(HERE, "..", "bin", f) for f in os.listdir(os.path.join(HERE, "..", "bin")) if f.lower().endswith(".exe")][0]
results = []


def check(name, cond):
    results.append(bool(cond))
    print(("PASS  " if cond else "FAIL  ") + name)


def snapshot(root):
    out = {}
    for d, _, fs in os.walk(os.path.join(root, "Apps")):
        for f in fs:
            p = os.path.join(d, f)
            if not f.lower().endswith((".orig", ".tmp")):
                out[os.path.relpath(p, root).lower()] = hashlib.sha256(open(p, "rb").read()).hexdigest()
    return out


def run(exe, op, root):
    return subprocess.run([exe, op, root], capture_output=True).returncode


def main():
    src = sys.argv[1]
    other = sys.argv[2] if len(sys.argv) > 2 else None
    tmp = tempfile.mkdtemp(prefix="sc3u_test_")
    game = os.path.join(tmp, "game")
    shutil.copytree(os.path.join(src, "Apps"), os.path.join(game, "Apps"))
    pristine = snapshot(game)
    check("check on the unmodified game -> 0 (not installed)", run(EXE, "--check", game) == 0)
    check("install -> 0", run(EXE, "--install", game) == 0)
    installed = snapshot(game)
    check("files changed", installed != pristine)
    check("check -> 1 (installed)", run(EXE, "--check", game) == 1)
    check("install again -> 1 (nothing to do), no change", run(EXE, "--install", game) == 1 and snapshot(game) == installed)
    check("uninstall -> 0", run(EXE, "--uninstall", game) == 0)
    check("uninstall restores every file byte for byte", snapshot(game) == pristine)
    if other:
        for first, second in ((EXE, other), (other, EXE)):
            ok = run(first, "--install", game) == 0 and run(second, "--install", game) == 0
            both = snapshot(game)
            check("%s then %s: both installed" % (os.path.basename(first), os.path.basename(second)),
                  ok and run(EXE, "--check", game) == 1 and run(other, "--check", game) == 1)
            check("  uninstall %s keeps the other one installed" % os.path.basename(first),
                  run(first, "--uninstall", game) == 0 and run(second, "--check", game) == 1)
            check("  uninstall %s -> unmodified game" % os.path.basename(second),
                  run(second, "--uninstall", game) == 0 and snapshot(game) == pristine)
    shutil.rmtree(tmp, ignore_errors=True)
    print("\n%d/%d passed" % (sum(results), len(results)))
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
