import os
import argparse
import xml.etree.ElementTree as ET
import shutil
from collections import defaultdict


def main():
    try:
        main_internal()
    except Exception as ex:
        print(f"Exception occurred in script: {ex}")
        exit(-1)


def main_internal():
    script_options = parse_options()
    print("Options:", script_options)
    convert_annotations_to_yolo(script_options.input_annotations_files, script_options.output_directory)
    return


def convert_annotations_to_yolo(input_annotations_files, output_directory):
    output_folder = os.path.abspath(output_directory)
    if os.path.exists(output_folder):
        shutil.rmtree(output_folder)
    os.makedirs(output_folder, exist_ok=True)

    images_by_path = []
    for annotations_file in input_annotations_files:
        images_by_path.extend(prepare_images(annotations_file))

    save(output_folder, images_by_path, output_directory)

    print(f"Processing completed, images: {len(images_by_path)}")
    return

def prepare_images(annotations_file):
    annotations = parse_annotations(annotations_file)
    print(annotations)

    images_folder = os.path.dirname(annotations_file)
    all_images = {
        os.path.splitext(image_file)[0] + os.path.splitext(image_file)[1]: os.path.join(images_folder, image_file)
        for image_file in os.listdir(images_folder)
        if image_file.lower().endswith((".png", ".jpg", ".jpeg", ".bmp"))
    }

    if len(annotations.images) != len(all_images):
        print(
            f"Different number of annotated/unannotated images: {len(annotations.images)} vs {len(all_images)}"
        )

    images = []
    for image in annotations.images:
        matching_image_file = all_images.get(image.name)
        if matching_image_file is None:
            print(f"Failed to find annotated image {image.name} in {images_folder}")
            continue

        print(f"Processing {image.name}, tags: {len(image.tags)}")
        yolo_image = YoloImage(
            image_file=matching_image_file,
            tags=image.tags
        )
        images.append(yolo_image)
    return images


def save(output_folder, images, use_symlinks):
    if not os.path.exists(output_folder):
        os.makedirs(output_folder)

    labels = defaultdict(lambda: None)
    idx = 0

    for image in images:
        for tag in image.tags:
            class_name = tag.label
            if labels[class_name] is None:
                labels[class_name] = YoloLabel(index=idx, name=class_name)
                idx += 1

    image_train_count = 75
    image_valid_count = 25
    image_test_count = 0
    print(
        f"Splitting {len(images)} images to train/valid/test: {image_train_count}/{image_valid_count}/{image_test_count}%"
    )
    names = ["train", "val", "test"]
    split = split_collection(images, image_train_count, image_valid_count, image_test_count)

    print(f"Writing the results of a split to {output_folder}")

    for collection_name in names:
        os.makedirs(os.path.join(output_folder, collection_name), exist_ok=True)

    for i, images_collection in enumerate(split):
        images_collection_name = names[i]
        collection_folder_path = os.path.join(output_folder, images_collection_name)

        print(
            f"Writing '{images_collection_name}' to {collection_folder_path}, count: {len(images_collection)}"
        )

        for image in images_collection:
            for tag in image.tags:
                tag_folder_path = os.path.join(collection_folder_path, tag.label)
                os.makedirs(tag_folder_path, exist_ok=True)
                destination_image_file = os.path.join(tag_folder_path, os.path.basename(image.image_file))
                if use_symlinks:
                    os.symlink(
                        image.image_file,
                        destination_image_file)
                else:
                    shutil.copy2(
                        image.image_file,
                        destination_image_file)


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


def parse_annotations(file_path):
    tree = ET.parse(file_path)
    root = tree.getroot()

    images = []
    for image_elem in root.findall("image"):
        id = int(image_elem.attrib["id"])
        name = image_elem.attrib["name"]
        width = int(image_elem.attrib["width"])
        height = int(image_elem.attrib["height"])

        tags = [
            Tag(
                label=tag_elem.attrib["label"],
                source=tag_elem.attrib["source"],
            )
            for tag_elem in image_elem.findall("tag")
        ]

        images.append(
            Image(
                id=id,
                name=name,
                width=width,
                height=height,
                tags=tags
            )
        )

    return Annotations(annotations_file=file_path, images=images)


