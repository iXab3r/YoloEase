import sys
import os
import platform
import argparse
import logging
import time
import re
import winreg

# Setting up the logger
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def get_passed_arguments(args):
    return args

def get_script_path():
    return os.path.realpath(__file__)

def get_python_version():
    return sys.version

def get_os_version():
    return platform.platform()

def query_nvidia_driver():
    try:
        output = os.popen('nvidia-smi --query-gpu=driver_version --format=csv,noheader').read().strip()
        if output:
            return f"NVIDIA Driver Version: {output}"
    except Exception:
        pass
    return None

def query_amd_driver():
    try:
        registry_path = r'SOFTWARE\AMD\CIM\GL'
        registry_key = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, registry_path)
        driver_version, _ = winreg.QueryValueEx(registry_key, "DriverVersion")
        if driver_version:
            return f"AMD Driver Version: {driver_version}"
    except Exception:
        pass
    return None

def query_dxdiag():
    try:
        output = os.popen('dxdiag /t dxdiag.txt').read()
        with open("dxdiag.txt", "r") as f:
            content = f.read()
            match = re.search(r"Driver Version:\s*(\S+)", content)
            driver_version = match.group(1) if match else "Unknown"
        os.remove("dxdiag.txt")
        return f"DirectX Driver Version: {driver_version}"
    except Exception:
        return "Unknown"

def get_video_driver_version():
    nvidia_version = query_nvidia_driver()
    if nvidia_version:
        return nvidia_version

    amd_version = query_amd_driver()
    if amd_version:
        return amd_version

    return query_dxdiag()


def get_cuda_version():
    try:
        output = os.popen('nvcc --version').read()
        match = re.search(r"release (\S+),", output)
        version = match.group(1) if match else "Unknown"
        return version
    except Exception:
        return "Not Installed"

def get_system_details(args):
    details = {
        "Python Version": get_python_version(),
        "Script Path": get_script_path(),
        "Arguments Passed": get_passed_arguments(args),
        "OS Version": get_os_version(),
        "Video Driver Version": get_video_driver_version(),
        "CUDA Version": get_cuda_version(),
    }
    return details

def main(args):
    methods = [
        get_python_version,
        get_script_path,
        get_passed_arguments,
        get_os_version,
        get_video_driver_version,
        get_cuda_version,
    ]

    for method in methods:
        start_time = time.time()
        result = method(args) if method == get_passed_arguments else method()
        elapsed_time = time.time() - start_time
        logger.info(f"{method.__name__}: {result} (Executed in {elapsed_time:.4f} seconds)")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Display various system details for Windows")
    parser.add_argument("--arg1", help="Example argument 1")
    parser.add_argument("--arg2", help="Example argument 2")
    args = parser.parse_args()

    main(args)
