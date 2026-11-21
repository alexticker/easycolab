"""Loopback ingress for the Gradio and Cloudflare tunnels on a Colab VM."""

import base64
import contextlib
import hashlib
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import tempfile
import time
import urllib.request


GRADIO_INGRESS = 7860
GRADIO_BACKEND = 27860
COMFY_INGRESS = 8188
COMFY_BACKEND = 28188
INFERENCE_PATHS = (
    "/models", "/chat/completions", "/images/generations",
    "/messages/count_tokens", "/messages",
    "/v1/models", "/v1/chat/completions", "/v1/images/generations",
    "/v1/messages/count_tokens", "/v1/messages",
    "/v1/v1/messages/count_tokens", "/v1/v1/messages",
)


def _ssha_htpasswd_digest(password: str) -> str:
    """Fast salted SHA-1 digest ({SSHA}) supported natively by nginx auth_basic.

    Management passwords are already enforced to be 24-256 high-entropy chars,
    so avoiding 5000-round SHA-512-crypt per request eliminates severe CPU
    stalls when ComfyUI loads 100+ static assets concurrently through nginx.
    """
    salt = secrets.token_bytes(8)
    digest = hashlib.sha1(password.encode("utf-8") + salt).digest() + salt
    return "{SSHA}" + base64.b64encode(digest).decode("ascii")


def nginx_config(directory):
    # Send a canonical path to the inference backend. Its API-key check remains
    # mandatory. Every other path, including upgrades and future routes, is admin.
    inference = "\n".join(
        f"location = {path} {{ auth_basic off; proxy_set_header Host $http_host; proxy_set_header Connection \"\"; proxy_pass http://gradio_backend{path}; }}"
        for path in INFERENCE_PATHS
    )

    comfy_static_extra = """
            location ~* ^/(assets|extensions|scripts)/ {
                proxy_buffering on;
                proxy_buffers 16 64k;
                proxy_buffer_size 64k;
                proxy_set_header Host $http_host;
                proxy_set_header X-Forwarded-Proto https;
                proxy_set_header Connection "";
                proxy_set_header Authorization "";
                proxy_pass http://comfy_backend;
            }
    """

    # On Colab (running as root), keep workers as root so they can read the 0700 temp dir & users file.
    user_directive = "user root;" if getattr(os, "geteuid", lambda: -1)() == 0 else ""

    def server(port, upstream_name, extra=""):
        return f"""
        server {{
            listen 127.0.0.1:{port};
            auth_basic "EasyColab";
            auth_basic_user_file "{directory}/users";
            if ($http_sec_fetch_site = cross-site) {{ return 403; }}
            set $origin_check $http_origin;
            if ($http_origin = https://$http_host) {{ set $origin_check ""; }}
            if ($origin_check != "") {{ return 403; }}
            {extra}
            location / {{
                proxy_set_header Host $http_host;
                proxy_set_header X-Forwarded-Proto https;
                proxy_set_header Upgrade $http_upgrade;
                proxy_set_header Connection $connection_upgrade;
                proxy_set_header Authorization "";
                proxy_pass http://{upstream_name};
            }}
        }}
        """

    return f"""
    daemon off;
    {user_directive}
    worker_processes auto;
    pid "{directory}/nginx.pid";
    error_log stderr warn;
    events {{ worker_connections 2048; }}
    http {{
        access_log off;
        sendfile on;
        tcp_nopush on;
        tcp_nodelay on;
        keepalive_timeout 65;
        keepalive_requests 1000;

        client_body_temp_path "{directory}/body";
        proxy_temp_path "{directory}/proxy";
        fastcgi_temp_path "{directory}/fastcgi";
        uwsgi_temp_path "{directory}/uwsgi";
        scgi_temp_path "{directory}/scgi";

        # Compress ComfyUI's ~15-25 MB JS/CSS/JSON frontend assets over trycloudflare (~75-80% smaller)
        gzip on;
        gzip_vary on;
        gzip_proxied any;
        gzip_comp_level 5;
        gzip_min_length 512;
        gzip_types
            text/plain text/css text/xml text/javascript
            application/javascript application/x-javascript
            application/json application/xml application/wasm
            image/svg+xml font/ttf font/otf application/font-woff application/font-woff2;

        map $http_upgrade $connection_upgrade {{ default upgrade; '' ''; }}
        client_max_body_size 0;
        proxy_http_version 1.1;
        proxy_buffering off;
        proxy_request_buffering off;
        proxy_read_timeout 3600s;
        proxy_hide_header Access-Control-Allow-Origin;

        upstream gradio_backend {{
            server 127.0.0.1:{GRADIO_BACKEND};
            keepalive 32;
        }}
        upstream comfy_backend {{
            server 127.0.0.1:{COMFY_BACKEND};
            keepalive 32;
        }}

        {server(GRADIO_INGRESS, "gradio_backend", inference)}
        {server(COMFY_INGRESS, "comfy_backend", comfy_static_extra)}
    }}
    """


@contextlib.contextmanager
def authenticated_ingress(password):
    if not re.fullmatch(r"[A-Za-z0-9_-]{24,256}", password):
        raise ValueError("Management password must contain at least 24 URL-safe ASCII characters")
    nginx = shutil.which("nginx")
    if not nginx:
        raise RuntimeError("Install nginx on the runtime before starting EasyColab")
    with tempfile.TemporaryDirectory(prefix="easycolab-auth-") as directory:
        for sub in ("body", "proxy", "fastcgi", "uwsgi", "scgi"):
            Path(directory, sub).mkdir(exist_ok=True)
        digest = _ssha_htpasswd_digest(password)
        Path(directory, "users").write_text(f"easycolab:{digest}\n", encoding="ascii")
        config = Path(directory, "nginx.conf")
        config.write_text(nginx_config(directory), encoding="utf-8")
        with subprocess.Popen([nginx, "-p", directory, "-c", str(config)]) as proxy:
            try:
                # A wrong password must be challenged by both listeners before
                # either tunnel is published. Fail if a previous proxy owns a port.
                opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
                for port in (GRADIO_INGRESS, COMFY_INGRESS):
                    for _ in range(100):
                        if proxy.poll() is not None:
                            raise RuntimeError("Authenticated ingress failed to bind")
                        try:
                            opener.open(f"http://127.0.0.1:{port}/").close()
                        except urllib.error.HTTPError as error:
                            if error.code == 401:
                                break
                            raise
                        except urllib.error.URLError:
                            time.sleep(0.05)
                            continue
                        raise RuntimeError("Ingress accepted anonymous traffic")
                    else:
                        raise RuntimeError("Authenticated ingress did not start")
                # nginx retries occupied ports for a while before exiting. Its
                # private pid file appears only after both sockets are bound.
                if not Path(directory, "nginx.pid").exists():
                    raise RuntimeError("Ingress ports are already occupied")
                yield
            finally:
                proxy.terminate()
                try:
                    proxy.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proxy.kill()
