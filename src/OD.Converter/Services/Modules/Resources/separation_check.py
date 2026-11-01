"""Check the pinned CPU stack and locally verified htdemucs weights offline."""

import importlib.metadata
from pathlib import Path
import sys

import soundfile
import torch
import torchaudio
from demucs.states import load_model


def main():
    """Load only the manager-verified checkpoint and execute a short CPU inference."""
    assert importlib.metadata.version("demucs") == "4.0.1"
    assert torch.__version__ == "2.6.0+cpu"
    assert torchaudio.__version__ == "2.6.0+cpu"
    assert torch.version.cuda is None
    assert soundfile.__version__ == "0.13.1"
    torch.set_num_threads(2)

    # Demucs checkpoints contain model classes. Only this hash-pinned local file
    # is accepted; unrestricted checkpoint loading must never accept media input.
    checkpoint = Path(sys.argv[1]).resolve(strict=True)
    package = torch.load(checkpoint, map_location="cpu", weights_only=False)
    model = load_model(package).eval().cpu()
    assert model.samplerate == 44100 and model.audio_channels == 2
    assert "vocals" in model.sources
    # The smoke check exercises a short window without padding it to the full
    # training segment. Normal separation retains its own engine configuration.
    model.use_train_segment = False
    samples = torch.zeros(1, 2, 44100)
    with torch.inference_mode():
        result = model(samples)
    assert result.shape == (1, len(model.sources), 2, 44100)
    assert torch.isfinite(result).all()
    print("Demucs CPU runtime, dependencies, local model and inference passed.")


if __name__ == "__main__":
    main()
