# EasyColab ComfyUI 双层操作指南

在 EasyColab 上操作 ComfyUI 分为两层，**两层均使用同一套 HTTP Basic Auth（`easycolab:<管理密码>`）**：
1. **EasyColab 控制层**（`{BASE_URL}/minimax_api/comfyui/*`）：启停服务、安装预置包、下权重、释放显存、同步 Drive。
2. **ComfyUI 原生执行层**（`{COMFY_URL}/*`）：查节点与模型文件、上传参考图、提交 API 格式工作流、轮询历史与下载结果。

---

## 1. 控制层接口（`/minimax_api/comfyui/*`）

| 操作 | 方法与路径 | 请求体 / 说明 |
|---|---|---|
| 查状态与公网地址 | `GET /minimax_api/comfyui/status` | 返回 `comfyui.is_running`、`is_starting`、`public_url`、`queue_running`、`queue_pending`、`preset_packs`、`recent_outputs` |
| 启动 ComfyUI | `POST /minimax_api/comfyui/start` | `{"unload_existing": true, "force_restart": false}`（显存紧张时建议 `unload_existing: true`） |
| 停止 ComfyUI | `POST /minimax_api/comfyui/stop` | 停止前自动同步数据至 Google Drive |
| 释放显存 | `POST /minimax_api/comfyui/free_vram` | 卸载 ComfyUI 显存模型但不关停服务 |
| 同步到 Drive | `POST /minimax_api/comfyui/sync_drive` | 同步 `custom_nodes`、`models`、`workflows`、`output` 到 Google Drive |
| 下载模型权重 | `POST /minimax_api/comfyui/download_weight` | `{"url_or_repo": "org/repo", "filename": "path/file.safetensors", "subfolder": "diffusion_models", "hf_token": ""}`（`subfolder` 可选 `diffusion_models`、`checkpoints`、`loras`、`vae`、`text_encoders`、`clip_vision`、`latent_upscale_models` 等） |
| 安装预置工作流包 | `POST /minimax_api/comfyui/install_pack` | `{"pack_id": "<id>", "auto_start": true, "hf_token": ""}` |
| 拉取输出文件 | `GET /minimax_api/comfyui/output_media/{rel_path}` | 下载 ComfyUI `output/` 目录下的生成产物 |

### 内置 4 套预置工作流包（`pack_id`）

| `pack_id` | 内容 | 体积 |
|---|---|---|
| `wan22_i2v_flf2v` | 万相 Wan2.2 14B 4 步极速单图/首尾帧视频 | ~35.4 GB |
| `minimax_h3_i2v_flf2v` | MiniMax-H3 Turbo 8 步单图/首尾帧有声视频 | ~39.1 GB |
| `ltx23_flf2v` | LTX-2.3 22B 首尾帧电影运镜有声视频 | ~36.3 GB |
| `hunyuan15_i2v_1080p` | 混元 HunyuanVideo 1.5 单图直出 720P + 1080P 超分 | ~43.5 GB |

---

## 2. 原生执行层（直连 `comfyui.public_url`）

当 `comfyui.is_running == true` 时，取 `COMFY_URL = status["comfyui"]["public_url"].rstrip("/")`。

### 标准执行步骤

1. **先查环境与真实模型文件名（切勿盲猜文件名或节点参数）**
   - `GET {COMFY_URL}/system_stats`：检查服务健康与显存。
   - `GET {COMFY_URL}/object_info/{NodeClass}`（如 `UNETLoader`、`CheckpointLoaderSimple`、`LoraLoaderModelOnly`、`CLIPLoader`、`VAELoader`）：获取该节点支持的准确参数与云端已安装的模型文件名列表。
2. **上传输入图（I2V / FLF2V / 图生图时）**
   - `POST {COMFY_URL}/upload/image`（`multipart/form-data`，字段 `image=@local.png`、`type=input`、`overwrite=true`）。
   - 返回 `{"name": "local.png", "subfolder": "", "type": "input"}`，将 `name` 填入 `LoadImage.inputs.image`。
3. **提交 API 格式工作流**
   - `POST {COMFY_URL}/prompt`
   - Body：`{"prompt": {"<node_id>": {"class_type": "...", "inputs": {...}}}, "client_id": "agent-1"}`
   - **关键避坑**：`/prompt` **只接受 API 格式**（`{node_id: {class_type, inputs}}`），**不接受** ComfyUI 前端画布保存的 UI Graph 格式（含 `nodes`/`links` 数组的 JSON）。务必先构造或转换为 API 格式再提交。
   - 返回 `{"prompt_id": "<uuid>", "node_errors": {}}`；若 `node_errors` 非空，需先修正节点参数。
4. **轮询状态并下载产物**
   - 队列状态：`GET {COMFY_URL}/queue`（`queue_running`、`queue_pending`）。
   - 执行结果：`GET {COMFY_URL}/history/{prompt_id}`。当响应包含 `prompt_id` 时，从 `history[prompt_id]["outputs"]` 中提取 `images` / `gifs` / `videos` / `audio` 里的 `filename`、`subfolder`、`type`。
   - 下载文件：`GET {COMFY_URL}/view?filename=<name>&subfolder=<sub>&type=output`（或经 EasyColab 的 `GET {BASE_URL}/minimax_api/comfyui/output_media/<rel_path>`）。
5. **中断与显存释放**
   - 中断当前出图：`POST {COMFY_URL}/interrupt`
   - 释放显存：`POST {COMFY_URL}/free`（Body：`{"unload_models": true, "free_memory": true}`）

---

## 3. 最小 Python 调用示例

```python
import json, os, time, requests

# 读取 Windows 本机配置
cfg_path = os.path.join(os.environ["LOCALAPPDATA"], "EasyColab", "settings.json")
cfg = json.load(open(cfg_path, encoding="utf-8"))
BASE_URL = cfg["colabServerURL"].rstrip("/")
ADMIN_PW = cfg["adminPasswordsByURL"].get(BASE_URL) or cfg["adminPasswordsByURL"].get(BASE_URL + "/")
AUTH = ("easycolab", ADMIN_PW)

# 1. 获取 ComfyUI 公网地址
st = requests.get(f"{BASE_URL}/minimax_api/status?touch=1", auth=AUTH, timeout=30).json()
comfy_url = st["comfyui"]["public_url"].rstrip("/")

# 2. 提交 API 格式工作流并轮询
resp = requests.post(f"{comfy_url}/prompt", auth=AUTH, json={"prompt": workflow_api_dict}, timeout=30).json()
prompt_id = resp["prompt_id"]
while True:
    hist = requests.get(f"{comfy_url}/history/{prompt_id}", auth=AUTH, timeout=30).json()
    if prompt_id in hist:
        outputs = hist[prompt_id]["outputs"]
        break
    time.sleep(3)
```

---

## 4. 常见排错

- **`401 Unauthorized`**：检查用户名是否为 `easycolab`，以及 `adminPasswordsByURL` 键名是否带/不带末尾 `/`。
- **`403 Forbidden`**：移除请求中的 `Origin` 或 `Sec-Fetch-Site` 头。
- **`/prompt` 返回 `400` 或 `node_errors`**：不要直接提交 UI Graph JSON；用 `GET {COMFY_URL}/object_info/{NodeClass}` 核对节点名、必填输入与模型文件名。
- **OOM 显存不足**：先调 `POST {BASE_URL}/minimax_api/unload_model`（`{"model_id": ""}`）清空 EasyColab 内置模型，再跑 ComfyUI。
