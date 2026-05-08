# YoloEase

YoloEase is a desktop tool for iterative YOLO model training.

It helps you turn images or screen recordings into a model that can be used by EyeAuras `ML Search`: collect frames, create annotation tasks, train a model, run predictions, improve the next batch, and export ONNX weights.

The current app is project-based and includes its own annotation workspace, managed prerequisites, video frame extraction, training timeline, prediction-assisted annotation, and dataset augmentations.

## Quick Links

- [Download latest release](https://github.com/iXab3r/YoloEase/releases/)
- [Russian getting started guide](https://wiki.eyeauras.net/ru/YoloEase/getting-started)
- [Prerequisites guide](https://wiki.eyeauras.net/ru/YoloEase/prerequisites)
- [Data sources guide](https://wiki.eyeauras.net/ru/YoloEase/features/data-sources)
- [Recorded demo session](https://youtu.be/MdLETBZPeec)
- [Ready-to-use EyeAuras pack](https://eyeauras.net/share/S20260507232328uRAOxLRumL6o)

## Demo Assets

- [ONNX weights](https://s3.eyeauras.net/media/2026/05/Qwe_yolo11s_202605072245262GXYDdJshE3b.onnx)
- [PyTorch weights](https://s3.eyeauras.net/media/2026/05/Qwe_yolo11s_202605072245262GXYDdJshE3b.pt)
- [Full YoloEase project with training history](https://s3.eyeauras.net/media/2026/05/AimTrainerDemo.zip)

## Workflow

1. Create a `.yeproj` project.
2. Add images, folders, or video files.
3. Extract frames from video when needed.
4. Define labels and annotate the first task manually.
5. Train locally or prepare a Google Colab run.
6. Use the latest model to predict and auto-annotate the next tasks.
7. Export the trained ONNX model and use it in EyeAuras.

## Training Modes

**Local Training** runs on your own machine. YoloEase manages a portable Python environment, required Python packages, PyTorch runtime, Ultralytics, and ONNX tooling from the `Prerequisites` tab.

**Google Colab** is available when you want to run training outside your local machine.

## What YoloEase Automates

- Project-owned copies of training assets.
- Annotation task creation and status tracking.
- Dataset generation for YOLO training.
- Optional image augmentations.
- Training timeline and progress reporting.
- Prediction previews for the latest model.
- ONNX output for EyeAuras automation.

YoloEase is still evolving quickly. Please report problems or suggestions through the app or the repository issues.
