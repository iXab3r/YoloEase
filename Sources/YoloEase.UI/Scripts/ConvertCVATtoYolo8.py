import os
import argparse
import xml.etree.ElementTree as ET
import shutil
import cv2
import logging
import numpy as np
import matplotlib.pyplot as plt
from collections import defaultdict
from shapely.geometry import Polygon, LineString, GeometryCollection
from dataclasses import dataclass
from shapely.ops import split

# Set up logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)


def main():
    try:
        main_internal()
    except Exception as ex:
        logger.error(f"Exception occurred in script: {ex}")
        exit(-1)


def main_internal():
    script_options = parse_options()
    logger.info("Options:", script_options)

    output_folder = os.path.abspath(script_options.output_directory)
    if os.path.exists(output_folder):
        shutil.rmtree(output_folder)
    os.makedirs(output_folder, exist_ok=True)

    images_by_path = []
    for annotations_file in script_options.input_annotations_files:
        images_by_path.extend(prepare_images(annotations_file))

    save(output_folder, images_by_path, script_options.output_directory, script_options.train_val_percentage)

    logger.info(f"Processing completed, images: {len(images_by_path)}")


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
        logger.info(
            f"Different number of annotated/unannotated images: {len(annotations.images)} vs {len(all_images)}"
        )

    images = []
    for image in annotations.images:
        matching_image_file = all_images.get(image.name)
        if matching_image_file is None:
            logger.info(f"Failed to find annotated image {image.name} in {images_folder}")
            continue

        if len(image.masks) == 0 and len(image.boxes) == 0:
            logger.info(f"Image {image.name} in {images_folder} is not annotated")

        logger.info(f"Processing {image.name}, boxes: {len(image.boxes)}, masks: {len(image.masks)}")
        boxes = [
            YoloLabeledBBox(
                class_name=box.label,
                bbox=RectangleD.from_ltrb(
                    box.xtl / image.width,
                    box.ytl / image.height,
                    box.xbr / image.width,
                    box.ybr / image.height,
                    ),
                unscaled_bbox=RectangleD.from_ltrb(
                    box.xtl, box.ytl, box.xbr, box.ybr
                ),
            )
            for box in image.boxes
        ]

        yolo_masks = [mask for single_mask in image.masks for mask in parse_to_yolo_labeled_masks(single_mask, image)]
        # CvatMaskConverter.draw_and_show_polygons(image.height, image.width, [yolo_mask.unscaled_mask for yolo_mask in yolo_masks])

        images.append(
            YoloImage(image_file=matching_image_file, bboxes=boxes, masks=yolo_masks)
        )

    return images


def parse_to_yolo_labeled_masks(mask, image):
    logger.info(f"Parsing mask of {image.name}: {mask.label}, {len(mask.rle)} len")

    polygons = CvatMaskConverter.cvat_rle_to_polygon(mask.rle, mask.height, mask.width)
    adjusted_polygons = CvatMaskConverter.adjust_polygon_coords(polygons, mask.left, mask.top)
    logger.info(f"Parsed mask of {image.name} to {len(polygons)} polys")
    # CvatMaskConverter.draw_and_show_polygons(image.height, image.width, adjusted_polygons)

    scaled_polygons = [polygon.astype(np.float32).copy() for polygon in adjusted_polygons]  # convert to float first
    for polygon in scaled_polygons:
        polygon[:, :, 0] /= image.width
        polygon[:, :, 1] /= image.height

    yolo_masks = [
        YoloLabeledMask(
            class_name=mask.label,
            unscaled_mask=polygon,
            mask=scaled_polygon
        )
        for polygon, scaled_polygon in zip(adjusted_polygons, scaled_polygons)
    ]

    return yolo_masks


