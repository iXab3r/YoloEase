import argparse
import logging
import time 
import os
import sys

from cvat_sdk import make_client
from cvat_sdk.core.proxies.tasks import ResourceType

# Set up logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def print_script_info():
    """Logs the script location and the arguments it was run with."""
    script_location = os.path.realpath(__file__)
    script_args = sys.argv[1:]  # Exclude the script name itself

    logger.debug(f"Script location: {script_location}")
    logger.debug(f"Script arguments: {script_args}")

def read_file_paths(file_path):
    """Read file paths from a file."""
    try:
        with open(file_path, 'r') as file:
            paths = file.read().splitlines()
            return paths
    except Exception as e:
        logger.error(f"Error reading file paths: {e}")
        return []

def test_connectivity(args):
    """Test connectivity to CVAT server."""
    try:
        with make_client(host=args.host, port=args.port, credentials=(args.username, args.password)) as client:
            logger.info("Connection successful!")
    except Exception as e:
        logger.error(f"Connection failed: {e}")

def create_task(args):
    """Create a new task in CVAT."""
    start_time = time.time()  # Record start time

    try:
        with make_client(host=args.host, port=args.port, credentials=(args.username, args.password)) as client:
            client.organization_slug = args.organization
            
            task_spec = {
                "name": args.task_name,
                "labels": [],
                "project_id": args.project_id,
                "image_quality": 100 # Set image quality to 100%, by default it is only 70%
            }

            file_paths = args.file_paths if args.file_paths else read_file_paths(args.file_list)

            # Print the list of files being uploaded
            logger.info(f"Uploading the following files(projectId: {args.project_id}, org: '{args.organization}'):")
            for index, file_path in enumerate(file_paths, start=1):
                logger.info(f"{index:3}: {file_path}")

            task = client.tasks.create_from_data(
                spec=task_spec,
                resource_type=ResourceType.LOCAL,
                resources=file_paths,
                status_check_period=1
            )

            task_id = task.id
            logger.info(f"Task {args.task_name} created successfully.")
            logger.info(f"Created task ID: {task_id}")
            task.fetch()
    except Exception as e:
        logger.error(f"Failed to create task: {e}")

    end_time = time.time()  # Record end time
    total_time = end_time - start_time  # Calculate total time
    logger.info(f"Total time taken: {total_time:.2f} seconds")

# Main argument parser
print_script_info()

parser = argparse.ArgumentParser(description="CVAT operations")
parser.add_argument("operation", choices=["test", "task.create"], help="Operation to perform")
parser.add_argument("--host", required=True, help="CVAT host address")
parser.add_argument("--port", type=int, help="CVAT port number")
parser.add_argument("--username", required=True, help="CVAT username")
parser.add_argument("--password", required=True, help="CVAT password")
parser.add_argument("--organization", default="", help="Organization name (optional)")

args, unknown = parser.parse_known_args()

if args.operation == "test":
    test_connectivity(args)
elif args.operation == "task.create":
    task_parser = argparse.ArgumentParser()
    task_parser.add_argument("--project-id", type=int, required=True, help="CVAT project id")
    task_parser.add_argument("--task-name", required=True, help="Name of the task")
    task_parser.add_argument("--file-list", help="Path to a file containing a list of files to upload, one per line")
    task_parser.add_argument("--file-paths", nargs='*', help="List of file paths to upload")

    task_args = task_parser.parse_args(unknown, namespace=args)
    create_task(task_args)