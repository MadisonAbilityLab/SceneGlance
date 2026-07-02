# SceneGlance: Python Backend

The ML inference server for SceneGlance. It receives sensor video streamed from the
[HoloLens 2 Unity frontend](../unity), runs instance segmentation on each frame, and streams the
recognition results (masks, boxes, classes, geometry) back to the headset for AR visualization.

## Requirements

- **Python 3.8+**
- A **CUDA-capable GPU** (models run on `cuda:0`)
- **PyTorch**, 2.1.2 recommended for mmdetection compatibility (install the build matching your CUDA)
- **mmdetection** stack (`mmengine`, `mmcv`, `mmdet`). After installing PyTorch, run:
  ```bash
  pip install -U openmim
  mim install mmengine "mmcv>=2.0.0"
  mim install mmdet
  ```
- Additional Python packages:
  ```bash
  pip install opencv-python av protobuf numpy
  pip install ujson   # optional, faster JSON parsing
  ```

## Model weights

Fine-tuned checkpoints are published as assets on the
[GitHub Releases](../../../releases) page. Download them and place them in
[`mmdetection/checkpoints/`](./mmdetection/checkpoints) to match the paths in
[`mmdetection/api.py`](./mmdetection/api.py), e.g. `kitchen_102.pth`, `vistas_120.pth`. The corresponding model configs (`rtmdet-ins_l_1xb4-150e_*.py`) are already
provided there. Select the active model with the `scenario` variable at the top of `apps.py`
(`'kitchen'`, `'vistas'`, or `'coco'`).

## Running

Make sure the HoloLens 2 is on the same network with
[*Research Mode*](https://learn.microsoft.com/en-us/windows/mixed-reality/develop/advanced-concepts/research-mode)
enabled and the SceneGlance Unity app running, then launch the backend with the headset's IP address:

```bash
python apps.py --host <HoloLens IP>
```

The system takes ~20 seconds to initialize after connecting. The control-panel window lets you
adjust colors, designs, contour thickness, label sizes, and pause/resume recognition streaming.

## Optional: debug log server

To receive on-device debug logs from the HoloLens on this PC, run:

```bash
python debugserver.py
```

It listens on port `9031`. Set this PC's IP address and port `9031` in the Unity `DebugServer`
component before deploying (see the frontend README).