def save(output_folder, images, use_symlinks, train_val_percentage):
    if not os.path.exists(output_folder):
        os.makedirs(output_folder)

    labels = defaultdict(lambda: None)
    idx = 0

    for image in images:
        for bbox in image.bboxes:
            class_name = bbox.class_name
            if labels[class_name] is None:
                labels[class_name] = YoloLabel(index=idx, name=class_name)
                idx += 1
        for mask in image.masks:
            class_name = mask.class_name
            if labels[class_name] is None:
                labels[class_name] = YoloLabel(index=idx, name=class_name)
                idx += 1

    labels = dict(labels)

    data_yaml_path = os.path.join(output_folder, "data.yaml")
    with open(data_yaml_path, "w") as f:
        f.write(
            f"""train: ../train/images
val: ../valid/images
test: ../test/images

nc: {len(labels)}
names: [{', '.join(f"'{label.name}'" for label in labels.values())}]

"""
        )

    imageTrainCount = train_val_percentage
    imageValidCount = 100 - train_val_percentage
    imageTestCount = 0
    logger.info(
        f"Splitting {len(images)} images to train/valid/test: {imageTrainCount}/{imageValidCount}/{imageTestCount}%"
    )
    names = ["train", "valid", "test"]
    split = split_collection(images, imageTrainCount, imageValidCount, imageTestCount)

    logger.info(f"Writing the results of a split to {output_folder}")
    for i, images_collection in enumerate(split):
        images_collection_name = names[i]
        collection_folder_path = os.path.join(output_folder, images_collection_name)
        images_folder_path = os.path.join(collection_folder_path, "images")
        labels_folder_path = os.path.join(collection_folder_path, "labels")

        logger.info(
            f"Writing '{images_collection_name}' to {collection_folder_path}, count: {len(images_collection)}"
        )

        os.makedirs(images_folder_path, exist_ok=True)
        os.makedirs(labels_folder_path, exist_ok=True)

        for image in images_collection:

            destination_image_file = os.path.join(images_folder_path, os.path.basename(image.image_file))
            if use_symlinks:
                os.symlink(
                    image.image_file,
                    destination_image_file)
            else:
                shutil.copy2(
                    image.image_file,
                    destination_image_file)

            image_boxes = [
                f"{labels[bbox.class_name].index} {bbox.bbox.center_x} {bbox.bbox.center_y} {bbox.bbox.width} {bbox.bbox.height}"
                for bbox in image.bboxes
            ]
            image_masks = [
                f"{labels[mask.class_name].index} {' '.join(f'{float(val):.16f}'.rstrip('0').rstrip('.') for val in mask.mask.flatten())}"
                for mask in image.masks
            ]
            with open(
                    os.path.join(
                        labels_folder_path,
                        f"{os.path.splitext(os.path.basename(image.image_file))[0]}.txt",
                    ),
                    "w",
            ) as f:
                f.write("\n".join(image_boxes))
                f.write("\n".join(image_masks))


def read_file_paths(file_path):
    """Read file paths from a file."""
    try:
        with open(file_path, 'r') as file:
            paths = file.read().splitlines()
            return paths
    except Exception as e:
        logger.error(f"Error reading file paths: {e}")
        return []


