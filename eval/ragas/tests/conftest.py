import shutil
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
FIXTURES = Path(__file__).resolve().parent / "fixtures"


@pytest.fixture
def dataset_dir(tmp_path):
    return Path(shutil.copytree(FIXTURES / "dataset", tmp_path / "dataset"))


@pytest.fixture
def run_dir(tmp_path):
    return Path(shutil.copytree(FIXTURES / "run", tmp_path / "run"))
