import ast
import io
import json
import os
import shutil
import subprocess
import tempfile
import time
import types
import unittest
from contextlib import redirect_stdout
from typing import Any, Dict
from unittest import mock

import colab_bridge

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP_PY_PATH = os.path.join(REPO_ROOT, "app.py")


def _load_app_drive_helpers(tmp_root: str):
    with open(APP_PY_PATH, "r", encoding="utf-8") as f:
        tree = ast.parse(f.read(), filename=APP_PY_PATH)

    wanted_funcs = {
        "_is_drive_mounted_live",
        "_has_rclone_drive_remote",
        "_refresh_drive_mount_state",
        "_flush_drive_file",
        "_persist_generated_output_to_drive",
    }
    selected = []
    for node in tree.body:
        if isinstance(node, ast.FunctionDef) and node.name in wanted_funcs:
            selected.append(node)

    mod = ast.Module(body=selected, type_ignores=[])
    ast.fix_missing_locations(mod)

    ns: Dict[str, Any] = {
        "os": os,
        "shutil": shutil,
        "subprocess": subprocess,
        "time": time,
        "Any": Any,
        "Dict": Dict,
        "DRIVE_MOUNTED": False,
        "MODEL_BASE_ROOT": os.path.join(tmp_root, "models"),
        "DEFAULT_OUTPUT_DIR": os.path.join(tmp_root, "outputs"),
        "_RCLONE_DRIVE_PROBE_CACHE": {"mtime": -1.0, "ts": 0.0, "ok": False},
    }
    exec(compile(mod, APP_PY_PATH, "exec"), ns)
    return ns


class ColabBridgeDriveMountTest(unittest.TestCase):
    def test_build_rclone_conf_rejects_public_adc_without_quota_project(self):
        with tempfile.TemporaryDirectory() as tmp:
            tok_file = os.path.join(tmp, "token.json")
            adc_payload = {
                "token": "ya29.test",
                "refresh_token": "1//refresh",
                "token_uri": "https://oauth2.googleapis.com/token",
                "client_id": "764086051850-6qr4p6gpi6hn506pt8ejuq83di341hur.apps.googleusercontent.com",
                "client_secret": "d-secret",
                "scopes": list(colab_bridge.PUBLIC_SCOPES),
                "expiry": "2099-01-01T00:00:00Z",
            }
            with open(tok_file, "w", encoding="utf-8") as f:
                json.dump(adc_payload, f)

            self.assertEqual(colab_bridge._build_rclone_drive_conf(tok_file), "")

            custom_payload = {
                **adc_payload,
                "client_id": "1234567890-customapp.apps.googleusercontent.com",
            }
            with open(tok_file, "w", encoding="utf-8") as f:
                json.dump(custom_payload, f)

            conf = colab_bridge._build_rclone_drive_conf(tok_file)
            self.assertIn("[easycolab_gdrive]", conf)
            self.assertIn("1234567890-customapp.apps.googleusercontent.com", conf)