def parse_options():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--inputAnnotationsFiles",
        help="Paths to CVAT 1.1 annotation file (annotations.xml)",
        nargs="+",
    )
    parser.add_argument(
        "--inputAnnotationsFileList",
        help="Path to file containing list of paths to CVAT 1.1 annotation files (annotations.xml)",
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
    parser.add_argument(
        "--trainPercentage",
        type=int,
        help="Percentage of images to use for training",
    )
    args = parser.parse_args()

    file_paths = args.inputAnnotationsFiles if args.inputAnnotationsFiles else read_file_paths(args.inputAnnotationsFileList)

    return ScriptOptions(
        input_annotations_files=file_paths,
        output_directory=args.outputDirectory,
        use_symlinks=args.symlinks,
        train_val_percentage=args.trainPercentage
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

        boxes = [
            Box(
                label=box_elem.attrib["label"],
                source=box_elem.attrib["source"],
                occluded=int(box_elem.attrib["occluded"]),
                xtl=float(box_elem.attrib["xtl"]),
                ytl=float(box_elem.attrib["ytl"]),
                xbr=float(box_elem.attrib["xbr"]),
                ybr=float(box_elem.attrib["ybr"]),
                z_order=int(box_elem.attrib["z_order"]),
            )
            for box_elem in image_elem.findall("box")
        ]

        masks = [
            Mask(
                label=mask_elem.attrib["label"],
                source=mask_elem.attrib["source"],
                occluded=int(mask_elem.attrib["occluded"]),
                rle=mask_elem.attrib["rle"],
                left=int(mask_elem.attrib["left"]),
                top=int(mask_elem.attrib["top"]),
                width=int(mask_elem.attrib["width"]),
                height=int(mask_elem.attrib["height"]),
                z_order=int(mask_elem.attrib["z_order"]),
            )
            for mask_elem in image_elem.findall("mask")
        ]

        images.append(
            Image(
                id=id,
                name=name,
                width=width,
                height=height,
                boxes=boxes,
                masks=masks,
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


class RectangleD:
    def __init__(self, x, y, width, height):
        self.x = x
        self.y = y
        self.width = width
        self.height = height

    @property
    def center_x(self):
        return self.x + self.width / float(2)

    @property
    def center_y(self):
        return self.y + self.height / float(2)

    @classmethod
    def from_yolo(cls, center_x, center_y, width, height):
        x = center_x - width / float(2)
        y = center_y - height / float(2)
        return cls(x, y, width, height)

    @classmethod
    def from_ltrb(cls, left, top, right, bottom):
        x = left
        y = top
        width = right - left
        height = bottom - top
        return cls(x, y, width, height)


class Annotations:
    def __init__(self, annotations_file, images):
        self.annotations_file = annotations_file
        self.images = images


class Image:
    def __init__(self, id, name, width, height, boxes, masks):
        self.id = id
        self.name = name
        self.width = width
        self.height = height
        self.boxes = boxes
        self.masks = masks


class Box:
    def __init__(
            self, label, source, occluded, xtl, ytl, xbr, ybr, z_order
    ):
        self.label = label
        self.source = source
        self.occluded = occluded
        self.xtl = xtl
        self.ytl = ytl
        self.xbr = xbr
        self.ybr = ybr
        self.z_order = z_order


@dataclass
class Mask:
    label: str
    source: str
    occluded: int
    rle: str
    left: int
    top: int
    width: int
    height: int
    z_order: int


class YoloLabel:
    def __init__(self, index, name):
        self.index = index
        self.name = name


class YoloImage:
    def __init__(self, image_file, bboxes, masks):
        self.image_file = image_file
        self.bboxes = bboxes
        self.masks = masks


class YoloLabeledBBox:
    def __init__(self, class_name, bbox, unscaled_bbox):
        self.class_name = class_name
        self.bbox = bbox
        self.unscaled_bbox = unscaled_bbox


class YoloLabeledMask:
    def __init__(self, class_name, mask, unscaled_mask):
        self.class_name = class_name
        self.mask = mask
        self.unscaled_mask = unscaled_mask


class ScriptOptions:
    def __init__(self, input_annotations_files, output_directory, use_symlinks, train_val_percentage):
        self.input_annotations_files = input_annotations_files
        self.output_directory = output_directory
        self.use_symlinks = use_symlinks
        self.train_val_percentage = train_val_percentage


class CvatMaskConverter:

    @staticmethod
    def draw_and_show_polygons(height, width, polygons):
        img = np.zeros((height, width, 3), np.uint8)
        polygons = [np.array(poly, dtype=np.int32).reshape((-1, 1, 2)) for poly in polygons]

        for polygon in polygons:
            color = tuple(np.random.randint(0, 255, 3).tolist())
            cv2.fillPoly(img, [polygon], color)
            for point in polygon:
                cv2.circle(img, tuple(point[0]), 1, (255, 255, 255), thickness=-1)

        plt.imshow(img)
        plt.show()

    @staticmethod
    def rle_to_mask(rle_string, height, width):
        rle_numbers = [int(num_string) for num_string in rle_string.split(',')]
        if len(rle_numbers) % 2 != 0:
            rle_numbers = rle_numbers[:-1]
        rle_pairs = np.array(rle_numbers).reshape(-1, 2)
        img = np.zeros([height, width], dtype=np.uint8)
        start = 0
        for index, length in rle_pairs:
            start += index
            for j in range(length):
                img[start // width, start % width] = 255
                start += 1
        return img

    @staticmethod
    def mask_to_polygons(mask):
        polygons = []
        contours, hierarchy = cv2.findContours(mask, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)

        for i in range(len(contours)):
            if hierarchy[0][i][3] != -1:
                continue
            polygon_coords = contours[i].reshape(-1, 2)

            # If the number of points in the contour is less than 3, it cannot define a polygon
            if len(polygon_coords) < 3:
                continue

            polygon = Polygon(polygon_coords)

            if hierarchy[0][i][2] != -1:
                hole_index = hierarchy[0][i][2]
                hole_coords = contours[hole_index].reshape(-1, 2)
                hole = Polygon(hole_coords)
                centroid = hole.centroid

                result = polygon.difference(hole)

                if isinstance(result, GeometryCollection):
                    geometries = [geom for geom in result.geoms if isinstance(geom, Polygon)]
                else:
                    geometries = [result]

                for geom in geometries:
                    for angle in np.linspace(0, 2 * np.pi, 1000):
                        line_candidate = LineString(
                            [(centroid.x + 1000 * np.cos(angle + np.pi), centroid.y + 1000 * np.sin(angle + np.pi)),
                             (centroid.x + 1000 * np.cos(angle), centroid.y + 1000 * np.sin(angle))])
                        split_result = split(geom, line_candidate)

                        if isinstance(split_result, GeometryCollection):
                            split_polygons = [g for g in split_result.geoms if isinstance(g, Polygon)]
                        else:
                            split_polygons = list(split_result)

                        if len(split_polygons) == 2:
                            polygons.extend(split_polygons)
                            break
            else:
                polygons.append(polygon)

        opencv_polygons = []
        for polygon in polygons:
            contour = np.array(polygon.exterior.coords).reshape((-1, 1, 2)).astype(np.int32)
            opencv_polygons.append(contour)

        return opencv_polygons

    @staticmethod
    def cvat_rle_to_polygon(rle_string, image_height, image_width):
        mask = CvatMaskConverter.rle_to_mask(rle_string, image_height, image_width)
        polygons = CvatMaskConverter.mask_to_polygons(mask)
        return polygons

    @staticmethod
    def adjust_polygon_coords(polygons, left, top):
        adjusted_polygons = []
        for polygon in polygons:
            polygon = polygon.copy()
            polygon[:, :, 0] += left
            polygon[:, :, 1] += top
            adjusted_polygons.append(polygon)

        return adjusted_polygons


if __name__ == "__main__":
    main()
