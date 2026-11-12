"""Builds a Debian .deb package for BlongusLinux without requiring dpkg-deb."""

import io
import os
import tarfile
import time


def make_ar_entry(name: str, data: bytes, mtime: int) -> bytes:
    header = (
        f"{name:<16}"
        f"{mtime:<12}"
        f"{0:<6}"
        f"{0:<6}"
        f"{100644:<8}"
        f"{len(data):<10}"
        "`\n"
    ).encode("ascii")
    assert len(header) == 60
    pad = b"\n" if len(data) % 2 == 1 else b""
    return header + data + pad


def add_bytes(tf: tarfile.TarFile, arcname: str, data: bytes, mode: int, mtime: int) -> None:
    info = tarfile.TarInfo(name=arcname)
    info.size = len(data)
    info.mode = mode
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    info.mtime = mtime
    tf.addfile(info, io.BytesIO(data))


def add_dir(tf: tarfile.TarFile, arcname: str, mtime: int) -> None:
    info = tarfile.TarInfo(name=arcname)
    info.type = tarfile.DIRTYPE
    info.mode = 0o755
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    info.mtime = mtime
    tf.addfile(info)


def build_deb(binary_path: str, icon_path: str, desktop_path: str, out_deb: str) -> None:
    mtime = int(time.time())
    with open(binary_path, "rb") as f:
        bin_bytes = f.read()
    with open(icon_path, "rb") as f:
        icon_bytes = f.read()
    with open(desktop_path, "rb") as f:
        desktop_bytes = f.read()

    installed_kb = (len(bin_bytes) + len(icon_bytes) + len(desktop_bytes) + 1023) // 1024

    control_text = (
        "Package: blongus\n"
        "Version: 0.2.0\n"
        "Architecture: amd64\n"
        "Maintainer: sheppysheps <166719640+sheppysheps@users.noreply.github.com>\n"
        f"Installed-Size: {installed_kb}\n"
        "Section: games\n"
        "Priority: optional\n"
        "Depends: libc6, libx11-6, libxext6\n"
        "Recommends: pipewire-bin | pulseaudio-utils | alsa-utils\n"
        "Description: Nonsense word generator with confetti and cheering overlay\n"
        " Generates a nonsense word from three dictionary parts.\n"
        " Supports both CLI output and a 5-second transparent GUI celebration.\n"
    ).encode("utf-8")

    control_buf = io.BytesIO()
    with tarfile.open(fileobj=control_buf, mode="w:gz") as tf:
        add_bytes(tf, "./control", control_text, 0o644, mtime)

    data_buf = io.BytesIO()
    with tarfile.open(fileobj=data_buf, mode="w:gz") as tf:
        for d in [
            "./usr",
            "./usr/local",
            "./usr/local/bin",
            "./usr/share",
            "./usr/share/applications",
            "./usr/share/icons",
            "./usr/share/icons/hicolor",
            "./usr/share/icons/hicolor/256x256",
            "./usr/share/icons/hicolor/256x256/apps",
        ]:
            add_dir(tf, d, mtime)
        add_bytes(tf, "./usr/local/bin/blongus", bin_bytes, 0o755, mtime)
        add_bytes(tf, "./usr/share/icons/hicolor/256x256/apps/blongus.png", icon_bytes, 0o644, mtime)
        add_bytes(tf, "./usr/share/applications/blongus.desktop", desktop_bytes, 0o644, mtime)

    os.makedirs(os.path.dirname(out_deb), exist_ok=True)
    with open(out_deb, "wb") as f:
        f.write(b"!<arch>\n")
        f.write(make_ar_entry("debian-binary", b"2.0\n", mtime))
        f.write(make_ar_entry("control.tar.gz", control_buf.getvalue(), mtime))
        f.write(make_ar_entry("data.tar.gz", data_buf.getvalue(), mtime))

    print(f"Built {out_deb} ({os.path.getsize(out_deb)} bytes)")


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    build_deb(
        binary_path=os.path.join(root, "dist-linux", "blongus"),
        icon_path=os.path.join(root, "BlongusLinux", "Assets", "scrimmy.png"),
        desktop_path=os.path.join(root, "BlongusLinux", "packaging", "blongus.desktop"),
        out_deb=os.path.join(root, "dist-linux", "blongus_0.2.0_amd64.deb"),
    )

