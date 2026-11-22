---
name: easycolab
description: 操作 EasyColab 云端 GPU 服务、REST 接口、OpenAI/Anthropic 双协议网关与 ComfyUI 工作流。当用户需要检查/唤醒/释放 EasyColab 实例，下载/加载/卸载模型（MiniMax-H3、MiniMax-H3-Image、Qwen3.8-27B、Qwen-Image-2.1、Z-Image-Turbo、自定义 HF 模型），调用 EasyColab 生图/生视频或 /v1/chat/completions、/v1/messages 接口，或在 EasyColab 上启动、安装工作流包并运行 ComfyUI 时激活。
---

# EasyColab 操作指南

本 Skill 采用按需加载结构：先用本页完成连接与鉴权，再按任务读取对应参考文档。

## 1. 先发现现有连接与凭证

在唤醒新实例前，优先读取本机已保存的活跃服务配置：
- **Windows**：`%LOCALAPPDATA%\EasyColab\settings.json`
- **Linux**：`~/.config/EasyColab/settings.json`
- **macOS**：EasyColab 应用的 `UserDefaults`

关键字段：
- `colabServerURL`：EasyColab 服务根地址（如 `https://xxxx.gradio.live`，去掉末尾 `/`）
- `adminPasswordsByURL`：字典，按服务地址映射 **管理密码**（24～256 位）
- `apiAccessKey`：`/v1/*` 推理网关专用 **API 密钥**
- `hfToken`：可选 Hugging Face Token

先发一次探活确认实例在线（同时重置闲置看门狗）：
`GET {BASE_URL}/minimax_api/status?touch=1`（带管理密码 Basic Auth）。

## 2. 双鉴权规则（必读）

EasyColab 底层 nginx（`runtime_auth.py`）使用两套独立鉴权：

| 访问面 | 路径 / 地址 | 鉴权方式 |
|---|---|---|
| **管理控制面** | `/minimax_api/*`、Gradio 界面 | **HTTP Basic Auth**：用户名 `easycolab`，密码为管理密码 |
| **ComfyUI 隧道** | `comfyui.public_url`（`*.trycloudflare.com`） | **HTTP Basic Auth**：用户名 `easycolab`，密码同上 |
| **推理网关** | `/v1/models`、`/v1/chat/completions`、`/v1/images/generations`、`/v1/messages` | **不用 Basic Auth**；使用 `Authorization: Bearer <apiAccessKey>`（OpenAI）或 `x-api-key: <apiAccessKey>`（Anthropic） |

> **避坑**：请求 nginx 保护的接口时不要附带外部 `Origin` 或 `Sec-Fetch-Site: cross-site` 请求头，否则返回 `403`。

## 3. 按需读取详细文档

- **唤醒/停止实例、调 `/minimax_api/*` 生图/生视频、调 `/v1/*` 网关** → 读取 [references/api.md](references/api.md)
- **启动/管理 ComfyUI、安装预置工作流包、直连 ComfyUI API 跑工作流** → 读取 [references/comfyui.md](references/comfyui.md)

## 4. 核心协作与安全底线

- **显存互斥**：EasyColab 内置模型与 ComfyUI 共享同一张 GPU。切到 ComfyUI 前可卸载内置模型（`unload_model`）或在启动时传 `unload_existing: true`；切回内置模型前调用 `comfyui/free_vram`。
- **算力成本**：Colab GPU 空转会持续消耗 CU。任务结束且用户不再需要时，提醒释放实例（`POST /minimax_api/disconnect` 或 `colab_bridge.py stop_all`）。
- **联动 Skill**：写 MiniMax-H3 视频提示词时先用 `h3-prompt-writing`；做 EasyColab 之外的通用 Colab 运维时用 `colab-operator`。