class AppDrivePersistenceHelpersTest(unittest.TestCase):
    def test_is_drive_mounted_live_distinguishes_mount_boundary(self):
        with tempfile.TemporaryDirectory() as tmp:
            ns = _load_app_drive_helpers(tmp)

            def fake_isdir(p):
                return p == "/content/drive/MyDrive"

            def fake_exists(p):
                return p in ("/content", "/content/drive/MyDrive")

            # Same st_dev -> unmounted local overlay directory -> False
            with (
                mock.patch("os.path.isdir", side_effect=fake_isdir),
                mock.patch("os.path.exists", side_effect=fake_exists),
                mock.patch("os.stat", side_effect=lambda p: types.SimpleNamespace(st_dev=100)),
            ):
                self.assertFalse(ns["_is_drive_mounted_live"]())

            # Different st_dev -> real FUSE DriveFS mount -> True
            def fake_stat_mounted(p):
                return types.SimpleNamespace(st_dev=200 if p == "/content/drive/MyDrive" else 100)

            with (
                mock.patch("os.path.isdir", side_effect=fake_isdir),
                mock.patch("os.path.exists", side_effect=fake_exists),
                mock.patch("os.stat", side_effect=fake_stat_mounted),
            ):
                self.assertTrue(ns["_is_drive_mounted_live"]())

    def test_rclone_remote_rejects_broken_adc_client_and_syncs_when_valid(self):
        with tempfile.TemporaryDirectory() as tmp:
            ns = _load_app_drive_helpers(tmp)
            rc_file = os.path.join(tmp, "rclone.conf")
            with open(rc_file, "w", encoding="utf-8") as f:
                f.write("[easycolab_gdrive]\ntype = drive\nclient_id = 764086051850-abc.apps.googleusercontent.com\n")

            with (
                mock.patch("os.path.expanduser", return_value=rc_file),
                mock.patch("shutil.which", return_value="/usr/local/bin/rclone"),
            ):
                self.assertFalse(ns["_has_rclone_drive_remote"]())

            # Write a custom OAuth client config and verify rclone probe + output copy
            with open(rc_file, "w", encoding="utf-8") as f:
                f.write("[easycolab_gdrive]\ntype = drive\nclient_id = 998877-custom.apps.googleusercontent.com\n")

            local_img = os.path.join(tmp, "outputs", "sample.png")
            os.makedirs(os.path.dirname(local_img), exist_ok=True)
            with open(local_img, "wb") as wf:
                wf.write(b"PNGDATA" * 100)

            ran_cmds = []

            def fake_run(cmd, **kwargs):
                ran_cmds.append(cmd)
                return types.SimpleNamespace(returncode=0, stdout="{}", stderr="")

            with (
                mock.patch("os.path.expanduser", return_value=rc_file),
                mock.patch("shutil.which", return_value="/usr/local/bin/rclone"),
                mock.patch("subprocess.run", side_effect=fake_run),
                mock.patch.dict(ns, {"_is_drive_mounted_live": lambda: False}),
            ):
                self.assertTrue(ns["_has_rclone_drive_remote"]())
                saved = ns["_persist_generated_output_to_drive"](local_img)
                self.assertEqual(saved, local_img)

            self.assertTrue(
                any(
                    cmd[:2] == ["rclone", "copy"]
                    and "easycolab_gdrive:EasyColab_Drive/EasyColab_Outputs" in cmd
                    for cmd in ran_cmds
                )
            )

    def test_refresh_drive_mount_state_upgrades_output_dir_and_flushes(self):
        with tempfile.TemporaryDirectory() as tmp:
            ns = _load_app_drive_helpers(tmp)
            fake_drive_outputs = os.path.join(tmp, "mounted_drive_outputs")
            local_video = os.path.join(tmp, "outputs", "clip.mp4")
            os.makedirs(os.path.dirname(local_video), exist_ok=True)
            with open(local_video, "wb") as wf:
                wf.write(b"MP4DATA" * 128)

            flushed = []
            with (
                mock.patch.dict(ns, {"_is_drive_mounted_live": lambda: True}),
                mock.patch("os.makedirs"),
            ):
                self.assertTrue(ns["_refresh_drive_mount_state"]())
                self.assertEqual(ns["DEFAULT_OUTPUT_DIR"], "/content/drive/MyDrive/EasyColab_Outputs")
                self.assertEqual(ns["MODEL_BASE_ROOT"], "/content/drive/MyDrive/EasyColab_Models")

            drive_file = os.path.join(fake_drive_outputs, "clip.mp4")
            os.makedirs(fake_drive_outputs, exist_ok=True)
            shutil.copy2(local_video, drive_file)
            with mock.patch("os.fsync", side_effect=lambda fd: flushed.append(fd)):
                ns["_flush_drive_file"](drive_file)
            self.assertEqual(len(flushed), 1)


if __name__ == "__main__":
    unittest.main()
