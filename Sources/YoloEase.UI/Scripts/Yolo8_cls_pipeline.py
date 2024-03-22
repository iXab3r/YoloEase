import os
import argparse
import shutil
from ultralytics import YOLO
from ConvertCVATtoYolo8_cls import convert_annotations_to_yolo


def main():
    try:
        main_internal()
    except Exception as ex:
        print(f"Exception occurred in script: {ex}")
        exit(-1)


def main_internal():
    script_options = parse_options()
    print("Options:", script_options)

    output_folder = os.path.abspath(script_options.output_directory)

    # Convert CVAT annotations to YOLO format
    convert_annotations_to_yolo(script_options.input_annotations_files, output_folder)

    # Load the model
    model = YOLO("yolov8n-cls.pt")  # Load the pre-trained model
    
    save_directory = os.path.join(output_folder, "runs")

    # Define the training parameters
    model.train(
        data=output_folder,  # Path to the data.yaml file
        imgsz=100,  # Image size for training
        epochs=100,  # Number of training epochs
        task="classify",  # Task (classify, detect, segment)
        mode="train",  # Training mode,
        project=save_directory
    )

    # Load the trained model
    trained_model_path = os.path.join(save_directory, "train", "weights", "best.pt")
    model = YOLO(trained_model_path)

    # Export the model to ONNX format
    model.export(format="onnx")
    return


def parse_options():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--inputAnnotationsFiles",
        help="Paths to CVAT 1.1 annotation file (annotations.xml)",
        required=True,
        nargs="+",
    )
    parser.add_argument(
        "--outputDirectory",
        help="Path to output directory",
        required=True,
    )
    parser.add_argument(
        "--symlinks",
        help="Use symbolic links instead of copying files",
        action="store_true"
    )
    args = parser.parse_args()

    return ScriptOptions(
        input_annotations_files=args.inputAnnotationsFiles,
        output_directory=args.outputDirectory,
        use_symlinks=args.symlinks
    )


class ScriptOptions:
    def __init__(self, input_annotations_files, output_directory, use_symlinks):
        self.input_annotations_files = input_annotations_files
        self.output_directory = output_directory
        self.use_symlinks = use_symlinks


if __name__ == "__main__":
    main()