def split_collection(collection, *part_percentages):
    """
    Splits a collection into parts according to the specified percentages.

    Args:
        collection (list): The collection to be split.
        *part_percentages (int): The percentages to split the collection.

    Returns:
        list: A list of parts of the original collection.

    Raises:
        ValueError: If the collection is empty, no percentages are specified,
                    a percentage is negative, percentages don't sum up to 100,
                    or not enough items for each non-zero percentage part to have at least one item.

    Examples:
        split_collection([1, 2, 3, 4, 5], 20, 30, 50) returns [[1], [2, 3], [4, 5]]
        split_collection([1, 2, 3, 4, 5, 6], 33, 33, 34) returns [[1, 2], [3, 4], [5, 6]]
        split_collection(['a', 'b', 'c', 'd', 'e'], 50, 50) returns [['a', 'b', 'c'], ['d', 'e']]
        split_collection([1, 2, 3, 4, 5, 6, 7, 8], 25, 25, 25, 25) returns [[1, 2], [3, 4], [5, 6], [7, 8]]
        split_collection([1, 2, 3, 4, 5, 6, 7, 8, 9], 33, 33, 34) returns [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
    """
    # Check if collection is empty
    if not collection:
        raise ValueError("The collection cannot be empty.")

    # Check if part percentages are specified
    if not part_percentages:
        raise ValueError("At least one part percentage must be specified.")

    # Check if part percentages are non-negative
    if any(p < 0 for p in part_percentages):
        raise ValueError("Part percentages must be positive.")

    # Check if sum of part percentages is 100
    total_percentage = sum(part_percentages)
    if abs(total_percentage - 100) > 0.0001:
        raise ValueError("The sum of the part percentages must equal 100.")

    # Size of the collection
    collection_size = len(collection)

    # Check if collection has at least as many items as there are parts
    part_count = len(part_percentages)
    non_zero_part_count = len(list(filter(lambda p: p > 0, part_percentages)))
    if collection_size < non_zero_part_count:
        raise ValueError("The collection must have at least as many items as there are parts.")

    # Initially distribute at least one item to each part with non-zero percentage
    part_sizes = [1 if p > 0 else 0 for p in part_percentages]

    # Calculate remaining items after initial distribution
    remaining = collection_size - sum(part_sizes)

    # Check if there are enough items for each non-zero percentage part to have at least one item
    if remaining < 0:
        raise ValueError("Not enough items for each non-zero percentage part to have at least one item.")

    # Distribute the rest of the items proportionally
    rest_parts_sizes = [int(round(remaining * (p / total_percentage))) for p in part_percentages]
    part_sizes = [part_sizes[i] + rest_parts_sizes[i] for i in range(part_count)]

    # Distribute any items left due to rounding
    remaining = collection_size - sum(part_sizes)
    for i in range(remaining):
        part_sizes[i] += 1

    # Split collection into parts
    parts = []
    current_index = 0
    for part_size in part_sizes:
        part = collection[current_index: current_index + part_size]
        parts.append(part)
        current_index += part_size

    # Return the parts
    return parts


class Annotations:
    def __init__(self, annotations_file, images):
        self.annotations_file = annotations_file
        self.images = images


class Image:
    def __init__(self, id, name, width, height, tags):
        self.id = id
        self.name = name
        self.width = width
        self.height = height
        self.tags = tags


class Tag:
    def __init__(
            self, label, source
    ):
        self.label = label
        self.source = source


class YoloLabel:
    def __init__(self, index, name):
        self.index = index
        self.name = name


class YoloImage:
    def __init__(self, image_file, tags):
        self.image_file = image_file
        self.tags = tags


class ScriptOptions:
    def __init__(self, input_annotations_files, output_directory, use_symlinks):
        self.input_annotations_files = input_annotations_files
        self.output_directory = output_directory
        self.use_symlinks = use_symlinks


if __name__ == "__main__":
    main()
