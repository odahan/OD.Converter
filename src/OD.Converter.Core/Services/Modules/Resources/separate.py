"""Separate aligned PCM with the verified local CPU model, without model discovery or downloads."""

from pathlib import Path
import sys

import numpy as np
import soundfile as sf
import torch
from demucs.apply import apply_model
from demucs.states import load_model


def main():
    """Retain the full timeline and write float stems without independent gain normalization."""
    checkpoint, source, output = (Path(value) for value in sys.argv[1:])
    torch.set_num_threads(2)
    torch.manual_seed(0)
    # Only the checkpoint verified by ModuleManager is loaded as a model package.
    model = load_model(torch.load(checkpoint, map_location="cpu", weights_only=False)).eval().cpu()
    samples, rate = sf.read(source, dtype="float32", always_2d=True)
    if rate != model.samplerate or samples.shape[1] != model.audio_channels:
        raise ValueError("The prepared PCM must match the model sample rate and channel count.")
    if not np.isfinite(samples).all():
        raise ValueError("Input PCM contains non-finite samples.")
    mix = torch.from_numpy(samples.T.copy())
    reference = mix.mean(0)
    mean, deviation = reference.mean(), reference.std(unbiased=False)
    if deviation > 1e-8:
        normalized = (mix - mean) / deviation
        with torch.inference_mode():
            estimates = apply_model(model, normalized[None], device="cpu", shifts=0,
                                    split=True, overlap=0.25, progress=True, num_workers=0)[0]
        estimates = estimates * deviation + mean
    else:
        # Constant signals have no separable information; preserve them in the background.
        estimates = torch.zeros(len(model.sources), *mix.shape)
    vocals = estimates[model.sources.index("vocals")]
    background = estimates[[i for i, name in enumerate(model.sources) if name != "vocals"]].sum(0)
    if deviation <= 1e-8:
        background = mix
    if vocals.shape != mix.shape or background.shape != mix.shape:
        raise ValueError("Demucs changed the timeline length.")
    output.mkdir(exist_ok=False)
    for name, stem in (("vocals", vocals), ("background", background)):
        if not torch.isfinite(stem).all():
            raise ValueError("Demucs produced non-finite samples.")
        sf.write(output / (name + ".wav"), stem.T.numpy(), rate, subtype="FLOAT")


if __name__ == "__main__":
    main()
