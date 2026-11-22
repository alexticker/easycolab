# EasyColab 实例生命周期、托管接口与双协议网关

---

## 1. 实例生命周期（`colab_bridge.py`）

仅在没有可用实例，或用户明确要求唤醒/关闭 Colab VM 时执行（位于 EasyColab 仓库根目录）：

```powershell
# 查看 Google 授权与剩余 CU
python colab_bridge.py auth_status

# 唤醒 GPU 实例并启动 app.py（管理密码需为 24~256 位 [A-Za-z0-9_-]）
$env:EASYCOLAB_ADMIN_PASSWORD = "<24位以上安全密码>"
$env:EASYCOLAB_IDLE_TIMEOUT_MINUTES = "30"   # 可选
python colab_bridge.py wake --gpu G4 --app-py .\app.py   # 可选：G4 / H100 / A100 / L4 / T4

# 发送保活心跳
python colab_bridge.py keepalive

# 释放所有 Colab 运行时（停止 CU 计费）
python colab_bridge.py stop_all
```

---

## 2. 内置模型目录（`MODEL_CATALOG`）

| `model_id` | 模态 | 说明 |
|---|---|---|
| `minimax_h3` | `video_audio` | MiniMax-H3 33B BF16 · 24 FPS 视频 + 32 kHz 立体声 |
| `minimax_h3_image` | `image` | MiniMax-H3-Image 33B · 6 步原生 4K 文生图与多图指令修图 |
| `qwen38_27b_gguf` | `llm_vlm` | Qwen3.8-27B UD-Q4_K_M + MTP 加速 · 支持 256K 上下文 |
| `qwen38_27b` | `llm_vlm` | Qwen3.8-27B BF16 原生 |
| `qwen_image_21` | `image` | Qwen-Image-2.1 · 文生图与多图指令修图 |
| `z_image_turbo` | `image` | Z-Image-Turbo · 8 步极速文生图 |

---

## 3. 托管控制与异步生成接口（`/minimax_api/*`）

> 全部接口均需 **HTTP Basic Auth（`easycolab:<管理密码>`）**，POST 请求体为 JSON。

### 标准调用顺序

1. **查询状态与显存**
   - `GET /minimax_api/status?model_id=minimax_h3&touch=1`
   - 关注 `loaded_models`、`is_downloading`、`is_loading_vram`、`comfyui`。
2. **下载权重（异步）**
   - `POST /minimax_api/download_model`
   - Body：`{"model_id": "minimax_h3", "hf_token": ""}`
   - 轮询 `/minimax_api/status` 直到 `is_downloading == false`。
3. **加载模型到显存（异步）**
   - `POST /minimax_api/load_model`
   - Body：`{"model_id": "minimax_h3", "precision_mode": "bf16", "offload_mode": "disk", "context_length": 32768}`
   - 轮询 `/minimax_api/status` 直到 `is_loading_vram == false`。
4. **提交异步生成任务**
   - `POST /minimax_api/generate`
   - 常用字段：
     - `model_id`：目标模型 ID（如 `"minimax_h3"`、`"z_image_turbo"`）
     - `task_mode`：
       - 视频（`minimax_h3`）：`"t2va"`（文生有声视频）或 `"fl2va"`（首尾帧有声视频）
       - 图像（`minimax_h3_image` / `qwen_image_21` / `z_image_turbo`）：`"t2i"` 或 `"i2i_edit"`
     - `visual_prompt`（必填）、`negative_prompt`
     - 音轨提示词（`minimax_h3` 专用）：`soundscape_prompt`、`music_prompt`
     - 参考图 / 首尾帧（base64，支持裸 base64 或 data URI）：`first_frame_base64`、`last_frame_base64`
     - 尺寸与采样：`width`（默认 `832`）、`height`（默认 `480`）、`num_frames`（默认 `124`）、`num_inference_steps`（默认 `35`）、`fps`（默认 `24`）、`seed`（`42`）、`randomize_seed`（`true`）、`batch_count`（`1~8`）、`denoising_strength`（`0.70`）
   - 返回 `{"job_id": "<12位hex>", "status": "queued", ...}`。
   - **注意**：提交新任务会自动中断上一个仍在排队/运行中的 GPU 任务。
5. **轮询进度与下载成片**
   - 查进度：`GET /minimax_api/job/{job_id}`（关注 `status`、`progress`、`message`、`filename`、`completed_videos`）
   - 下载结果：`GET /minimax_api/media/{filename}`（或 `/minimax_api/video/{filename}`）
   - 中断任务：`POST /minimax_api/cancel`

### 其他管理接口

- 卸载显存：`POST /minimax_api/unload_model` — `{"model_id": "minimax_h3"}`（传空字符串 `""` 卸载全部）
- 自定义 HF 模型：
  - `GET /minimax_api/hf_trending?category=all&search_query=...`
  - `GET /minimax_api/hf_model_detail?repo_id=...`
  - `POST /minimax_api/custom_model/register` — `{"repo_id": "...", "include_pattern": "", "modality": "auto", "auto_download": false}`
  - `POST /minimax_api/custom_model/remove` — `{"model_id": "...", "delete_files": false}`
- 运行参数配置：
  - `POST /minimax_api/config_idle` — `{"idle_timeout_minutes": 30}`
  - `POST /minimax_api/config_context` — `{"context_length": 65536}`
  - `POST /minimax_api/config_api_key` — `{"api_key": "..."}`
- 断开释放实例：`POST /minimax_api/disconnect`

---

## 4. OpenAI 与 Anthropic 双协议网关（`/v1/*`）

> 需先加载对应模型到显存（LLM：`qwen38_27b_gguf` / `qwen38_27b`；生图：`minimax_h3_image` / `z_image_turbo` / `qwen_image_21`），否则返回 `503 model_not_loaded`。

- **模型列表**：`GET /v1/models`（Header：`Authorization: Bearer <apiAccessKey>`）
- **OpenAI 对话**：`POST /v1/chat/completions`（Header：`Authorization: Bearer <apiAccessKey>`）
  - 支持 `stream`、`tools`、多模态图片输入、`enable_thinking`（`true`/`false`）、`reasoning_effort`（`none`/`low`/`medium`/`high`）。
- **OpenAI 生图**：`POST /v1/images/generations`（Header：`Authorization: Bearer <apiAccessKey>`）
  - Body：`{"model": "Tongyi-MAI/Z-Image-Turbo", "prompt": "...", "size": "1024x1024", "n": 1, "steps": 8}`
  - 返回 `{"data": [{"b64_json": "..."}]}`。
- **Anthropic Messages**：`POST /v1/messages`、`POST /v1/messages/count_tokens`（Header：`x-api-key: <apiAccessKey>`）
  - 可直接作为 `ANTHROPIC_BASE_URL={BASE_URL}` 接入 Claude Code / Anthropic SDK。
