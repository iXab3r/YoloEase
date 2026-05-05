# YoloEase Summary

## What This Project Is

YoloEase is a desktop application for iterative YOLO model training. It is designed around a repeated loop:

1. collect raw images or video-derived frames
2. sync them into project-managed storage
3. create annotation tasks
4. get annotations back
5. convert the annotated data into YOLO format
6. train or retrain a model
7. use the latest model to help with the next annotation round

The product goal is to reduce the manual work around dataset preparation and retraining, not just to run a single training job.

## Product Shape

The app is project-based. A user opens a `.yeproj` file, and that project becomes the entry point for:

- data source definitions
- project settings
- model and training configuration
- annotation state
- synced local copies of training files
- generated datasets
- training history and automation state

The app is now oriented around the built-in offline annotation workspace. The overall training pipeline still expects CVAT-shaped annotation exports so the downstream dataset creation flow stays stable.

## Current Annotation Backend

YoloEase keeps the backend seam internally, but the user-facing app now operates in offline mode only:

- `Offline` mode: keeps annotation state inside the project workspace on disk and exposes a built-in project tab inside the app

Offline mode is intended to be portable. The `.yeproj` file is the entry point, and the offline workspace lives in a sibling folder next to that file. Copy both and you copy the full project state. Legacy project files that still say `CVAT` are opened as offline projects while preserving their stored project workspace path where possible.

## High-Level Workflow

### 1. Input Data

The user points the app at source data such as images or videos. Source files are treated as upstream input. During sync, the app copies the needed files into project-owned storage so the rest of the pipeline works from a stable local snapshot.

### 2. Task Creation

The app selects the next batch of files that should be annotated. This can be driven by project state, previous annotations, and prediction results from the latest trained model.

### 3. Annotation

Historically this step was done through CVAT. The offline backend keeps the same mental model:

- tasks
- jobs
- labels with colors
- task status changes such as new, in progress, completed

Offline mode manages labels, task metadata, jobs, files, and task state inside project storage.

### 4. Dataset Preparation

Annotated tasks are exported in a CVAT-style format. That export is then converted into YOLO dataset structure so the rest of the training stack does not need to care whether the annotations came from CVAT or from the offline backend.

### 5. Augmentation and Training

The prepared dataset can be augmented, split into train/validation sets, and passed into YOLO training. The app also keeps track of training progress and generated model outputs.

Training runs through a managed Python toolchain under the application's data directory. The prerequisites workflow owns Python, the virtual environment, PyTorch, Ultralytics, ONNX Runtime, and related packages so training does not depend on global PATH state. When a compatible NVIDIA driver is detected, the managed environment can install CUDA-enabled PyTorch wheels and training selects the first CUDA device automatically; otherwise CPU training remains valid.

### 6. Prediction-Assisted Iteration

Once a model exists, the app can run predictions on unannotated data and use those results to support the next round of annotation work. This is part of the intended feedback loop rather than a separate standalone feature.

## Important Architectural Idea

The annotation system is only one part of the app. The broader pipeline is:

- data ingestion and sync
- annotation task management
- annotation export/import
- dataset conversion
- augmentation
- training
- prediction
- retraining automation

Because of that, replacing CVAT is not just a UI task. The safest migration path is to preserve the annotation export contract so the rest of the pipeline can remain mostly unchanged.

## Repo Layout

### `Sources/YoloEase.UI`

The main desktop application. This contains:

- the app shell and tabs
- project settings UI
- data source and local file handling
- offline annotation backend integration
- training workflow UI
- augmentation features
- YOLO integration
- Python helper scripts used for conversion and tool bridging

Important subareas inside this project:

- `Core`: application logic and orchestration
- `ProjectTree`: project settings and configuration UI
- `TrainingTimeline`: automation and progress flow for retraining
- `Augmentations`: dataset augmentation UI and logic
- `Yolo`: training and prediction integration
- `Cvat`: legacy CVAT-compatible helpers and contracts kept for migration and CVAT-shaped export compatibility
- `Scripts`: Python helpers for annotation conversion and related tasks

### `Sources/YoloEase.Cvat.Shared`

Shared/generated pieces related to CVAT communication.

### `Sources/YoloEase.Tests`

Automated tests.

### `Submodules`

Third-party or shared dependencies brought in as git submodules.

## Offline Mode Storage Model

Offline mode is meant to be self-contained and easy to move around. The intended shape is:

- `.yeproj` file as the project entry point
- sibling workspace folder holding synced assets and annotation state

That workspace mirrors annotation concepts that previously lived in CVAT, such as:

- project metadata
- labels and colors
- tasks
- jobs
- per-task file lists
- annotation exports
- task status and revision-like state

This keeps the system understandable and makes it easier to preserve the existing training pipeline.

## Boundaries And Current Constraints

- The app is offline-first and no longer exposes a CVAT backend selection mechanism.
- The rest of the pipeline still benefits from CVAT-compatible exports.
- Offline annotation editing is not feature-complete yet; task-state flow exists first.
- Sync currently copies source files into project storage, and that behavior is intentionally preserved for now.
- The project is optimized for iterative improvement of a dataset and model over time, not for one-off import/export only.

## Good Mental Model For Future Work

Think of YoloEase as an orchestration layer around the full annotation-to-training loop.

The most important system seams are:

- source data vs project-owned synced data
- annotation backend vs dataset export format
- raw annotations vs YOLO-ready dataset
- manual annotation work vs automated retraining
- current model predictions vs next batch selection

When changing the project, try to preserve these seams unless you are intentionally redesigning the whole loop.
