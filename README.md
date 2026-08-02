# Myrvo FlowGraph

This package scaffolds a `ScriptableObject`-backed flowgraph system for Unity:

- graph asset persistence
- node, port, edge, and variable models
- selected-node execution with dependency-aware dirty recompute
- editor authoring surface with pan, zoom, minimap, selection, framing, and edge creation

## Local project shell

This repository now includes a minimal Unity project wrapper:

- [Packages/manifest.json](/Users/denis/Documents/myrvo-unity/Packages/manifest.json)
- [ProjectSettings/ProjectVersion.txt](/Users/denis/Documents/myrvo-unity/ProjectSettings/ProjectVersion.txt)

That makes it possible to open the workspace directly in the installed Unity editor and validate the package against a real project layout.

The package also includes a batch validation entrypoint:

- `Myrvo.FlowGraph.Editor.FlowGraphBatchValidation.CreateAndRunSampleGraph`

## Current renderer choice

The editor surface uses Unity's graph tooling APIs instead of a custom canvas implementation, which keeps the authoring workflow aligned with Unity's node-editor patterns.

On this machine, the installed editor is `Unity 2021.3.45f2`, and the discoverable shipped graph surface is `UnityEditor.GraphViewModule` plus package implementations such as Shader Graph and VFX Graph. No local `com.unity.graphtools.foundation` package was available to compile against, so the package is structured so the authoring/runtime model stays stable while the editor backend can later be swapped to a GraphTools Foundation implementation in a newer Unity baseline.

## Included node set

- `Text Input`
- `Image`
- `Capture Screenshot`
- `Text to Image`
- `Image to Image`
- `Image to Image (2 Inputs)`
- `Text to Video`
- `Image to Video`
- `Ask Image`
- `Crop`
- `Export File`
- `Ask Text`
- `Text to Speech`
- `Prompt Template`
- `Debug Log`

## Execution model

- Select a node in the Flow Graph window and use `Execute Node` in the inspector, or `Execute Selected` in the toolbar.
- The runner walks upstream dependencies for that node, re-executing only nodes marked dirty or nodes whose dependencies changed.
- Trigger ports remain available for compatibility, but the primary authoring flow is node-centric execution.

## Companion-app extension path

- Register new node types with `FlowNodeAttribute`
- Authoring search automatically discovers registered node types through `FlowNodeRegistry`
- Variables are exposed in a graph blackboard, which keeps the editor layout closer to companion-style graph tooling
- Use `Assets/Create/Myrvo/Flow Graph Sample` to generate a runnable example graph asset in a fresh project

## Next integration step

Map the `project-companion-app` node catalog into additional `FlowNodeModel` types and, if required by your target Unity version, replace the editor backend with the preferred GraphTools Foundation integration layer while preserving the same `FlowGraphAsset` runtime model.
