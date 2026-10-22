# Myrvo FlowGraph

Myrvo FlowGraph (`com.deathbygravitystudio.myrvo`) is a Unity editor and runtime for connecting text, image, video, and audio nodes. Its image-generation providers are OpenAI and Google Gemini.

## Features

- Graph assets store nodes, ports, edges, and variables. Create one with **Assets > Create > Myrvo > Flow Graph**.
- **Window > Myrvo > Flow Graph** opens an editor built on Unity GraphView, with a grid, zoom, pan, box selection, a minimap, port connections, and a create-node search. Double-clicking a graph asset opens the same window.
- **Execute Selected** (toolbar) and **Execute Node** (inspector) run the selected node and the upstream nodes it depends on. The selected node runs even when its cached outputs are still valid. Upstream nodes are skipped when their inputs are unchanged and a usable cached output exists. Locked nodes keep their cached outputs.
- Before a run, the graph is checked for missing nodes, broken ports, incompatible port types, and cycles.
- Image results keep a preview texture. The node content window can pan and zoom that preview, and the Crop node can edit its rectangle there.
- **Export File** writes a result under the project `Assets` folder.
- **Project Settings > Myrvo > Flow Graph** toggles node debug logging. **Project Settings > Myrvo > Flow Providers** stores provider credentials.
- **Assets > Create > Myrvo > Flow Graph Sample** creates a wired example graph.

## Nodes

The create-node search lists nodes in the Source, Generator, and Utility categories.

### Source

- **Text Input** — text authored on the node.
- **Image** — an assigned texture, exposed as an image value.
- **Capture Screenshot** — a screenshot of the Game view. In the editor the Game view must be open. Super size is an integer from 1 to 4.

### Generator

- **Text to Image**, **Image to Image**, and **Image to Image (2 Inputs)** — send the prompt, and any connected images, to the selected OpenAI or Gemini image model. The default model id is `google/gemini-2.5-flash-image`. Each node stores aspect ratio, image size, and a transparency flag, and those values are included in the provider request.
- **Text to Video** — a video value carrying the prompt and a duration in seconds.
- **Image to Video** — a video value carrying the source image label, the prompt, and a duration in seconds.

### Utility

- **Prompt Template** — replaces `{subject}`, `{style}`, and `{mood}` in a template string. Subject and mood can come from input ports. Style uses the value stored on the node.
- **Ask Text** — an answer string built from the question and the source text.
- **Ask Image** — an answer string built from the image label and the question.
- **Crop** — crops an image by x, y, width, and height.
- **Export File** — writes the connected value to a path relative to `Assets` and outputs that asset path. Image values are written from their encoded bytes, and strings are written as UTF-8 text. Video and audio values are written when the artifact contains binary data.
- **Text to Speech** — an audio value carrying the transcript and a voice name. The default voice name is `Alloy`.

**Debug Log** prints its message with `Debug.Log`. Its category is Debug, so it is omitted from the create-node search. The sample graph asset creates one in code.

## Providers

Plugins marked with `FlowProviderPlugin` are discovered at runtime. Image nodes call `FlowProviderExecutionService.ExecuteImageRequestAsync` for the model selected on the node. Set the API key and base URL in **Project Settings > Myrvo > Flow Providers** before running those nodes.

### OpenAI (`openai`)

Settings are an API key and a base URL. The default base URL is `https://api.openai.com/v1`. Text-to-image calls `POST /images/generations`. Requests that include source images call `POST /images/edits`.

| Model id | Label | Requests |
| --- | --- | --- |
| `openai/gpt-image-2` | GPT Image 2 | Text to Image, Image to Image, Image to Image (2 Inputs) |
| `openai/gpt-image-1.5` | GPT Image 1.5 | Text to Image, Image to Image, Image to Image (2 Inputs) |
| `openai/gpt-image-1` | GPT Image 1 | Text to Image, Image to Image, Image to Image (2 Inputs) |
| `openai/gpt-image-1-mini` | GPT Image 1 Mini | Text to Image, Image to Image, Image to Image (2 Inputs) |
| `openai/dall-e-3` | DALL-E 3 | Text to Image |

### Google Gemini (`google`)

Settings are an API key and a base URL. The default base URL is `https://generativelanguage.googleapis.com/v1beta`. Image requests call `{baseUrl}/models/{model}:generateContent`.

| Model id | Label | Requests |
| --- | --- | --- |
| `google/gemini-2.5-flash-image` | Gemini 2.5 Flash Image | Text to Image, Image to Image, Image to Image (2 Inputs) |
| `google/gemini-3.1-flash-image-preview` | Gemini 3.1 Flash Image | Text to Image, Image to Image, Image to Image (2 Inputs) |
| `google/gemini-3-pro-image-preview` | Gemini 3 Pro Image | Text to Image, Image to Image, Image to Image (2 Inputs) |

## Install

This repository is a Unity package. `package.json` declares Unity **2022.3** as the minimum version. The package name is `com.deathbygravitystudio.myrvo`.

In Unity, open **Window > Package Manager**, choose the **+** menu, then **Add package from git URL**, and enter:

```
https://github.com/denchi/UnityAIFlow.git
```

Or add the dependency to the project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.deathbygravitystudio.myrvo": "https://github.com/denchi/UnityAIFlow.git"
  }
}
```

## Quick start

1. Add the package with the git URL above.
2. Use **Assets > Create > Myrvo > Flow Graph Sample**. The sample connects Text Input to Prompt Template and Ask Text, then connects the answer to Debug Log and Export File.
3. Open **Window > Myrvo > Flow Graph** if the asset did not open it. Select the Export File node and click **Execute Selected**, or **Execute Node** in the inspector.
4. For image generation, create **Assets > Create > Myrvo > Flow Graph**. Right-click the canvas and choose **Create Node**, then connect a Text Input prompt into Text to Image. Enter an API key under **Project Settings > Myrvo > Flow Providers**, select the image node, and execute it.

Dragging a port onto empty canvas space opens the same node search, limited to ports that can connect. **Mark Graph Dirty** and per-node **Mark Dirty** mark nodes so a later dependency walk recomputes them.

## API keys

Open **Edit > Project Settings > Myrvo > Flow Providers** and enter the API key for OpenAI, Google Gemini, or both. Each key is a password field. Keys and base URLs are stored in `UserSettings/MyrvoFlowProviderProjectSettings.asset`. They are not written into flow graph assets.

Never commit API keys. Keep them out of graph assets, scripts, scenes, and this repository, and keep the Unity project's `UserSettings` folder out of version control.

## Licence

Myrvo FlowGraph is released under the MIT License. Copyright (c) 2026 Denis Chiriac / Death By Gravity Studio. See [LICENSE](LICENSE).
